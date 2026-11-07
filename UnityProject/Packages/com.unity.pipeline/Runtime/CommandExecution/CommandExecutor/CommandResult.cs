using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Unity.Pipeline.Commands
{
    /// <summary>
    /// A command's result, written to its request's Out as it is produced: a single result with
    /// <see cref="Write"/>, or one record per item with <see cref="WriteRecord"/>.
    /// </summary>
    /// <remarks>
    /// The format is the request's <see cref="OutputFormat"/>, or JSON when
    /// <see cref="CommandRequest.FormatOutput"/> is false. The human format's table takes its
    /// columns from the first record, with its null fields: <see cref="CommandRequest.OmitNullsOption"/>
    /// blanks a null value rather than drop its column. A table cell is kept on one line, while a text
    /// result or record, and a field's value, keep their line breaks. An empty list writes nothing.
    /// Thread-safe.
    /// </remarks>
    sealed class CommandResult
    {
        const string ColumnSeparator = "  ";

        /// <summary>The options that pick the tsv and JSON formats, as an error names them.</summary>
        const string TsvFormat = CommandRequest.FormatOption + " tsv";
        const string JsonFormat = CommandRequest.FormatOption + " json";

        readonly object m_Lock = new object();
        readonly TextWriter m_Out;
        readonly bool m_Formatted;
        readonly OutputFormat m_Format;
        readonly IReadOnlyList<string> m_Fields;
        readonly bool m_Verbose;
        readonly bool m_OmitNulls;

        /// <summary>The width lines are cut to; null to leave them whole.</summary>
        readonly int? m_Width;

        bool m_WroteResult;
        int m_Records;
        bool m_Completed;

        /// <summary>The fields written for each record, set by the first one; null for scalar records.</summary>
        List<string> m_Columns;

        /// <summary>The human format's column widths, set with <see cref="m_Columns"/>.</summary>
        int[] m_ColumnWidths;

        /// <summary>Whether the first object written was checked for the fields <see cref="m_Fields"/> names.</summary>
        bool m_FieldsChecked;

        /// <param name="request">The request whose Out takes the result and whose options pick its format.</param>
        public CommandResult(CommandRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            CommandRequest.ReadOptions(request.Arguments, out m_Verbose, out m_OmitNulls, out var format, out var fields);
            m_Out = request.Out;
            m_Formatted = request.FormatOutput;
            m_Format = m_Formatted ? format : OutputFormat.Json;
            m_Fields = m_Formatted ? fields : null;
            m_Width = m_Formatted && request.OutIsTerminal && request.TerminalWidth > 0 ? request.TerminalWidth : null;
        }

        /// <summary>The format the result is written in.</summary>
        public OutputFormat Format => m_Format;

        /// <summary>Whether <see cref="CommandRequest.FieldsOption"/> picks the fields written.</summary>
        public bool SelectsFields => m_Fields != null;

        /// <summary>Whether any result was written.</summary>
        public bool HasWritten
        {
            get
            {
                lock (m_Lock)
                    return m_WroteResult || m_Records > 0;
            }
        }

        /// <summary>Writes the command's single result.</summary>
        /// <exception cref="InvalidOperationException">The command already wrote its result, or a record.</exception>
        public void Write(object result)
        {
            lock (m_Lock)
            {
                if (m_WroteResult)
                    throw new InvalidOperationException("A command writes its single result once.");
                if (m_Records > 0)
                    throw new InvalidOperationException("A command writes its result with Write or with WriteRecord, not both.");
                m_WroteResult = true;

                if (m_Format == OutputFormat.Json)
                {
                    if (!m_Formatted)
                        m_Out.Write(ExecResponseSerializer.SerializeResult(result, m_Verbose, m_OmitNulls));
                    else if (ToToken(result) is JArray items)
                    {
                        // A list, as records are: one value per line.
                        foreach (var item in items)
                            m_Out.Write(SelectFields(item).ToString(Formatting.None) + "\n");
                    }
                    else
                        m_Out.Write(SelectFields(ToToken(result)).ToString(Formatting.None) + "\n");
                    return;
                }

                var token = ToToken(result);
                switch (token)
                {
                    case JValue none when none.Type == JTokenType.Null:
                        break;
                    case JArray items:
                        foreach (var item in items)
                            WriteRecordToken(item);
                        break;
                    case JObject fields when m_Format == OutputFormat.Human:
                        WriteFields(fields);
                        break;
                    default:
                        WriteRecordToken(token);
                        break;
                }
            }
        }

        /// <summary>Writes one record of the result.</summary>
        /// <exception cref="InvalidOperationException">The command already wrote a single result.</exception>
        public void WriteRecord(object record)
        {
            lock (m_Lock)
            {
                if (m_WroteResult)
                    throw new InvalidOperationException("A command writes its result with Write or with WriteRecord, not both.");
                WriteRecordToken(ToToken(record));
            }
        }

        /// <summary>
        /// Writes a command's return value, unless the command already wrote its result. A list is
        /// written as records when <see cref="CommandRequest.FormatOutput"/> is set.
        /// </summary>
        public void WriteReturned(object returned)
        {
            if (HasWritten)
                return;

            if (m_Formatted && IsList(returned))
            {
                foreach (var item in (IEnumerable)returned)
                    WriteRecord(item);
            }
            else
                Write(returned);
        }

        /// <summary>
        /// Ends the result: closes the JSON array of records when <see cref="CommandRequest.FormatOutput"/>
        /// is off. Formatted JSON writes one record per line, with nothing to close.
        /// </summary>
        public void Complete()
        {
            lock (m_Lock)
            {
                if (m_Completed)
                    return;
                m_Completed = true;
                if (m_Format == OutputFormat.Json && !m_Formatted && m_Records > 0)
                    m_Out.Write("\n]");
            }
        }

        static bool IsList(object value)
        {
            return value is IEnumerable
                && !(value is string)
                && !(value is IDictionary)
                && !(value is JObject)
                && !(value is JValue);
        }

        /// <summary>
        /// A value as JSON. JSON drops nulls here for <see cref="CommandRequest.OmitNullsOption"/>; the
        /// other formats keep them until the fields written are known.
        /// </summary>
        JToken ToToken(object value) => ExecResponseSerializer.ResultToToken(value, m_Verbose, OmitsNullsFromTokens);

        bool OmitsNullsFromTokens => m_OmitNulls && m_Format == OutputFormat.Json;

        void WriteRecordToken(JToken record)
        {
            if (m_Format == OutputFormat.Json)
            {
                if (m_Formatted)
                    m_Out.Write(SelectFields(record).ToString(Formatting.None) + "\n");
                else
                    m_Out.Write((m_Records == 0 ? "[\n" : ",\n") + record.ToString(Formatting.None));
                m_Records++;
                return;
            }

            var cells = Cells(record);
            if (m_Format == OutputFormat.Tsv)
                m_Out.Write(string.Join("\t", cells.Select(EscapeTsv)) + "\n");
            else if (record is JObject)
                WriteRow(cells.Select(ToSingleLine).ToList());
            else
                WriteText(cells[0]);
            m_Records++;
        }

        /// <summary>A human-format row, preceded by the header for the first one.</summary>
        void WriteRow(List<string> cells)
        {
            if (m_ColumnWidths == null)
            {
                var headers = m_Columns.Select(Header).ToList();
                m_ColumnWidths = headers.Select((header, i) => Math.Max(header.Length, cells[i].Length)).ToArray();
                WriteLine(Pad(headers));
            }

            WriteLine(Pad(cells));
        }

        string Pad(List<string> cells)
        {
            if (m_ColumnWidths == null)
                return string.Join(ColumnSeparator, cells);

            var line = new StringBuilder();
            for (var i = 0; i < cells.Count; i++)
            {
                if (i > 0)
                    line.Append(ColumnSeparator);
                line.Append(i < cells.Count - 1 ? cells[i].PadRight(m_ColumnWidths[i]) : cells[i]);
            }
            return line.ToString();
        }

        /// <summary>
        /// A human-format single result: one <c>name: value</c> line per field, a value's further
        /// lines indented under its first.
        /// </summary>
        void WriteFields(JObject result)
        {
            CheckFields(result);
            if (m_OmitNulls)
                ExecResponseSerializer.RemoveNullProperties(result);
            foreach (var name in m_Fields ?? result.Properties().Select(p => p.Name).ToList())
            {
                var lines = Lines(Text(Field(result, name)));
                WriteLine(name + ": " + lines[0]);
                var indent = new string(' ', name.Length + 2);
                foreach (var line in lines.Skip(1))
                    WriteLine(indent + line);
            }
        }

        /// <summary>A human-format text result or record: its lines as they are.</summary>
        void WriteText(string text)
        {
            foreach (var line in Lines(text))
                WriteLine(line);
        }

        void WriteLine(string line)
        {
            if (m_Width is int width && line.Length > width)
                line = line.Substring(0, Math.Max(width - 1, 0)) + "…";
            m_Out.Write(line + "\n");
        }

        /// <summary>
        /// A record's values in column order; a scalar record is its one value. Tsv has no nested
        /// values: its columns leave out the fields that hold an object or a list, and naming one
        /// with <see cref="CommandRequest.FieldsOption"/> is refused.
        /// </summary>
        /// <exception cref="ArgumentException">A tsv record, or a field it names, holds an object or a list.</exception>
        List<string> Cells(JToken record)
        {
            var tsv = m_Format == OutputFormat.Tsv;
            if (!(record is JObject fields))
            {
                if (tsv && IsNested(record))
                    throw new ArgumentException($"{TsvFormat} writes no nested values; pass {JsonFormat} for them.");
                if (m_OmitNulls)
                    ExecResponseSerializer.RemoveNullProperties(record);
                return new List<string> { Text(record) };
            }

            CheckFields(fields);
            if (m_Columns == null)
            {
                m_Columns = m_Fields?.ToList() ?? fields.Properties()
                    .Where(p => !tsv || !IsNested(p.Value))
                    .Select(p => p.Name)
                    .ToList();
            }

            // Only once the columns are known, so a field null in the first record keeps its column.
            if (m_OmitNulls)
                ExecResponseSerializer.RemoveNullProperties(fields);

            var cells = new List<string>(m_Columns.Count);
            foreach (var name in m_Columns)
            {
                var value = Field(fields, name);
                if (tsv && IsNested(value))
                {
                    // Only a field --fields names, or one a later record nests, reaches here.
                    throw new ArgumentException(
                        $"{TsvFormat} writes no nested values, and '{name}' holds an object or a list; pass {JsonFormat} for it.");
                }
                cells.Add(Text(value));
            }
            return cells;
        }

        static bool IsNested(JToken value) => value is JObject || value is JArray;

        /// <summary>The fields <see cref="CommandRequest.FieldsOption"/> names, in that order.</summary>
        JToken SelectFields(JToken token)
        {
            if (m_Fields == null || !(token is JObject fields))
                return token;

            CheckFields(fields);
            var selected = new JObject();
            foreach (var name in m_Fields)
                selected[name] = Field(fields, name)?.DeepClone() ?? JValue.CreateNull();
            return selected;
        }

        /// <summary>
        /// Refuses a field <see cref="CommandRequest.FieldsOption"/> names that the first object
        /// written doesn't have, before anything is written for it. A later record may leave a field
        /// out, written as a missing value. Not checked when JSON's <see cref="CommandRequest.OmitNullsOption"/>
        /// left out the fields that are null.
        /// </summary>
        /// <exception cref="ArgumentException">A named field isn't one of the first object's.</exception>
        void CheckFields(JObject first)
        {
            if (m_FieldsChecked || m_Fields == null)
                return;
            m_FieldsChecked = true;
            if (OmitsNullsFromTokens)
                return;

            var unknown = m_Fields.Where(name => Field(first, name) == null).ToList();
            if (unknown.Count == 0)
                return;
            var known = string.Join(", ", first.Properties().Select(p => p.Name));
            throw new ArgumentException(unknown.Count == 1
                ? $"{CommandRequest.FieldsOption} names '{unknown[0]}', which isn't a field of this result. Its fields: {known}."
                : $"{CommandRequest.FieldsOption} names {string.Join(", ", unknown.Select(n => $"'{n}'"))}, which aren't fields of this result. Its fields: {known}.");
        }

        static JToken Field(JObject fields, string name) => fields.GetValue(name, StringComparison.OrdinalIgnoreCase);

        /// <summary>A value as text: strings as is, null as empty, anything else as JSON.</summary>
        static string Text(JToken value)
        {
            if (value == null || value.Type == JTokenType.Null || value.Type == JTokenType.Undefined)
                return string.Empty;
            if (value.Type == JTokenType.String)
                return (string)value;
            return value.ToString(Formatting.None);
        }

        /// <summary>A field name as a column header: <c>playMode</c> as <c>PLAY MODE</c>.</summary>
        static string Header(string name)
        {
            var header = new StringBuilder(name.Length + 4);
            for (var i = 0; i < name.Length; i++)
            {
                var c = name[i];
                if (c == '_' || c == '-')
                    c = ' ';
                else if (i > 0 && char.IsUpper(c) && char.IsLower(name[i - 1]))
                    header.Append(' ');
                header.Append(char.ToUpperInvariant(c));
            }
            return header.ToString();
        }

        /// <summary>A value with line breaks and tabs replaced by spaces.</summary>
        static string ToSingleLine(string value)
        {
            if (value.IndexOfAny(new[] { '\n', '\r', '\t' }) < 0)
                return value;
            return value.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
        }

        /// <summary>A value's lines, without the line break that ends the last one.</summary>
        static string[] Lines(string value)
        {
            var lines = value.Replace("\r\n", "\n").Split('\n', '\r');
            return lines.Length > 1 && lines[lines.Length - 1].Length == 0 ? lines.Take(lines.Length - 1).ToArray() : lines;
        }

        /// <summary>A value with backslashes, tabs and line breaks escaped.</summary>
        static string EscapeTsv(string value)
        {
            if (value.IndexOfAny(new[] { '\\', '\t', '\n', '\r' }) < 0)
                return value;
            return value.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "\\r");
        }
    }
}
