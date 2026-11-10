using Unity.Pipeline.Commands;
using UnityEngine;

namespace Unity.Pipeline.Runtime.Commands
{
    /// <summary>
    /// Runtime logging commands for Unity Player builds.
    /// Provides console logging capabilities from CLI.
    /// </summary>
    static class RuntimeLogCommand
    {
        const string Group = "console";

        [CliGroupCommand(Group, "Write a message to Unity console", MainThreadRequired = true, Tags = new[] { "observability/console", "runtime" })]
        public static string Write(
            [CliArg("message", "Message to log to console", Required = true)] string message,
            [CliArg("level", "Log level: info, warning, error")] string level = "info")
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return "Error: Message cannot be empty";
            }

            Debug.developerConsoleVisible = false;
            var logLevel = level?.ToLowerInvariant() ?? "info";

            switch (logLevel)
            {
                case "info":
                case "log":
                    Debug.Log($"[Pipeline] {message}");
                    return $"Logged info message: {message}";

                case "warning":
                case "warn":
                    Debug.LogWarning($"[Pipeline] {message}");
                    return $"Logged warning message: {message}";

                case "error":
                case "err":
                    Debug.LogError($"[Pipeline] {message}");
                    return $"Logged error message: {message}";

                default:
                    Debug.LogWarning($"[Pipeline] Unknown log level '{level}', defaulting to info: {message}");
                    Debug.Log($"[Pipeline] {message}");
                    return $"Logged message with default level: {message}";
            }
        }
    }
}