using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Unity.Pipeline
{
    /// <summary>
    /// One machine-readable argument-binding defect, reported in <c>/api/exec</c>'s
    /// <c>argProblems</c> array.
    ///
    /// The vocabulary matches the <c>unity</c> CLI's own <c>ArgProblemKind</c> one-for-one, plus
    /// <see cref="ArgProblemKind.TypeMismatch"/>, which needs the declared CLR type and so can
    /// only come from the server. Field names mirror the CLI's record exactly, so neither side
    /// needs a translation table.
    ///
    /// Deliberately machine-readable rather than pre-rendered English: the CLI renders AND
    /// LOCALIZES these itself across 10 locales. A server sending prose would make
    /// <c>unity command</c> the one surface that answers in English while the rest of the CLI
    /// localizes. The envelope's <c>errorDetails</c> carries the English fallback for curl, MCP,
    /// and for kinds a client has never heard of.
    /// </summary>
    public class ArgProblem
    {
        /// <summary>The kind of issue with provided argument.</summary>
        [JsonProperty("kind")]
        [JsonConverter(typeof(StringEnumConverter), true)]
        public ArgProblemKind Kind { get; set; }

        /// <summary>The offending raw token, where the defect is about a token.</summary>
        [JsonProperty("token", NullValueHandling = NullValueHandling.Ignore)]
        public string Token { get; set; }

        /// <summary>The parameter name involved, where the defect is about a name.</summary>
        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        public string Name { get; set; }

        /// <summary>A did-you-mean candidate for <see cref="ArgProblemKind.UnknownName"/>.</summary>
        [JsonProperty("suggestion", NullValueHandling = NullValueHandling.Ignore)]
        public string Suggestion { get; set; }

        /// <summary>Declared positional slots, for <see cref="ArgProblemKind.ExcessPositional"/>.</summary>
        [JsonProperty("capacity", NullValueHandling = NullValueHandling.Ignore)]
        public int? Capacity { get; set; }

        /// <summary>Positionals supplied, for <see cref="ArgProblemKind.ExcessPositional"/>.</summary>
        [JsonProperty("given", NullValueHandling = NullValueHandling.Ignore)]
        public int? Given { get; set; }

        /// <summary>
        /// The declared CLR type name, for <see cref="ArgProblemKind.TypeMismatch"/>. Sent as
        /// <c>Int32</c>-style short names, matching what the catalog's schema already reports.
        /// </summary>
        [JsonProperty("expectedType", NullValueHandling = NullValueHandling.Ignore)]
        public string ExpectedType { get; set; }
    }

    /// <summary>
    /// Kinds of argument-binding defect. Serialized camelCase (<c>unknownName</c>) to match the
    /// CLI's wire expectations; the C# spelling stays PascalCase per package convention.
    /// </summary>
    public enum ArgProblemKind
    {
        // Shape — schema-independent.

        /// <summary>
        /// A flag with no name: <c>--=</c> or <c>--=value</c>.
        ///
        /// Not a bare <c>--</c>: the first one is the end-of-flags separator and is consumed
        /// before any name parsing, and every later one is a positional.
        /// </summary>
        EmptyName,

        /// <summary><c>--key=</c> with nothing after the <c>=</c>.</summary>
        EmptyValue,

        /// <summary>A <c>-x</c> token in positional position (never a flag's value).</summary>
        SingleDash,

        /// <summary>The same <c>--key</c> supplied more than once.</summary>
        Duplicate,

        // Names — requires the declared-parameter array.

        /// <summary><c>--nope</c>, or <c>--group_by</c> when the command declares <c>group-by</c>.</summary>
        UnknownName,

        /// <summary>A bare <c>key=value</c> positional whose left side names a real parameter.</summary>
        BareAssignment,

        /// <summary>More positionals than the command has parameter slots.</summary>
        ExcessPositional,

        /// <summary>A positional whose slot was already filled by an explicit flag.</summary>
        PositionalConflict,

        /// <summary>
        /// A token the declared parameter type cannot accept. Detectable only by the server,
        /// which has the real <see cref="System.Type"/>.
        /// </summary>
        TypeMismatch,
    }
}
