using System;
using System.Collections.Generic;

namespace Unity.Pipeline.Commands
{
    /// <summary>
    /// Why commands that need the main thread can't run now. Commands that don't need it run
    /// anyway. A modal dialog is reported first: settling ends on its own, a dialog needs a person.
    /// </summary>
    enum CommandUnavailableReason
    {
        /// <summary>The Editor is importing or compiling since it started.</summary>
        Settling = 0,

        /// <summary>A modal dialog holds the main thread.</summary>
        BlockedByDialog = 1
    }

    /// <summary>A command can't run now, and why; retrying later can succeed.</summary>
    sealed class CommandUnavailableException : Exception
    {
        /// <summary>The full name of the command that can't run.</summary>
        public string Command { get; }

        /// <summary>Why it can't run.</summary>
        public CommandUnavailableReason Reason { get; }

        /// <summary>The dialogs open when it was refused, oldest first; empty unless <see cref="Reason"/> is BlockedByDialog.</summary>
        internal IReadOnlyList<DialogInfo> Dialogs { get; }

        public CommandUnavailableException(string command, CommandUnavailableReason reason, string message)
            : this(command, reason, message, Array.Empty<DialogInfo>())
        {
        }

        internal CommandUnavailableException(string command, CommandUnavailableReason reason, string message,
            IReadOnlyList<DialogInfo> dialogs)
            : base(message)
        {
            Command = command;
            Reason = reason;
            Dialogs = dialogs;
        }
    }
}
