using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Editor.Console
{
    /// <summary>
    /// Reads the Editor console's own entry store through UnityEditor.LogEntries.
    ///
    /// This is the only way to see entries the package's log-callback capture never received.
    /// Editor compile errors are logged with a sticky flag, and the console's Clear skips sticky
    /// entries, so they survive the Clear button, Clear on Play, Clear on Build and Clear on
    /// Recompile — only the next completed compile removes them. They can therefore stand in the
    /// console long after the callback-fed buffer evicted them or was never subscribed.
    ///
    /// Reflection rather than a direct reference, because LogEntries is internal. Any failure
    /// disables this permanently for the session and callers report the console as unseeded.
    /// </summary>
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    static class EditorConsoleEntries
    {
        // ConsoleMode bits from Unity's native EditorMonoConsole (mirrored by the internal
        // ConsoleWindow.ConsoleFlags): the three level toggles the console filter checks, and
        // Collapse, which folds identical entries into a single row.
        const int LevelFlags = (1 << 7) | (1 << 8) | (1 << 9);
        const int CollapseFlag = 1 << 0;

        // LogMessageFlags bits, grouped exactly as Unity's own LogModeToLogType groups them.
        const int ModeScriptingException = 1 << 17;
        const int ModeErrors = (1 << 0) | (1 << 4) | (1 << 8) | (1 << 11) | (1 << 6);
        const int ModeAsserts = (1 << 1) | (1 << 21);
        const int ModeWarnings = (1 << 9) | (1 << 12) | (1 << 7);

        static readonly MethodInfo k_StartGettingEntries;
        static readonly MethodInfo k_EndGettingEntries;
        static readonly MethodInfo k_GetEntryInternal;
        static readonly MethodInfo k_GetFilteringText;
        static readonly MethodInfo k_SetFilteringText;
        static readonly MethodInfo k_GetConsoleFlags;
        static readonly MethodInfo k_SetConsoleFlags;
        static readonly Type k_LogEntryType;
        static readonly FieldInfo k_MessageField;
        static readonly FieldInfo k_ModeField;
        static readonly FieldInfo k_CallstackStartField;

        static bool s_Disabled;

        /// <summary>An entry as it stands in the Editor console.</summary>
        internal struct Entry
        {
            public LogType Type;
            public string Message;
            public string StackTrace;
        }

        static EditorConsoleEntries()
        {
            // LogEntries and LogEntry are internal, and LogEntries' members are static and
            // non-public, so NonPublic is mandatory in the binding flags — GetMethod without it
            // silently returns null.
            const BindingFlags statics = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

            var logEntries = Type.GetType("UnityEditor.LogEntries,UnityEditor");
            k_LogEntryType = Type.GetType("UnityEditor.LogEntry,UnityEditor");
            if (logEntries == null || k_LogEntryType == null)
                return;

            k_StartGettingEntries = logEntries.GetMethod("StartGettingEntries", statics);
            k_EndGettingEntries = logEntries.GetMethod("EndGettingEntries", statics);
            k_GetEntryInternal = logEntries.GetMethod("GetEntryInternal", statics);
            k_GetFilteringText = logEntries.GetMethod("GetFilteringText", statics);
            k_SetFilteringText = logEntries.GetMethod("SetFilteringText", statics);

            var consoleFlags = logEntries.GetProperty("consoleFlags", statics);
            k_GetConsoleFlags = consoleFlags?.GetGetMethod(true);
            k_SetConsoleFlags = consoleFlags?.GetSetMethod(true);

            const BindingFlags fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            k_MessageField = k_LogEntryType.GetField("message", fields);
            k_ModeField = k_LogEntryType.GetField("mode", fields);
            k_CallstackStartField = k_LogEntryType.GetField("callstackTextStartUTF16", fields);
        }

        /// <summary>
        /// True when every member this reads was found and no read has failed yet.
        /// </summary>
        internal static bool Available =>
            !s_Disabled
            && k_StartGettingEntries != null && k_EndGettingEntries != null && k_GetEntryInternal != null
            && k_GetFilteringText != null && k_SetFilteringText != null
            && k_GetConsoleFlags != null && k_SetConsoleFlags != null
            && k_MessageField != null && k_ModeField != null && k_CallstackStartField != null;

        /// <summary>
        /// Read up to <paramref name="max"/> most recent console entries, oldest first. Returns
        /// false when the internal API is unavailable or the read failed, in which case
        /// <paramref name="into"/> is left untouched. Main thread only.
        /// </summary>
        /// <param name="max">Maximum number of entries to read, taken from the end of the console.</param>
        /// <param name="into">Receives the entries read.</param>
        /// <returns>True when entries were read.</returns>
        internal static bool TryReadRecent(int max, List<Entry> into)
        {
            if (!Available || max <= 0)
                return false;

            var flags = 0;
            var filter = string.Empty;
            var flagsMutated = false;
            var filterMutated = false;

            try
            {
                flags = (int)k_GetConsoleFlags.Invoke(null, null);
                filter = (string)k_GetFilteringText.Invoke(null, null) ?? string.Empty;

                // The console's row list is filtered by the level toggles and the search box, and
                // Collapse folds repeats into one row, so a user who has any of them set would
                // otherwise hide entries from this read. Open the view only when it is actually
                // restricted: in the default state this mutates nothing and avoids two native
                // re-filter passes.
                //
                // Collapse matters because the caller counts occurrences to work out what it missed;
                // a collapsed row reports one where the console holds several.
                //
                // Both must happen outside the StartGettingEntries bracket: setting them rebuilds
                // the filtered index, and reordering it under a live snapshot invalidates the row
                // indices the read below uses.
                var opened = (flags | LevelFlags) & ~CollapseFlag;
                if (opened != flags)
                {
                    k_SetConsoleFlags.Invoke(null, new object[] { opened });
                    flagsMutated = true;
                }

                if (!string.IsNullOrEmpty(filter))
                {
                    k_SetFilteringText.Invoke(null, new object[] { string.Empty });
                    filterMutated = true;
                }

                // StartGettingEntries returns the row count and pins it for the duration; GetCount
                // would report that same pinned value, so there is no reason to ask twice.
                var total = (int)k_StartGettingEntries.Invoke(null, null);
                try
                {
                    var entry = Activator.CreateInstance(k_LogEntryType);
                    var args = new object[2];
                    for (var row = Math.Max(0, total - max); row < total; row++)
                    {
                        args[0] = row;
                        args[1] = entry;
                        if (!(bool)k_GetEntryInternal.Invoke(null, args))
                            continue;

                        into.Add(ToEntry(entry));
                    }
                }
                finally
                {
                    // Leaving the snapshot open pins the console's row count for every other reader
                    // and makes Unity warn on the next StartGettingEntries.
                    k_EndGettingEntries.Invoke(null, null);
                }

                return true;
            }
            catch (Exception ex)
            {
                s_Disabled = true;
                Debug.LogWarning($"[Pipeline] Cannot read the Editor console store; console entries logged before capture started will not be reported: {ex.Message}");
                return false;
            }
            finally
            {
                if (filterMutated)
                    TryRestore(k_SetFilteringText, filter);
                if (flagsMutated)
                    TryRestore(k_SetConsoleFlags, flags);
            }
        }

        static Entry ToEntry(object logEntry)
        {
            var message = (string)k_MessageField.GetValue(logEntry) ?? string.Empty;
            var mode = (int)k_ModeField.GetValue(logEntry);

            // A console entry stores its message and callstack in one string, split at this index.
            var split = Mathf.Clamp((int)k_CallstackStartField.GetValue(logEntry), 0, message.Length);

            return new Entry
            {
                Type = ToLogType(mode),
                Message = message.Substring(0, split),
                StackTrace = message.Substring(split)
            };
        }

        static LogType ToLogType(int mode)
        {
            if ((mode & ModeScriptingException) != 0)
                return LogType.Exception;
            if ((mode & ModeErrors) != 0)
                return LogType.Error;
            if ((mode & ModeAsserts) != 0)
                return LogType.Assert;
            if ((mode & ModeWarnings) != 0)
                return LogType.Warning;
            return LogType.Log;
        }

        static void TryRestore(MethodInfo setter, object value)
        {
            try
            {
                setter.Invoke(null, new[] { value });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Pipeline] Failed to restore Editor console filtering state: {ex.Message}");
            }
        }
    }
}
