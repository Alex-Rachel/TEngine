using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.Pipeline.Commands
{
    /// <summary>
    /// A set of commands indexed by <see cref="CommandInfo.FullName"/>, together with every group they
    /// define. A group exists when a command is in it or in one of its subgroups, so
    /// <c>animation</c> is a group as soon as <c>animation.animator</c> holds a command.
    /// Immutable: <see cref="CommandRegistry"/> builds a new one whenever its commands change.
    /// </summary>
    internal sealed class CommandIndex
    {
        readonly Dictionary<string, CommandInfo> m_ByFullName;
        // The same commands under their one-word spelling (CommandInfo.GetIdFromName), for the
        // callers that name a command in one token.
        readonly Dictionary<string, CommandInfo> m_ByOneWord;
        // Every command under its name alone, whatever its group: one entry when the name is unique.
        readonly Dictionary<string, List<CommandInfo>> m_ByName;
        readonly HashSet<string> m_Groups;

        /// <summary>The indexed commands, in the order given.</summary>
        public IReadOnlyList<CommandInfo> Commands { get; }

        /// <param name="commands">Commands with distinct full names.</param>
        /// <exception cref="ArgumentException">Two commands share a full name.</exception>
        public CommandIndex(IEnumerable<CommandInfo> commands)
        {
            Commands = commands.ToList();
            m_ByFullName = Commands.ToDictionary(c => c.FullName, StringComparer.Ordinal);
            m_ByOneWord = new Dictionary<string, CommandInfo>(StringComparer.Ordinal);
            m_ByName = new Dictionary<string, List<CommandInfo>>(StringComparer.Ordinal);
            foreach (var command in Commands)
            {
                m_ByOneWord[CommandInfo.GetIdFromName(command.FullName)] = command;
                if (!m_ByName.TryGetValue(command.Name, out var named))
                    m_ByName[command.Name] = named = new List<CommandInfo>(1);
                named.Add(command);
            }
            m_Groups = new HashSet<string>(StringComparer.Ordinal);
            foreach (var command in Commands)
            {
                for (var group = command.Group; group != null; group = CommandInfo.ParentGroup(group))
                {
                    if (!m_Groups.Add(group))
                        break;
                }
            }
        }

        /// <summary>Look a command up by its full name (<c>editor status</c>).</summary>
        public bool TryGetCommand(string fullName, out CommandInfo command)
        {
            command = null;
            return fullName != null && m_ByFullName.TryGetValue(fullName, out command);
        }

        /// <summary>
        /// Look a command up by its one-word spelling (<c>editor.status</c>), the form
        /// <see cref="CommandInfo.GetIdFromName"/> makes. Accepted wherever a command is named but
        /// never produced in output.
        /// </summary>
        public bool TryGetCommandByOneWord(string oneWord, out CommandInfo command)
        {
            command = null;
            return oneWord != null && m_ByOneWord.TryGetValue(oneWord, out command);
        }

        /// <summary>
        /// Look a command up as a caller names it in one string: by full name, else by the
        /// one-word spelling, else as a name in <see cref="CommandInfo.GlobalGroup"/>, else by a
        /// name no other command shares.
        /// </summary>
        public bool TryFindCommand(string name, out CommandInfo command)
        {
            return TryGetCommand(name, out command) || TryGetCommandByOneWord(name, out command)
                || TryGetGlobalCommand(name, out command) || TryGetCommandByUniqueName(name, out command);
        }

        /// <summary>
        /// Look a command up by its name alone (<c>eval</c> for <c>script eval</c>), whatever its
        /// group. Found only when no other command has that name.
        /// </summary>
        public bool TryGetCommandByUniqueName(string name, out CommandInfo command)
        {
            command = null;
            if (name == null || !m_ByName.TryGetValue(name, out var named) || named.Count != 1)
                return false;
            command = named[0];
            return true;
        }

        /// <summary>The commands named <paramref name="name"/>, in any group; empty when there is none.</summary>
        public IReadOnlyList<CommandInfo> CommandsNamed(string name)
        {
            return name != null && m_ByName.TryGetValue(name, out var named) ? named : (IReadOnlyList<CommandInfo>)Array.Empty<CommandInfo>();
        }

        /// <summary>Look a command of <see cref="CommandInfo.GlobalGroup"/> up by its name alone.</summary>
        public bool TryGetGlobalCommand(string name, out CommandInfo command)
        {
            return TryGetCommand(CommandInfo.ComposeFullName(CommandInfo.GlobalGroup, name), out command);
        }

        /// <summary>Whether <paramref name="group"/> holds a command, directly or in a subgroup.</summary>
        public bool IsGroup(string group)
        {
            return group != null && m_Groups.Contains(group);
        }

        /// <summary>The commands directly in <paramref name="group"/>, not in its subgroups.</summary>
        public IEnumerable<CommandInfo> CommandsIn(string group)
        {
            return Commands.Where(c => c.Group == group);
        }

        /// <summary>The commands in <paramref name="group"/> or any of its subgroups.</summary>
        public IEnumerable<CommandInfo> CommandsUnder(string group)
        {
            return Commands.Where(c => CommandInfo.IsInGroupSubtree(c.Group, group));
        }

        /// <summary>The direct subgroups of <paramref name="group"/>, e.g. <c>animation.clip</c> for <c>animation</c>.</summary>
        public IEnumerable<string> SubgroupsOf(string group)
        {
            return m_Groups.Where(g => CommandInfo.ParentGroup(g) == group);
        }
    }
}
