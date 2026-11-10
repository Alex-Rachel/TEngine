using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;            // IUnityLinkerProcessor lives here on 6000.5; keep both usings
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEditor.UnityLinker;
using UnityEngine;
using Unity.Pipeline.MethodReload;
using Unity.Pipeline.Compilation;
using Unity.Pipeline.Config;

namespace Unity.Pipeline.Editor.BuildProcessors
{
    /// <summary>
    /// An on-demand <c>link.xml</c> for on-device method reload, generated once from the
    /// Project Settings page (or <c>script.methodreload generate_link_xml</c>) into
    /// <c>ProjectSettings/Packages/com.unity.pipeline/MethodReload.link.xml</c>, and handed to
    /// UnityLinker by the tiny <see cref="IUnityLinkerProcessor"/> below for development builds only.
    ///
    /// Why a file and not a build step: the scan is the expensive, opaque part. Done once, the result
    /// is a reviewable, diffable project file and builds stay fast. Why under ProjectSettings and not
    /// Assets: Unity applies any <c>link.xml</c> under Assets to every build, release included.
    ///
    /// What it preserves, and at what granularity:
    /// <list type="bullet">
    /// <item>the interpreter engine and its metadata reader, wholesale (three small assemblies);</item>
    /// <item>the curated host surface (<see cref="IlInterpreterHostBindings.CreateStandard"/>) and the
    /// <c>[MethodReload]</c> target types, per type;</item>
    /// <item>every engine / package type the player assemblies reference, per type with all members —
    /// so a reloaded body can call any other member of a type the game already uses;</item>
    /// <item>every type the player assemblies define, per type — so a reloaded body can call any game
    /// method. Under Minimal stripping user assemblies are not stripped anyway, so this is free there.</item>
    /// </list>
    /// Never whole engine modules or whole user assemblies. A type the game never touches at all
    /// stays strippable; the push-time warning names it, and the project can preserve it with its
    /// own <c>link.xml</c> under Assets.
    ///
    /// The metadata walk itself lives in <see cref="PlayerAssemblyMetadataWalk"/>; the settings-page
    /// section that drives this class is drawn by <c>RuntimePipelineConfigEditor</c>.
    /// </summary>
    class MethodReloadLinkXml : IUnityLinkerProcessor
    {
        public const string FileRelativePath = "ProjectSettings/Packages/com.unity.pipeline/MethodReload.link.xml";

        public static string AbsolutePath =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", FileRelativePath));

        public static bool Exists => File.Exists(AbsolutePath);

        // ---- build-time hook: hand the pre-generated file over, nothing else -------------------

        public int callbackOrder => 0;

        public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data)
        {
            // Release players compile the interpreter out; without the development define the Pipeline
            // is not in the player, so nothing receives a reload push. Either way there is nothing to
            // preserve for. By now the editor has switched to the build's target, so the active
            // target's defines are the build's.
            if (!IsInstrumentedBuild(report) || !Exists ||
                !PipelineBuildSymbols.IsDefinedForActiveTarget(PipelineBuildSymbols.Development))
                return string.Empty;

            // The file is a snapshot of the assemblies compiled for the target that was active when it
            // was generated; a type referenced only under another target's defines is missing from it.
            // Generating it was the opt-in, so refreshing a stale snapshot is what the user wants: by
            // now the editor has switched to the build's target and recompiled the script assemblies
            // with its defines, so regenerating here (~100 ms) yields a file exact for this build.
            var recorded = ReadRecordedBuildTarget(AbsolutePath);
            var target = report.summary.platform.ToString();
            if (recorded != null && recorded != target)
            {
                var stats = Generate();
                Debug.Log($"Pipeline: regenerated the method-reload link.xml ({FileRelativePath}) for {target}; it was " +
                    $"generated with {recorded} as the active build target — {stats}");
            }
            else
            {
                Debug.Log($"Pipeline: including the on-demand method-reload link.xml ({FileRelativePath}, " +
                    $"generated {File.GetLastWriteTimeUtc(AbsolutePath):u}). Regenerate it from Project Settings > " +
                    "Pipeline > Runtime > Method Reload after larger code changes.");
            }
            return AbsolutePath;
        }

