using System.Collections.Generic;

namespace Unity.Pipeline
{
    /// <summary>
    /// The Command Not Found message for a flat name commands had before they were grouped: it
    /// names the full name that replaced it, which is usually too far from the old name for a
    /// "Did you mean" suggestion.
    /// </summary>
    internal static class ObsoleteCommandNameMessage
    {
        /// <param name="typedName">The name that matched no command.</param>
        /// <param name="obsoleteNames">Old name to full name, or null on a server that has none.</param>
        /// <returns>The message, or null when <paramref name="typedName"/> is not an old name.</returns>
        internal static string ForMiss(string typedName, IReadOnlyDictionary<string, string> obsoleteNames)
        {
            if (typedName == null || obsoleteNames == null || !obsoleteNames.TryGetValue(typedName, out var fullName))
                return null;
            return $"No command named '{typedName}' is available. It was renamed: run 'unity command {fullName}'.";
        }
    }
}
