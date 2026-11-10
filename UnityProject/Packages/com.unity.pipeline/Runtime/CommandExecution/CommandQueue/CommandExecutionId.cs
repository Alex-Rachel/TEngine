using System;

namespace Unity.Pipeline.Commands
{
    /// <summary>
    /// Names one execution, unique within the instance. Its text form is what clients hold and
    /// pass back, such as a detached job's id, and parses back to the same id.
    /// </summary>
    readonly struct CommandExecutionId : IEquatable<CommandExecutionId>
    {
        readonly Guid m_Value;

        CommandExecutionId(Guid value)
        {
            m_Value = value;
        }

        /// <summary>A new id, unlike every other.</summary>
        public static CommandExecutionId New() => new CommandExecutionId(Guid.NewGuid());

        /// <summary>Reads an id from its text form.</summary>
        /// <returns>False when <paramref name="text"/> is not an id's text form.</returns>
        public static bool TryParse(string text, out CommandExecutionId id)
        {
            if (Guid.TryParseExact(text ?? string.Empty, "N", out var value))
            {
                id = new CommandExecutionId(value);
                return true;
            }

            id = default;
            return false;
        }

        /// <summary>The id's text form: 32 lower-case hex digits.</summary>
        public override string ToString() => m_Value.ToString("N");

        public bool Equals(CommandExecutionId other) => m_Value.Equals(other.m_Value);

        public override bool Equals(object obj) => obj is CommandExecutionId other && Equals(other);

        public override int GetHashCode() => m_Value.GetHashCode();

        public static bool operator ==(CommandExecutionId left, CommandExecutionId right) => left.Equals(right);

        public static bool operator !=(CommandExecutionId left, CommandExecutionId right) => !left.Equals(right);
    }
}