        static bool IsInstrumentedBuild(BuildReport report)
        {
#if UNITY_7000_0_OR_NEWER
            return PlayerSettings.GetManagedCodeVariant(report.summary.namedBuildTarget) <= ManagedCodeVariant.Instrumented;
#else
            return report != null && report.summary.options.HasFlag(BuildOptions.Development);
#endif
        }

        const string BuildTargetHeaderKey = "active build target: ";

        /// <summary>The build target recorded in the file's header comment, or null if absent.</summary>
        internal static string ReadRecordedBuildTarget(string path)
        {
            try
            {
                foreach (var line in File.ReadLines(path))
                {
                    var i = line.IndexOf(BuildTargetHeaderKey, StringComparison.Ordinal);
                    if (i >= 0) return line.Substring(i + BuildTargetHeaderKey.Length).Trim();
                    if (line.Contains("-->")) break; // header comment over, no record
                }
            }
            catch (IOException) { }
            return null;
        }

        // ---- generation --------------------------------------------------------------------------

        public sealed class Stats
        {
            public int WholesaleAssemblies;
            public int HostSurfaceTypes;
            public int ReloadTargetTypes;
            public int ReferencedTypes;
            public int ReferencedAssemblies;
            public int UserTypes;
            public int UserAssemblies;
            public int TotalTypes;
            public long Bytes;

            public override string ToString() =>
                $"{TotalTypes} type(s): {ReferencedTypes} engine/package type(s) referenced by user code across " +
                $"{ReferencedAssemblies} assembly(ies), {UserTypes} user type(s) across {UserAssemblies} " +
                $"assembly(ies), {HostSurfaceTypes} host-surface type(s), {ReloadTargetTypes} [MethodReload] " +
                $"target(s); {WholesaleAssemblies} interpreter assembly(ies) wholesale; {Bytes / 1024} KB";
        }

