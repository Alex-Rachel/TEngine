using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditorInternal;

namespace Unity.Pipeline.Editor.Commands.Profiling
{
    /// <summary>A target the Editor's Profiler connection can point at. Main thread only.</summary>
    sealed class ProfilerTarget
    {
        // Keep in sync with ProfilerConnection::kEditorGuid (0xFFFFFFFF as UInt32).
        const int EditorGuid = -1;

        public int Guid { get; private set; }
        public string Name { get; private set; }

        /// <summary>This Editor process, i.e. guid <see cref="EditorGuid"/>. A connection to *another*
        /// Editor is an ordinary remote target: <c>ProfilerDriver.profileEditor</c> drives this process,
        /// so it would not reach it.</summary>
        public bool IsEditor { get; private set; }
        public bool Connectable { get; private set; }
        public bool IsCurrent { get; private set; }

        /// <summary>Without <c>BuildOptions.EnableDeepProfilingSupport</c> the Editor still accepts
        /// <c>deepProfiling = true</c> and reads it back as true while the Player instruments nothing.</summary>
        public bool DeepProfilingSupported { get; private set; } = true;

        /// <summary>GetAvailableProfilers() omits a Player connected via Autoconnect Profiler, so the
        /// current connection is always added.</summary>
        public static List<ProfilerTarget> Enumerate()
        {
            var current = ProfilerDriver.connectedProfiler;
            var targets = new List<ProfilerTarget> { LocalEditor(current) };

            foreach (var guid in ProfilerDriver.GetAvailableProfilers())
            {
                if (guid == EditorGuid)
                    continue;
                targets.Add(FromGuid(guid, current));
            }

            if (current != EditorGuid && targets.All(t => t.Guid != current))
                targets.Add(FromGuid(current, current));

            return targets;
        }

        /// <summary>Whatever the Editor's Profiler connection points at now.</summary>
        public static ProfilerTarget Current()
        {
            var current = ProfilerDriver.connectedProfiler;
            return current == EditorGuid ? LocalEditor(current) : FromGuid(current, current);
        }

        /// <summary><c>editor</c>, a guid, or a name: exact first, then a unique partial match.</summary>
        public static ProfilerTarget Match(List<ProfilerTarget> targets, string requested)
        {
            requested = requested.Trim();

            if (string.Equals(requested, "editor", StringComparison.OrdinalIgnoreCase))
                return targets.FirstOrDefault(t => t.IsEditor);

            if (int.TryParse(requested, out var guid))
                return targets.FirstOrDefault(t => t.Guid == guid);

            var exact = targets.Where(t => string.Equals(t.Name, requested, StringComparison.OrdinalIgnoreCase)).ToList();
            if (exact.Count == 1)
                return exact[0];

            var partial = targets
                .Where(t => t.Name != null && t.Name.IndexOf(requested, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
            return partial.Count == 1 ? partial[0] : null;
        }

        /// <summary>
        /// Confirms the connection still points at this target. The Profiler window drives the same
        /// global setting, so a capture checks rather than assumes: the alternative is a capture of the
        /// wrong process, reported under the name of this one.
        /// </summary>
        public void VerifyStillSelected()
        {
            var current = ProfilerDriver.connectedProfiler;
            if (current == Guid)
                return;

            throw new InvalidOperationException(
                $"The Editor's Profiler connection moved off '{Name}' (it is now guid {current}), so the capture " +
                "would have come from the wrong process. Run the command again, and leave the Profiler window's " +
                "connection alone while it runs.");
        }

        /// <summary>
        /// The entry for this Editor process. Current only on guid <see cref="EditorGuid"/>: a connection
        /// to a remote Editor also reports itself as an Editor connection, and folding it in here would
        /// hide it from the list and make <c>editor</c> resolve to the wrong process.
        /// </summary>
        static ProfilerTarget LocalEditor(int current)
        {
            return new ProfilerTarget(EditorGuid, "Editor", isEditor: true)
            {
                Connectable = true,
                IsCurrent = current == EditorGuid
            };
        }

        static ProfilerTarget FromGuid(int guid, int current)
        {
            return new ProfilerTarget(guid, ProfilerDriver.GetConnectionIdentifier(guid), isEditor: false)
            {
                Connectable = ProfilerDriver.IsIdentifierConnectable(guid),
                IsCurrent = guid == current,
                DeepProfilingSupported = IsDeepProfilingSupported(guid)
            };
        }

        internal ProfilerTarget(int guid, string name, bool isEditor)
        {
            Guid = guid;
            Name = name;
            IsEditor = isEditor;
        }

        static MethodInfo s_IsDeepProfilingSupported;
        static bool s_IsDeepProfilingSupportedResolved;

        /// <summary>
        /// Internal, and the only way to know whether a Player was built for deep profiling. Assumes
        /// support when the member is missing, so a Unity version that renames it degrades to the
        /// pre-check behaviour instead of breaking the commands.
        /// </summary>
        static bool IsDeepProfilingSupported(int guid)
        {
            if (!s_IsDeepProfilingSupportedResolved)
            {
                s_IsDeepProfilingSupportedResolved = true;
                s_IsDeepProfilingSupported = typeof(ProfilerDriver).GetMethod(
                    "IsDeepProfilingSupported",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(int) }, null);
            }

            if (s_IsDeepProfilingSupported == null)
                return true;

            try
            {
                return (bool)s_IsDeepProfilingSupported.Invoke(null, new object[] { guid });
            }
            catch (Exception)
            {
                return true;
            }
        }
    }
}
