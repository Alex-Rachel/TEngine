using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.Pipeline
{
    /// <summary>Snapshot of one dialog shown/dismissed event, as relayed by EditorDialogEvents.</summary>
    internal readonly struct DialogInfo
    {
        public readonly int Id;
        public readonly string Source;
        public readonly string Title;
        public readonly string Message;
        public readonly string[] Buttons;
        public readonly string Level;
        public readonly DateTime OpenedAtUtc;
        public readonly DateTime? DismissedAtUtc;

        public DialogInfo(int id, string source, string title, string message, string[] buttons, string level, DateTime openedAtUtc, DateTime? dismissedAtUtc)
        {
            Id = id;
            Source = source;
            Title = title;
            Message = message;
            Buttons = buttons;
            Level = level;
            OpenedAtUtc = openedAtUtc;
            DismissedAtUtc = dismissedAtUtc;
        }
    }

    /// <summary>
    /// Per-server modal-dialog state (the same one-instance-per-<see cref="BasePipelineServer"/>
    /// ownership as <c>CliProgressState</c>/<c>Progress</c>, so a test server's dialog state never
    /// crosses into the live server's). Unlike progress, dialogs are not scoped to a currently
    /// executing command — they can be raised by anything (a human, a background job, an unrelated
    /// command) at any time, so there is no owner-id concept here.
    /// </summary>
    internal sealed class DialogStateTracker
    {
        readonly object m_Gate = new object();
        readonly Dictionary<int, DialogInfo> m_Open = new Dictionary<int, DialogInfo>();

        internal bool IsDialogOpen
        {
            get { lock (m_Gate) return m_Open.Count > 0; }
        }

        /// <summary>
        /// Ordered oldest-opened-first: callers (the dialog busy-gate, /api/status) treat index 0
        /// as "the first open dialog", and Dictionary&lt;,&gt;'s enumeration order isn't guaranteed
        /// to match insertion order once entries have been removed and re-added.
        /// </summary>
        internal List<DialogInfo> CurrentlyOpen
        {
            get { lock (m_Gate) return m_Open.Values.OrderBy(e => e.OpenedAtUtc).ToList(); }
        }

        internal void OnShown(int id, string source, string title, string message, string[] buttons, string level, DateTime openedAtUtc)
        {
            var info = new DialogInfo(id, source, title, message, buttons, level, openedAtUtc, null);
            lock (m_Gate)
            {
                m_Open[id] = info;
            }
        }

        internal void OnDismissed(int id, DateTime dismissedAtUtc)
        {
            lock (m_Gate)
            {
                m_Open.Remove(id);
            }
        }

    }
}
