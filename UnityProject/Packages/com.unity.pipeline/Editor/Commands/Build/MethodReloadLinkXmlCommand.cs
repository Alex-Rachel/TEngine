using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.BuildProcessors;

namespace Unity.Pipeline.Editor.Commands.Build
{
    /// <summary>
    /// CLI face of <see cref="MethodReloadLinkXml"/>: generate (or delete) the on-demand
    /// method-reload link.xml so an agent or CI job can refresh it before a device build.
    /// </summary>
    static class MethodReloadLinkXmlCommand
    {
        const string Group = "script.methodreload";

        [CliGroupCommand(Group,
            "Generate ProjectSettings/Packages/com.unity.pipeline/MethodReload.link.xml: per-type " +
            "preservation of the engine/package types the game's assemblies reference and of the game's own " +
            "types, plus the interpreter's host surface and [MethodReload] targets. Applied to IL2CPP development " +
            "builds only, while the Pipeline is enabled in development builds. Pass delete=true to remove the file instead.",
            MainThreadRequired = true, Tags = new[] { "build", "scripts/methodreload" })]
        public static object GenerateLinkXml(
            [CliArg("delete", "Remove the generated file instead of (re)generating it.")] bool delete = false)
        {
            if (delete)
            {
                var existed = MethodReloadLinkXml.Exists;
                MethodReloadLinkXml.Delete();
                return new { success = true, deleted = existed, path = MethodReloadLinkXml.FileRelativePath };
            }

            var stats = MethodReloadLinkXml.Generate();
            return new
            {
                success = true,
                path = MethodReloadLinkXml.FileRelativePath,
                message = stats.ToString(),
                stats,
            };
        }
    }
}
