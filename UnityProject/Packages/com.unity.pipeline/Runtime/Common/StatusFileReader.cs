using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Unity.Pipeline
{
    /// <summary>
    /// Reads the status files that the async commands (build, bake, recompile, package, audit,
    /// test run, target switch) use to carry a result across a domain reload.
    ///
    /// The file's text is already JSON, so the obvious read is to return it verbatim. That is wrong:
    /// a command's return value is assigned to <c>CommandExecutionResponse.Result</c> and serialized,
    /// so a string of JSON lands on the wire as a quoted, escaped document
    /// (<c>"result": "{\"status\":\"idle\"}"</c>) that a client must parse twice and a JSON-path
    /// consumer cannot read at all. Parsing to a <see cref="JToken"/> here makes the serializer emit
    /// the payload as real JSON.
    /// </summary>
    internal static class StatusFileReader
    {
        /// <summary>The payload every reader falls back to when no status file has been written yet.</summary>
        internal static JObject Idle => new JObject { ["status"] = "idle" };

        /// <summary>
        /// The status file's contents as a JSON token, or <c>{"status":"idle"}</c> when the file does
        /// not exist. Text that does not parse is returned as a <c>malformed</c> payload carrying the
        /// raw text, so a half-written or hand-edited file reports itself instead of throwing out of
        /// a status poll.
        /// </summary>
        internal static JToken ReadOrIdle(string path)
        {
            if (!File.Exists(path))
                return Idle;

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (IOException ex)
            {
                return new JObject { ["status"] = "unreadable", ["message"] = ex.Message };
            }

            return Parse(text);
        }

        /// <summary>
        /// Status-file text as one complete JSON document. Text that is empty, does not parse, or
        /// carries anything after the document becomes a <c>malformed</c> payload rather than an
        /// exception — see <see cref="ReadOrIdle"/>.
        /// </summary>
        internal static JToken Parse(string text)
        {
            // No idle fallback here: idle states that no operation has run, which only a missing file
            // can say. Every writer uses File.WriteAllText, which truncates the file before it writes,
            // so empty text is a read that landed inside a write — calling that idle tells a polling
            // client that a running operation never started.
            if (string.IsNullOrWhiteSpace(text))
                return Malformed(text);

            try
            {
                // DateParseHandling.None so every value passes through exactly as the file holds it.
                // Newtonsoft's default turns an ISO-8601 string into a Date token, and reading that
                // back as a string yields a local, timezone-less format ("09/16/2026 21:43:58") that
                // no longer round-trips: that is how build_status came to report elapsedMs 0 in any
                // non-UTC timezone — the reparsed instant lost its UTC kind, ToUniversalTime() shifted
                // it by the local offset, and the Math.Max(0, ...) clamp hid the negative result.
                using (var reader = new JsonTextReader(new StringReader(text))
                       {
                           DateParseHandling = DateParseHandling.None
                       })
                {
                    var document = JToken.ReadFrom(reader);

                    // ReadFrom stops at the end of the first token, so a shorter write over a longer
                    // file leaves a readable document followed by the tail of the old one. Requiring
                    // EOF keeps that from being reported as the current status.
                    return reader.Read() ? Malformed(text) : document;
                }
            }
            catch (JsonException)
            {
                return Malformed(text);
            }
        }

        static JObject Malformed(string text)
        {
            return new JObject { ["status"] = "malformed", ["raw"] = text ?? string.Empty };
        }
    }
}
