using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Unity.Pipeline.Commands
{
    /// <summary>
    /// The command catalog <c>pipeline list_commands</c> returns, and the listing of a group named
    /// with nothing after it, written as the <c>unity</c> CLI lays out a listing for people, or as records for the other
    /// formats to write.
    /// </summary>
    static class CommandListing
    {
        /// <summary>What the catalog holds, picked by the detail it was asked for.</summary>
        internal enum Kind
        {
            /// <summary>Commands, flat or in a group_by tree.</summary>
            Commands,

            /// <summary>The group index: one row per group.</summary>
            Groups,

            /// <summary>The tag index: one row per tag.</summary>
            Tags

        }

        const string Reset = "\x1b[0m";
        const string Bold = "\x1b[1m";
        const string Dim = "\x1b[2m";
        const string Cyan = "\x1b[36m";

        const string UntaggedLabel = "(untagged)";

        /// <summary>The kind of catalog <paramref name="detail"/> asks for.</summary>
        internal static Kind KindOf(string detail)
        {
            if (string.Equals(detail, "groups", StringComparison.OrdinalIgnoreCase))
                return Kind.Groups;
            if (string.Equals(detail, "tags", StringComparison.OrdinalIgnoreCase))
                return Kind.Tags;
            return Kind.Commands;
        }

        /// <summary>The listing for people, each line ending with a line break.</summary>
        /// <param name="catalog">What <see cref="BasePipelineServer.BuildCommandCatalog"/> returned.</param>
        /// <param name="color">Whether to color it, for a terminal.</param>
        internal static string Human(object catalog, Kind kind, bool color)
        {
            return Human(ToJObject(catalog), kind, color);
        }

        /// <summary><see cref="Human(object, Kind, bool)"/>, for the catalog as JSON.</summary>
        internal static string Human(JObject catalog, Kind kind, bool color)
        {
            var writer = new Writer(color);
            WriteHuman(writer, catalog, kind);
            // Each section opens with a blank line, except the first.
            return writer.ToString().TrimStart('\n');
        }

        /// <summary>
        /// The listing as records, for <see cref="CommandResult.WriteRecord"/>: each command (a
        /// group_by tree's flattened), group or tag, as the catalog holds it, with a command's
        /// schema parsed.
        /// </summary>
        /// <param name="catalog">What <see cref="BasePipelineServer.BuildCommandCatalog"/> returned.</param>
        internal static IEnumerable<JObject> Entries(object catalog, Kind kind)
        {
            return Entries(ToJObject(catalog), kind);
        }

        /// <summary><see cref="Entries(object, Kind)"/>, for the catalog as JSON.</summary>
        internal static IEnumerable<JObject> Entries(JObject catalog, Kind kind)
        {
            switch (kind)
            {
                case Kind.Groups:
                    return Array(catalog, "groups").OfType<JObject>();
                case Kind.Tags:
                    return Array(catalog, "tags").OfType<JObject>();
                default:
                    var commands = new List<JObject>();
                    if (catalog["groups"] is JArray groups)
                        FlattenGroups(groups, commands);
                    else
                        commands.AddRange(Array(catalog, "commands").OfType<JObject>());
                    return commands.Select(WithParsedSchema);
            }
        }

        /// <summary>
        /// The command with its <c>schema</c>, which the catalog holds as JSON text, as the object it
        /// is: a nested value, so tsv leaves it out and JSON writes it unescaped.
        /// </summary>
        static JObject WithParsedSchema(JObject command)
        {
            if (!(command["schema"] is JValue schema) || schema.Type != JTokenType.String)
                return command;
            var parsed = (JObject)command.DeepClone();
            parsed["schema"] = JToken.Parse((string)schema);
            return parsed;
        }

        static JObject ToJObject(object catalog) =>
            ExecResponseSerializer.ResultToToken(catalog, false, false) as JObject ?? new JObject();

        static void WriteHuman(Writer writer, JObject catalog, Kind kind)
        {
            switch (kind)
            {
                case Kind.Groups:
                    WriteIndex(writer, Array(catalog, "groups"), "group",
                        "\U0001F4C2 Available Groups:", "No groups available on this Unity Editor instance",
                        "List a group's commands: ", "unity command <group>");
                    break;
                case Kind.Tags:
                    WriteIndex(writer, Array(catalog, "tags"), "tag",
                        "\U0001F3F7️ Available Tags:", "No tags available on this Unity Editor instance",
                        "List a tag's commands: ", "unity command --tag <tag>");
                    break;
                default:
                    WriteCommands(writer, catalog);
                    break;
            }
        }

        /// <summary>The group or tag index: two columns of names and their command counts.</summary>
        static void WriteIndex(Writer writer, JArray rows, string nameKey,
            string header, string none, string hint, string hintCommand)
        {
            var lines = IndexGridLines(rows, nameKey, writer.Color);
            if (lines.Count == 0)
            {
                writer.Line("\n❌ " + none);
                return;
            }

            writer.Line("\n" + header + "\n");
            foreach (var line in lines)
                writer.Line(line);
            writer.Line("\n\U0001F4A1 " + hint + writer.Code(hintCommand));
        }

        /// <summary>
        /// The index as a two-column grid, column-major: the names read down the first column and
        /// continue at the top of the second.
        /// </summary>
        static List<string> IndexGridLines(JArray rows, string nameKey, bool color)
        {
            var cells = rows.Select(row => (Name: Cell(Display(Field(row, nameKey))), Count: Cell(Display(Field(row, "count")))))
                .ToList();
            var lines = new List<string>();
            if (cells.Count == 0)
                return lines;

            var nameWidth = cells.Max(c => c.Name.Length);
            var countWidth = cells.Max(c => c.Count.Length);
            var rowCount = (cells.Count + 1) / 2;
            string GridCell((string Name, string Count) cell) =>
                Paint(cell.Name.PadRight(nameWidth), Cyan, color) + "  " + Paint(cell.Count.PadLeft(countWidth), Dim, color);

            for (var i = 0; i < rowCount; i++)
            {
                var line = "  " + GridCell(cells[i]);
                if (i + rowCount < cells.Count)
                    line += "    " + GridCell(cells[i + rowCount]);
                lines.Add(line);
            }
            return lines;
        }

        static void WriteCommands(Writer writer, JObject catalog)
        {
            const string none = "\n❌ No commands available on this Unity Editor instance";

            // A group_by tree: each group's header with its count, then its commands.
            if (catalog["groups"] is JToken grouped && grouped.Type != JTokenType.Null)
            {
                var groups = grouped as JArray ?? new JArray();
                if (groups.Count == 0)
                {
                    writer.Line(none);
                    return;
                }
                WriteCommandGroups(writer, groups, "");
                writer.Line(UsageHint(writer, AnyTreeEntryHasGroup(groups)));
                return;
            }

            var commands = Array(catalog, "commands");
            if (commands.Count == 0)
            {
                writer.Line(none);
                return;
            }

            writer.Line("\n\U0001F4CB Available Commands:\n");
            var indexWidth = commands.Count.ToString(CultureInfo.InvariantCulture).Length;
            for (var i = 0; i < commands.Count; i++)
            {
                var label = (i + 1).ToString(CultureInfo.InvariantCulture).PadLeft(indexWidth) + ".";
                foreach (var line in CommandEntryLines(commands[i] as JObject, writer.Color, label))
                    writer.Line(line);
            }

            // The examples use listed commands, in the form they run in.
            writer.Line(UsageHint(writer, commands.Any(c => HasGroup(c as JObject))));
            var example = commands.OfType<JObject>().FirstOrDefault(HasGroup);
            writer.Line(Example(writer, example != null ? "unity command " + CliForm(example) : "unity command editor_play"));
            var withParameters = commands.OfType<JObject>().FirstOrDefault(c => Array(c, "parameters").Count > 0);
            if (withParameters != null)
            {
                var parameter = Array(withParameters, "parameters")[0];
                writer.Line(Example(writer, $"unity command {CliForm(withParameters)} --{Display(Field(parameter, "name"))} value"));
            }
            writer.Line(Paint("\n(? marks optional flags)", Dim, writer.Color));
        }

        /// <summary>A header with its count per group, its commands, then its children one indent deeper.</summary>
        static void WriteCommandGroups(Writer writer, JArray groups, string indent)
        {
            foreach (var entry in groups)
            {
                var group = entry as JObject;
                var label = Paint(Paint(GroupLabel(group), Cyan, writer.Color), Bold, writer.Color);
                var count = Paint("(" + Display(group?["count"]) + ")", Dim, writer.Color);
                writer.Line($"\n{indent}\U0001F4E6 {label} {count}");
                foreach (var command in Array(group, "commands"))
                {
                    var described = command as JObject;
                    writer.Line($"{indent}  {Paint(CliForm(described), Cyan, writer.Color)}  {Paint(Description(described), Dim, writer.Color)}");
                }
                var children = Array(group, "children");
                if (children.Count > 0)
                    WriteCommandGroups(writer, children, indent + "  ");
            }
        }

        /// <summary>
        /// A command's entry: its name with its flags (<c>?</c> marking an optional one), its
        /// description, and one line per parameter.
        /// </summary>
        static List<string> CommandEntryLines(JObject command, bool color, string indexLabel)
        {
            var parameters = Array(command, "parameters");
            var headerParameters = parameters.Count == 0
                ? ""
                : "  " + Paint(string.Join(" ", parameters.Select(p => "--" + Display(Field(p, "name")) + (IsRequired(p) ? "" : "?"))), Cyan, color);

            var leadWidth = indexLabel.Length + 1;
            var nameIndent = new string(' ', 2 + leadWidth);
            var parameterIndent = new string(' ', 4 + leadWidth);

            var lines = new List<string>
            {
                "  " + Paint(indexLabel, Dim, color) + " " + Paint(Paint(CliForm(command), Cyan, color), Bold, color) + headerParameters,
                nameIndent + Paint(Description(command), Dim, color),
            };
            foreach (var parameter in parameters)
            {
                var fields = parameter as JObject;
                var required = IsRequired(parameter) ? " (required)" : "";
                var defaultValue = fields != null && fields.ContainsKey("defaultValue")
                    ? $" [default: {Display(fields["defaultValue"])}]"
                    : "";
                var detail = NonEmpty(Field(parameter, "description")) ?? Display(Field(parameter, "type"));
                lines.Add(parameterIndent + Paint("--" + Display(Field(parameter, "name")), Cyan, color)
                    + Paint($": {detail}{required}{defaultValue}", Dim, color));
            }
            return lines;
        }

        /// <summary>Each group's own commands, then its children's.</summary>
        static void FlattenGroups(JArray groups, List<JObject> commands)
        {
            foreach (var group in groups)
            {
                commands.AddRange(Array(group, "commands").OfType<JObject>());
                FlattenGroups(Array(group, "children"), commands);
            }
        }

        static string UsageCommand(bool groupedEntries) => groupedEntries
            ? "unity command <group> <name> [--parameter value]"
            : "unity command <command> [--parameter value]";

        static string UsageHint(Writer writer, bool groupedEntries) =>
            "\n\U0001F4A1 Usage: " + writer.Code(UsageCommand(groupedEntries));

        static string Example(Writer writer, string command) => "\U0001F4D6 Example: " + writer.Code(command);

        /// <summary>
        /// A command as it is typed: its full name (<c>editor.playmode start</c>), or its name for an
        /// entry with none.
        /// </summary>
        static string CliForm(JObject command) => NonEmpty(command?["command"]) ?? Display(command?["name"]);

        static bool HasGroup(JObject command) => NonEmpty(command?["group"]) != null;

        static bool AnyTreeEntryHasGroup(JArray groups) => groups.OfType<JObject>().Any(group =>
            Array(group, "commands").Any(c => HasGroup(c as JObject)) || AnyTreeEntryHasGroup(Array(group, "children")));

        /// <summary>A group_by node's name: its package, its group, or its tag (the untagged one for an empty tag).</summary>
        static string GroupLabel(JObject group)
        {
            if (group == null)
                return "undefined";
            if (group.ContainsKey("package"))
                return Display(group["package"], "null");
            if (group.ContainsKey("group"))
                return Display(group["group"], "null");
            if (group["tag"] is JValue tag && tag.Type == JTokenType.String && (string)tag == "")
                return UntaggedLabel;
            return Display(group["tag"]);
        }

        static string Description(JObject command) => NonEmpty(command?["description"]) ?? "No description available";

        static bool IsRequired(JToken parameter)
        {
            var required = Field(parameter, "required");
            return required != null && required.Type == JTokenType.Boolean && (bool)required;
        }

        static JToken Field(JToken token, string name) => (token as JObject)?[name];

        static JArray Array(JToken token, string name) => Field(token, name) as JArray ?? new JArray();

        /// <summary>A string that isn't empty, or null.</summary>
        static string NonEmpty(JToken value) =>
            value != null && value.Type == JTokenType.String && ((string)value).Length > 0 ? (string)value : null;

        /// <summary>A value as the CLI shows it: a missing or null one as <paramref name="none"/>, a list comma-separated.</summary>
        static string Display(JToken value, string none = "undefined")
        {
            switch (value?.Type)
            {
                case null:
                case JTokenType.Null:
                case JTokenType.Undefined:
                    return none;
                case JTokenType.String:
                    return (string)value;
                case JTokenType.Boolean:
                    return (bool)value ? "true" : "false";
                case JTokenType.Integer:
                    return ((JValue)value).ToString(CultureInfo.InvariantCulture);
                case JTokenType.Float:
                    return ((double)value).ToString("R", CultureInfo.InvariantCulture);
                case JTokenType.Array:
                    return string.Join(",", value.Select(item => item.Type == JTokenType.Null ? "" : Display(item)));
                case JTokenType.Object:
                    return "[object Object]";
                default:
                    return value.ToString();
            }
        }

        /// <summary>A value on one line: line breaks and tabs as spaces.</summary>
        static string Cell(string value)
        {
            if (value.IndexOfAny(new[] { '\n', '\r', '\t' }) < 0)
                return value;
            return value.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
        }

        static string Paint(string text, string code, bool color) => color ? code + text + Reset : text;

        sealed class Writer
        {
            readonly StringBuilder m_Text = new StringBuilder();

            public Writer(bool color)
            {
                Color = color;
            }

            public bool Color { get; }

            public void Line(string line) => m_Text.Append(line).Append('\n');

            /// <summary>A command the reader types, in cyan on a terminal.</summary>
            public string Code(string command) => Paint(command, Cyan, Color);

            public override string ToString() => m_Text.ToString();
        }
    }
}
