#if UNITY_6000_3_OR_NEWER && MPPM_AVAILABLE
namespace Unity.Pipeline.Editor.Commands
{
    // Shared DTOs for the Play Mode Scenario commands. Available whenever MPPM reflection is
    // (6.3+), so both the 6.5+/6.7 implementation (PlayModeScenarioCommands.cs) and the 6.3
    // implementation (PlayModeScenarioCommands63.cs) can consume them, as can MppmReflection.
    internal sealed class ScenarioPlayer
    {
        public string Id;
        public string Name;
        public string Type;
        public string Controller;
        public string RunMode;
    }

    internal sealed class MppmPlayer
    {
        public string Name;
        public string State;
        public string VpId;
    }

    // create_scenario never activates the scenario it writes, and an agent driving the CLI only
    // sees the JSON response, so the response has to say so and give the next command.
    internal static class ScenarioCreateNotes
    {
        public const string DescriptionSuffix =
            " Does not make the new scenario active: run editor.playmode set_active_scenario --path <path> next, " +
            "before add_player or remove_player without --path, or start_scenario. The response's 'note' gives the exact command.";

        public static string NotActive(string path) =>
            "The new scenario is not active yet. editor.playmode add_player and remove_player without --path, and start_scenario, " +
            $"act on the active scenario. Activate this one with: editor.playmode set_active_scenario --path {QuotePath(path)}";

        // Quotes a path so the suggested command tokenizes back to the same path, both through
        // CommandLineTokenizer and when pasted into a POSIX shell. A path of only plain characters
        // stays bare; anything else is double-quoted with \, ", $ and ` escaped, which both read
        // as the literal character.
        internal static string QuotePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "\"\"";

            var plain = true;
            foreach (var c in path)
            {
                if (!(char.IsLetterOrDigit(c) || c == '/' || c == '.' || c == '_' || c == '-'))
                {
                    plain = false;
                    break;
                }
            }
            if (plain)
                return path;

            var quoted = new System.Text.StringBuilder(path.Length + 2).Append('"');
            foreach (var c in path)
            {
                if (c == '\\' || c == '"' || c == '$' || c == '`')
                    quoted.Append('\\');
                quoted.Append(c);
            }
            return quoted.Append('"').ToString();
        }
    }
}
#endif
