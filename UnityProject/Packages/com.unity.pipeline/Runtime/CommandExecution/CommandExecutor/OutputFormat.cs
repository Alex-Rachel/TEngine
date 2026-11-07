namespace Unity.Pipeline.Commands
{
    /// <summary>The format of a command's result, picked by the executor's options.</summary>
    enum OutputFormat
    {
        /// <summary>For people and agents: records as a table, a single result as one field per line.</summary>
        Human,

        /// <summary>For scripts: one line per record, its fields separated by tabs.</summary>
        Tsv,

        /// <summary>JSON: a single result as it is, records as one JSON value per line.</summary>
        Json
    }
}
