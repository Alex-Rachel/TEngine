using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Unity.Pipeline.Commands
{
    /// <summary>
    /// <see cref="ICommandExecutor"/> for one server: finds the request's command in the registry,
    /// binds its arguments, refuses it when it can't run now, and hands the rest to the queue. It
    /// refuses nothing itself, which is all a Player needs; the Editor's subclass adds its rules.
    /// </summary>
    class DefaultCommandExecutor : ICommandExecutor
    {
        readonly ICommandRegistry m_Registry;
        readonly ICommandQueue m_Queue;

        public DefaultCommandExecutor(ICommandRegistry registry, ICommandQueue queue)
        {
            m_Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            m_Queue = queue ?? throw new ArgumentNullException(nameof(queue));
        }

        /// <inheritdoc/>
        public CommandExecution Execute(CommandRequest request)
        {
            var command = Bind(request, out var parameters, out var listedGroup);
            if (listedGroup != null)
                return List(listedGroup, request);
            Refuse(command);

            var converted = Convert(command, parameters);
            return m_Queue.Enqueue(command, ToValues(parameters), converted, request);
        }

        /// <summary>
        /// Finds the request's command and binds its arguments: a command line's tokens, or the
        /// structured request's JSON object as it is. Takes the executor's own options out first:
        /// the queue reads them when it writes what the command returns.
        /// </summary>
        /// <param name="listedGroup">
        /// The group a command line named with nothing after it, to list in place of a command;
        /// null otherwise. The command and its parameters are then null.
        /// </param>
        CommandInfo Bind(CommandRequest request, out JObject parameters, out string listedGroup)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            listedGroup = null;
            var arguments = CommandRequest.ReadOptions(request.Arguments, out _, out _);
            var index = m_Registry.Index;
            if (request.ParametersJson != null)
            {
                if (arguments.Count > 0)
                    throw new ArgumentException(
                        $"A request by parameter name takes no command-line arguments, only {CommandRequest.VerboseOption} and {CommandRequest.OmitNullsOption}; got '{arguments[0]}'.");

                if (!index.TryFindCommand(request.Command, out var named))
                    throw BasePipelineServer.CommandNotFound(new BasePipelineServer.RawCommandMiss(request.Command, index.Commands, index.CommandsNamed(request.Command)));
                parameters = ParseParameters(request.ParametersJson);
                DropCliToken(named, parameters);
                RenameLegacyCliKeys(named, parameters);
                var unknown = CommandLineBinder.FindUnknownParameters(named, parameters);
                if (unknown.Count > 0)
                    throw new CommandArgumentsException(named, unknown, BasePipelineServer.DescribeArgProblems(named, unknown, structured: true));
                return named;
            }

            var tokens = new List<string>(arguments.Count + 1) { request.Command };
            tokens.AddRange(arguments);

            var resolution = BasePipelineServer.ResolveRawCommand(tokens, index);
            if (resolution.ListedGroup != null)
            {
                listedGroup = resolution.ListedGroup;
                parameters = null;
                return null;
            }

            var command = resolution.Command;
            var rest = tokens.GetRange(resolution.ArgumentsStart, tokens.Count - resolution.ArgumentsStart);
            RenameLegacyCliFlags(command, rest);
            if (!CommandLineBinder.TryBind(command, rest, out parameters, out var problems))
                throw new CommandArgumentsException(command, problems, BasePipelineServer.DescribeArgProblems(command, problems));

            // Binding succeeds when a required parameter simply wasn't given: that rule belongs to
            // parameter validation, and is checked here so an invalid request is refused before
            // it can be queued as a job. The refusal names the command the tokens resolved to.
            var missingRequired = BasePipelineServer.ValidateRequiredParametersBound(command, parameters);
            if (!string.IsNullOrEmpty(missingRequired))
                throw new ParameterValidationException(command.FullName, missingRequired);

            return command;
        }

        /// <summary>
        /// The key the <c>unity</c> CLI adds to some commands' parameters, holding the eval token: its
        /// MCP tools (<c>script eval</c>, <c>test run</c>, <c>script recompile</c>), <c>unity ci</c>'s
        /// recompile and a live build. No command declares it and authentication goes through the
        /// Authorization header, so it was always ignored; since unknown keys are refused, it is
        /// removed here instead: the CLI keeps sending it, and refusing it would fail those calls.
        /// </summary>
        internal const string CliTokenKey = "token";

        /// <summary>Removes <see cref="CliTokenKey"/>, unless the command declares a parameter of that name.</summary>
        static void DropCliToken(CommandInfo command, JObject parameters)
        {
            if (parameters == null || !parameters.ContainsKey(CliTokenKey))
                return;
            foreach (var parameter in command.Parameters)
                if (string.Equals(parameter.Name, CliTokenKey, StringComparison.Ordinal))
                    return;
            parameters.Remove(CliTokenKey);
        }

        /// <summary>
        /// Deprecated: parameter names released <c>unity</c> CLIs still send, from before parameters
        /// became kebab-case (AUTHAPI-115): a live build sends <c>outputPath</c> to <c>build run</c>,
        /// <c>unity console</c> and <c>unity ci</c>'s recompile send <c>since_session</c> to
        /// <c>console get</c>, and <c>unity command --group_by</c> sends <c>group_by</c> to
        /// <c>pipeline list_commands</c>. The CLI sends them as a structured request's keys or, over the
        /// protocol transport, as <c>--name</c> flags. Refusing them would break those CLIs, so they are
        /// renamed to the current name instead. This is not an alias for anyone else: it applies to
        /// these commands only, and is removed in <see cref="LegacyCliKeysRemovedIn"/>, when the CLIs
        /// that send the kebab-case names are the supported ones.
        /// </summary>
        internal static readonly (string Command, string OldKey, string NewKey)[] LegacyCliKeys =
        {
            (CommandNames.BuildRun, "outputPath", "output-path"),
            (CommandNames.ConsoleGet, "since_session", "since-session"),
            (CommandNames.PipelineListCommands, "group_by", "group-by"),
        };

        /// <summary>The package version that removes <see cref="LegacyCliKeys"/>; a test fails once the package reaches it.</summary>
        internal const string LegacyCliKeysRemovedIn = "1.0.0";

        /// <summary>
        /// Renames each of <see cref="LegacyCliKeys"/> for <paramref name="command"/> to its current name.
        /// A request that also sends the current name keeps the old key, which is then refused as unknown.
        /// </summary>
        static void RenameLegacyCliKeys(CommandInfo command, JObject parameters)
        {
            if (parameters == null)
                return;
            foreach (var (name, oldKey, newKey) in LegacyCliKeys)
            {
                if (!string.Equals(command.FullName, name, StringComparison.Ordinal)
                    || !parameters.TryGetValue(oldKey, StringComparison.Ordinal, out var value)
                    || parameters.ContainsKey(newKey))
                    continue;
                parameters.Remove(oldKey);
                parameters[newKey] = value;
            }
        }

        /// <summary>
        /// Renames each of <see cref="LegacyCliKeys"/> for <paramref name="command"/> among a command
        /// line's flags, written <c>--name value</c> or <c>--name=value</c>. A command line that also
        /// gives the current name keeps the old flag, which is then refused as unknown.
        /// </summary>
        static void RenameLegacyCliFlags(CommandInfo command, List<string> arguments)
        {
            foreach (var (name, oldKey, newKey) in LegacyCliKeys)
            {
                if (!string.Equals(command.FullName, name, StringComparison.Ordinal)
                    || arguments.Exists(a => IsFlag(a, newKey)))
                    continue;
                for (var i = 0; i < arguments.Count; i++)
                    if (IsFlag(arguments[i], oldKey))
                        arguments[i] = "--" + newKey + arguments[i].Substring(2 + oldKey.Length);
            }
        }

        static bool IsFlag(string argument, string key) =>
            argument.StartsWith("--" + key, StringComparison.Ordinal)
            && (argument.Length == key.Length + 2 || argument[key.Length + 2] == '=');

        /// <summary>
        /// Answers a request naming <paramref name="group"/> with the group's subgroups and
        /// commands, written to its Out as what a command returns is: at once, without queueing,
        /// refusing or reporting, since nothing runs.
        /// </summary>
        CommandExecution List(string group, CommandRequest request)
        {
            var output = new CommandResult(request);
            // Outside JSON, the group's commands as `list_commands --group` lays them out.
            if (output.Format != OutputFormat.Json)
            {
                var catalog = BasePipelineServer.BuildCommandCatalog(name => name == "group" ? group : null, null, null);
                if (output.Format == OutputFormat.Tsv)
                {
                    foreach (var record in CommandListing.Entries(catalog, CommandListing.Kind.Commands))
                        output.WriteRecord(record);
                }
                else
                    request.Out.Write(CommandListing.Human(catalog, CommandListing.Kind.Commands, request.OutIsTerminal));
            }
            else
                output.Write(BasePipelineServer.BuildGroupListing(group, m_Registry.Index));
            output.Complete();
            return CommandExecution.ForListing(group);
        }

        /// <summary>
        /// Reads a structured request's arguments as the HTTP transport received them: dates parse
        /// to dates, as they did when the transport handed the object over itself.
        /// </summary>
        static JObject ParseParameters(string json)
        {
            if (string.IsNullOrEmpty(json))
                return new JObject();

            var token = JToken.Parse(json);
            return token as JObject ?? throw new ArgumentException("The arguments must be a JSON object, by parameter name.");
        }

        /// <summary>
        /// Converts the bound arguments to the command's parameters' types and checks the required
        /// ones are there.
        /// </summary>
        static object[] Convert(CommandInfo command, JObject parameters)
        {
            var converted = BasePipelineServer.ExtractCommandParameters(command, parameters, out var conversionError);

            // Reject unconvertible arguments rather than silently running with the default.
            // The log keeps the name of the method this lived in, which clients and tests match.
            if (!string.IsNullOrEmpty(conversionError))
            {
                Debug.LogError($"ExecuteCommandByName: Parameter conversion failed: {conversionError}");
                throw new ParameterValidationException(command.FullName, conversionError);
            }

            var validationError = BasePipelineServer.ValidateCommandParameters(command, converted);
            if (!string.IsNullOrEmpty(validationError))
            {
                Debug.LogError($"ExecuteCommandByName: Parameter validation failed: {validationError}");
                throw new ParameterValidationException(command.FullName, validationError);
            }

            return converted;
        }


        /// <summary>
        /// The bound arguments by parameter name, as plain values: each a string, boolean, number,
        /// date or null, or a list or dictionary of them for a structured value.
        /// </summary>
        static IReadOnlyDictionary<string, object> ToValues(JObject parameters)
        {
            var values = new Dictionary<string, object>(parameters.Count);
            foreach (var property in parameters.Properties())
                values[property.Name] = ToValue(property.Value);
            return values;
        }

        static object ToValue(JToken token)
        {
            switch (token)
            {
                case JObject obj:
                    return ToValues(obj);
                case JArray array:
                    var items = new List<object>(array.Count);
                    foreach (var item in array)
                        items.Add(ToValue(item));
                    return items;
                case JValue value:
                    return value.Value;
                default:
                    return token.ToString();
            }
        }

        /// <summary>
        /// Throws <see cref="CommandUnavailableException"/> when <paramref name="command"/> can't run
        /// now. Called on the caller's thread, so an override reads only thread-safe state. Refuses
        /// nothing by default.
        /// </summary>
        protected virtual void Refuse(CommandInfo command) { }

        /// <summary>
        /// Told about each command that ran, once it ends, synchronous or a detached job; a
        /// command refused isn't reported. Called on whatever thread
        /// the command ended on, so an override sticks to thread-safe work or moves to the main
        /// thread itself, and never throws: the command's caller would see the failure.
        /// </summary>
        /// <param name="info">The execution that ended. It carries no transaction.</param>
        protected virtual void OnCommandEnded(in CommandExecutionInfo info) { }

        /// <summary>Reports a command of this executor's queue that ran, once it ends.</summary>
        internal void ReportEnded(in CommandExecutionInfo info) => OnCommandEnded(info);
    }
}