        /// <summary>Generate and write the file. Main thread (CompilationPipeline).</summary>
        public static Stats Generate()
        {
            var xml = Build(out var stats);
            var path = AbsolutePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, xml);
            stats.Bytes = new FileInfo(path).Length;
            return stats;
        }

        public static void Delete()
        {
            if (Exists) File.Delete(AbsolutePath);
        }

        /// <summary>Summary of the file on disk for the settings page; null when absent.</summary>
        public static string Describe()
        {
            if (!Exists) return null;
            var text = File.ReadAllText(AbsolutePath);
            var types = Regex.Matches(text, "<type ").Count;
            var wholesale = Regex.Matches(text, "<assembly [^>]*preserve=").Count;
            return $"{types} type(s), {wholesale} assembly(ies) wholesale, {new FileInfo(AbsolutePath).Length / 1024} KB, " +
                $"generated {File.GetLastWriteTime(AbsolutePath):g}";
        }

        /// <summary>Pure: the link.xml text for the current project state.</summary>
        public static string Build(out Stats stats)
        {
            stats = new Stats();
            var byAssembly = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);

            // 1. Interpreter engine: parses pushed IL via System.Reflection.Metadata and resolves host
            //    members by name, so those paths look unused to the stripper.
            var wholesale = new SortedSet<string>(s_InterpreterEngineAssemblies, StringComparer.Ordinal);
            stats.WholesaleAssemblies = wholesale.Count;

            // 2. Curated host surface, per type.
            var binding = IlInterpreterHostBindings.CreateStandard();
            foreach (var t in binding.RegisteredTypes)
                if (AddClrType(byAssembly, t)) stats.HostSurfaceTypes++;

            // Every assembly the player build compiles, and within those the game's own: sources under
            // Assets/. Package runtime assemblies (Unity.Pipeline, Unity.InputSystem, a UPM sample …)
            // are dependencies, not code a reloaded body extends, so only game assemblies get every
            // type enumerated below.
            var playerAssemblyNames = new HashSet<string>(StringComparer.Ordinal);
            var userPlayerAssemblies = new List<UnityEditor.Compilation.Assembly>();
            var userAssemblyNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var asm in CompilationPipeline.GetAssemblies(AssembliesType.Player))
            {
                playerAssemblyNames.Add(asm.name);
                if (!IsUserAssembly(asm)) continue;
                userPlayerAssemblies.Add(asm);
                userAssemblyNames.Add(asm.name);
            }

            // 3. [MethodReload] targets in any player assembly — a package component is as much a reload
            //    target as a game one, and its private fields must survive for the reflection walk.
            //    (The loaded-assembly scan also sees editor and test fixtures; those are not in a game
            //    build, so they are dropped here.)
            foreach (var t in SelectReloadTargets(CollectReloadableTargetTypes(), playerAssemblyNames))
                if (AddClrType(byAssembly, t))
                    stats.ReloadTargetTypes++;

            // 4. Referenced engine/package types + own types, straight from metadata.
            var referencedAssemblies = new HashSet<string>(StringComparer.Ordinal);
            var userAssemblies = new HashSet<string>(StringComparer.Ordinal);
            foreach (var asm in userPlayerAssemblies)
            {
                if (string.IsNullOrEmpty(asm.outputPath) || !File.Exists(asm.outputPath))
                    continue;
                PlayerAssemblyMetadataWalk.Scan scan;
                try
                {
                    scan = PlayerAssemblyMetadataWalk.Read(asm.outputPath);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Pipeline: could not read '{asm.outputPath}' for the method-reload link.xml: {ex.Message}");
                    continue;
                }
                if (scan == null) continue;

                userAssemblies.Add(scan.AssemblyName);
                foreach (var type in scan.DefinedTypes)
                    if (Add(byAssembly, scan.AssemblyName, type)) stats.UserTypes++;
                foreach (var (assembly, type) in scan.ReferencedTypes)
                {
                    // Cross-references between game assemblies are covered by their own definitions.
                    if (userAssemblyNames.Contains(assembly)) continue;
                    if (!Add(byAssembly, assembly, type)) continue;
                    stats.ReferencedTypes++;
                    referencedAssemblies.Add(assembly);
                }
            }
            stats.ReferencedAssemblies = referencedAssemblies.Count;
            stats.UserAssemblies = userAssemblies.Count;
            foreach (var kv in byAssembly)
                if (!wholesale.Contains(kv.Key)) stats.TotalTypes += kv.Value.Count;

            var sb = new StringBuilder();
            sb.AppendLine("<linker>");
            sb.AppendLine("  <!--");
            sb.AppendLine("    GENERATED by com.unity.pipeline (Project Settings > Pipeline > Runtime, or");
            sb.AppendLine("    unity command script.methodreload generate_link_xml). Applied to DEVELOPMENT builds only,");
            sb.AppendLine("    and only while the");
            sb.AppendLine("    runtime Pipeline is enabled in builds. Preserves, per type, what method reloaded onto an");
            sb.AppendLine("    IL2CPP device may reach: the interpreter's host surface, [MethodReload] targets, every");
            sb.AppendLine("    engine/package type the game's assemblies reference, and the game's own types.");
            sb.AppendLine("    Regenerate after larger code changes; delete to fall back to plain stripping.");
            sb.AppendLine($"    {DateTime.UtcNow:u} — {stats}");
            // The compiled assemblies carry the active target's defines, so a type referenced only under
            // another target is missing here; the linker hook regenerates when the build target differs.
            sb.AppendLine($"    {BuildTargetHeaderKey}{EditorUserBuildSettings.activeBuildTarget}");
            sb.AppendLine("  -->");
            foreach (var name in wholesale)
                sb.AppendLine($"  <assembly fullname=\"{Escape(name)}\" preserve=\"all\"/>");
            foreach (var kv in byAssembly)
            {
                if (wholesale.Contains(kv.Key)) continue;
                sb.AppendLine($"  <assembly fullname=\"{Escape(kv.Key)}\">");
                foreach (var type in kv.Value)
                    sb.AppendLine($"    <type fullname=\"{Escape(type)}\" preserve=\"all\"/>");
                sb.AppendLine("  </assembly>");
            }
            sb.AppendLine("</linker>");
            return sb.ToString();
        }

        // Game code lives under Assets/; anything compiled from Packages/ (including this package's
        // own runtime assemblies) is a dependency, not something a reloaded body extends.
        static bool IsUserAssembly(UnityEditor.Compilation.Assembly asm)
        {
            var sources = asm.sourceFiles;
            if (sources == null || sources.Length == 0) return false;
            foreach (var s in sources)
                if (s.StartsWith("Assets/", StringComparison.Ordinal)) return true;
            return false;
        }

        static readonly string[] s_InterpreterEngineAssemblies =
        {
            "Unity.Pipeline.IlInterpreter",
            "UnityPipeline.System.Reflection.Metadata",
            "UnityPipeline.System.Collections.Immutable",
        };

        // Both type and assembly names go through this: an asmdef may legally be named "Game&Tools".
        internal static string Escape(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

        // ---- reflection-side sources (host surface, [MethodReload] targets) ------------------------

        static bool AddClrType(SortedDictionary<string, SortedSet<string>> byAssembly, Type t)
        {
            // Preserve the open definition for constructed generics (a closed-generic fullname is
            // assembly-qualified and won't match in link.xml).
            var def = t.IsGenericType && !t.IsGenericTypeDefinition ? t.GetGenericTypeDefinition() : t;
            var asm = def.Assembly.GetName().Name;
            var full = def.FullName;
            if (string.IsNullOrEmpty(asm) || string.IsNullOrEmpty(full)) return false;
            return Add(byAssembly, asm, full.Replace('+', '/'));
        }

        // Record a type to preserve under its owning assembly; false if it was already there.
        static bool Add(SortedDictionary<string, SortedSet<string>> byAssembly, string asm, string typeName)
        {
            if (!byAssembly.TryGetValue(asm, out var types))
                byAssembly[asm] = types = new SortedSet<string>(StringComparer.Ordinal);
            return types.Add(typeName);
        }

        /// <summary>
        /// The reload targets that end up in a player build: any candidate whose assembly the player
        /// compiles, game code and UPM packages alike. Editor and test fixtures fall out here.
        /// </summary>
        internal static IEnumerable<Type> SelectReloadTargets(IEnumerable<Type> candidates, ISet<string> playerAssemblyNames)
        {
            foreach (var t in candidates)
                if (playerAssemblyNames.Contains(t.Assembly.GetName().Name))
                    yield return t;
        }

        /// <summary>
        /// Types carrying a class-level <c>[MethodReload]</c> or any <c>[MethodReload]</c> method, from
        /// every loaded assembly that references the attributes' assembly.
        /// </summary>
        internal static IEnumerable<Type> CollectReloadableTargetTypes()
        {
            const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            var pipeline = typeof(MethodReloadAttribute).Assembly.GetName().Name;
            var results = new List<Type>();
            foreach (var asm in PipelineUtils.GetLoadedAssemblies())
            {
                bool references = false;
                try
                {
                    foreach (var r in asm.GetReferencedAssemblies())
                        if (r.Name == pipeline) { references = true; break; }
                }
                catch { }
                if (!references) continue;

                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                catch { continue; }

                foreach (var t in types)
                {
                    if (t == null) continue;
                    try
                    {
                        if (t.IsDefined(typeof(MethodReloadAttribute), inherit: false)) { results.Add(t); continue; }
                        foreach (var m in t.GetMethods(Flags))
                            if (m.IsDefined(typeof(MethodReloadAttribute), inherit: false)) { results.Add(t); break; }
                    }
                    catch { }
                }
            }
            return results;
        }
    }
}
