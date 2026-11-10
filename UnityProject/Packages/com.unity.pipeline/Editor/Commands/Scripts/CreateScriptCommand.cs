using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;
using Unity.Pipeline.Models;
using UnityEditor;

namespace Unity.Pipeline.Editor.Commands.Scripts
{
    /// <summary>
    /// Authoring command that writes a new C# MonoBehaviour (or other base-class) script from a
    /// template into the project under the authoring root (CLI-195).
    ///
    /// IMPORTANT — the compile/domain-reload boundary:
    /// Writing a .cs file does NOT make the type available. Unity must import and compile the new
    /// file, which triggers a domain reload, before the type exists and can be attached. The agent
    /// workflow is therefore:
    ///   1. create_script         (this command — writes the file)
    ///   2. recompile             (triggers compilation; see <see cref="RecompileCommand"/>)
    ///   3. poll recompile_status (wait until "completed" / "up_to_date")
    ///   4. attach_script         (now the type exists; see <see cref="AttachScriptCommand"/>)
    /// This command intentionally does its part only: it creates the file and returns the asset
    /// identity. It does not trigger a recompile itself — the agent owns that step so it can batch
    /// multiple authoring writes before paying the domain-reload cost once.
    /// </summary>
    static class CreateScriptCommand
    {
        const string Group = "script";

        /// <summary>
        /// Default class body (the Start/Update stubs) written inside the generated class, matching
        /// Unity's own new-MonoBehaviour template so the output reads like a hand-authored script.
        /// <see cref="BuildSource"/> wraps it with the using/class declaration and an optional namespace.
        /// </summary>
        private const string DefaultBody =
            "    // Use this for initialization\n" +
            "    void Start()\n" +
            "    {\n\n" +
            "    }\n\n" +
            "    // Update is called once per frame\n" +
            "    void Update()\n" +
            "    {\n\n" +
            "    }\n";

        [CliGroupCommand(Group,
            "Create a new C# script (default base class MonoBehaviour) from a template under the authoring root. " +
            "NOTE: the type does not exist until a recompile completes — to attach it, call " + CommandNames.ScriptRecompile + ", poll " + CommandNames.ScriptRecompileStatus + ", then " + CommandNames.ScriptAttach + ".",
            Tags = new[] { "scripts" })]
        public static AuthoringResult Create(
            [CliArg("name", "Class/file name without extension, e.g. PlayerController. Must be a valid C# identifier.", Required = true)] string name,
            [CliArg("path", "Folder (relative to the authoring root; the Assets/ prefix is optional) to write the .cs into. Defaults to the authoring root.")] string path = null,
            [CliArg("namespace", "Optional namespace to wrap the class in. Omit for the global namespace.")] string @namespace = null,
            [CliArg("base-class", "Base class to derive from. Defaults to MonoBehaviour.")] string baseClass = "MonoBehaviour",
            [CliArg("overwrite", "Overwrite the file if it already exists. Defaults to false (an existing file is an error).")] bool overwrite = false)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Script 'name' is required.");

            var className = name.Trim();
            if (className.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                className = className.Substring(0, className.Length - 3);

            if (!IsValidIdentifier(className))
                throw new ArgumentException($"Script name '{className}' is not a valid C# class identifier.");

            if (string.IsNullOrWhiteSpace(baseClass))
                baseClass = "MonoBehaviour";

            // Resolve & sandbox the destination folder through the project-path policy. A null/empty
            // path means "the authoring root itself" — Resolve treats an empty path as an error, so
            // fall back to the configured root in that case (it is already a confined, valid path).
            string folder;
            if (string.IsNullOrWhiteSpace(path))
            {
                folder = ProjectPaths.AuthoringRoot;
            }
            else
            {
                folder = ProjectPaths.Resolve(path, out var error);
                if (folder == null)
                    throw new ArgumentException(error);
            }

            if (!AssetDatabase.IsValidFolder(folder))
                throw new ArgumentException(
                    $"Destination folder '{folder}' does not exist. Create it first with {CommandNames.AssetCreateFolder}.");

            var assetPath = $"{folder}/{className}.cs";
            if (File.Exists(ToAbsolute(assetPath)) && !overwrite)
                throw new ArgumentException(
                    $"A script already exists at '{assetPath}'. Pass overwrite=true to replace it.");

            var source = BuildSource(className, @namespace, baseClass);

            // Write through the filesystem then import: AssetDatabase has no "create text asset" API,
            // and CreateAsset is for UnityEngine.Object instances, not source files.
            File.WriteAllText(ToAbsolute(assetPath), source, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            // The MonoScript may already be loadable (its asset entry exists), but its compiled Type
            // will not exist until the next domain reload — see the class doc.
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(assetPath);
            var result = ObjectResolver.Describe(script) ?? new AuthoringResult { Type = "MonoScript" };
            result.AssetPath = assetPath;
            return result;
        }

        /// <summary>
        /// Build the script source from the template, substituting the class name, optional namespace
        /// and base class. When a namespace is supplied the class body is indented one extra level.
        /// </summary>
        private static string BuildSource(string className, string @namespace, string baseClass)
        {
            var usings = "using UnityEngine;\n\n";
            var classDecl = $"public class {className} : {baseClass}\n";

            var sb = new StringBuilder();
            sb.Append(usings);

            if (!string.IsNullOrWhiteSpace(@namespace))
            {
                sb.Append($"namespace {@namespace}\n{{\n");
                sb.Append(Indent(classDecl, "    "));
                sb.Append("    {\n");
                sb.Append(Indent(DefaultBody, "    "));
                sb.Append("    }\n");
                sb.Append("}\n");
            }
            else
            {
                sb.Append(classDecl);
                sb.Append("{\n");
                sb.Append(DefaultBody);
                sb.Append("}\n");
            }

            return sb.ToString();
        }

        private static string Indent(string text, string prefix)
        {
            var sb = new StringBuilder();
            foreach (var line in text.Split('\n'))
                sb.Append(line.Length == 0 ? "\n" : prefix + line + "\n");
            // Split adds a trailing empty element for a trailing newline; trim the extra newline we
            // appended for it.
            if (text.EndsWith("\n") && sb.Length > 0)
                sb.Length -= 1;
            return sb.ToString();
        }

        private static bool IsValidIdentifier(string value)
        {
            // A C# identifier: starts with a letter or underscore, then letters/digits/underscores.
            return Regex.IsMatch(value, @"^[A-Za-z_][A-Za-z0-9_]*$");
        }

        private static string ToAbsolute(string assetPath)
        {
            // assetPath is project-relative ("Assets/..."); ProjectRoot is the folder containing Assets/.
            return Path.Combine(ProjectPaths.ProjectRoot, assetPath).Replace('\\', '/');
        }
    }
}
