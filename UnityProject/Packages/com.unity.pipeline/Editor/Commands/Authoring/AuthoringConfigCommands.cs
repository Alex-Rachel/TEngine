using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;

namespace Unity.Pipeline.Editor.Commands.Authoring
{
    /// <summary>
    /// Commands to inspect and configure the authoring root — the base folder (under Assets/) that
    /// bare authoring paths resolve against and that authoring writes are confined to. Persisted
    /// per project; the default is "Assets" (full Assets access).
    /// </summary>
    static class AuthoringConfigCommands
    {
        const string Group = "authoring";

        [CliGroupCommand(Group, "Get the base folder (under Assets/) that bare authoring paths resolve against.", Tags = new[] { "authoring" })]
        public static object GetRoot()
        {
            return new { root = ProjectPaths.AuthoringRoot };
        }

        [CliGroupCommand(Group, "Set the base folder (under Assets/) that bare authoring paths resolve against and are confined to. Use 'Assets' for full project access.", Tags = new[] { "authoring" })]
        public static object SetRoot(
            [CliArg("root", "Project-relative folder under Assets/, e.g. Assets/AgentWork. Use 'Assets' to allow the whole project.", Required = true)] string root)
        {
            // Throws ArgumentException for invalid roots (outside Assets/ or containing ".."); the
            // server surfaces that message to the caller.
            ProjectPaths.AuthoringRoot = root;
            return new { root = ProjectPaths.AuthoringRoot };
        }
    }
}
