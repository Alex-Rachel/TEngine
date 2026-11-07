using System.Collections.Generic;
using System.Text;

namespace Unity.Pipeline
{
    /// <summary>
    /// Splits a raw command line into argv tokens for <c>/api/exec</c>'s <c>commandLine</c> form.
    ///
    /// The dialect is POSIX-ish and is the <b>pipeline's own</b> — not the caller's shell, and
    /// identical on every OS, so the same string tokenizes the same way from cmd.exe, bash, an
    /// MCP client or a chat box. It is documented as a versioned contract in
    /// <c>Documentation~/connectivity.md</c>; <c>CommandLineTokenizerTests</c> pins it.
    ///
    /// Rules: whitespace separates tokens; <c>"…"</c> groups with <c>\</c> escaping the next
    /// character; <c>'…'</c> groups with <b>no</b> escapes at all; <c>\</c> escapes outside
    /// quotes; adjacent spans concatenate (<c>a"b c"d</c> → <c>ab cd</c>); <c>""</c> yields one
    /// empty token. There is <b>no expansion of any kind</b> — no <c>$VAR</c>, globbing, <c>~</c>,
    /// comments or operators; <c>&amp;&amp;</c> and <c>|</c> are ordinary characters.
    ///
    /// POSIX-ish rather than Windows <c>CommandLineToArgvW</c> because single quotes are
    /// load-bearing here — <c>eval 'obj.name = "x";'</c> and <c>batch --operations '[{…}]'</c>
    /// need a form that passes double quotes through untouched, and Windows semantics have no
    /// single-quote concept. The usual argument for Windows rules (<c>\</c> is the path
    /// separator) does not apply: every path this API accepts is a forward-slashed Unity asset
    /// path.
    /// </summary>
    internal static class CommandLineTokenizer
    {
        /// <summary>
        /// Tokenizes <paramref name="commandLine"/>. Returns false and sets
        /// <paramref name="error"/> on an unbalanced quote or a dangling backslash.
        /// A null, empty or all-whitespace input succeeds with zero tokens — a lexer has no
        /// opinion about needing a command name, so the caller reports that (keeping the message
        /// identical to the structured path).
        /// </summary>
        public static bool TryTokenize(string commandLine, out List<string> tokens, out string error)
        {
            tokens = new List<string>();
            error = null;

            if (string.IsNullOrEmpty(commandLine))
                return true;

            var current = new StringBuilder();
            // Distinguishes `""` (one empty token) from whitespace (no token at all).
            var hasToken = false;
            var inSingle = false;
            var inDouble = false;

            for (var i = 0; i < commandLine.Length; i++)
            {
                var c = commandLine[i];

                if (inSingle)
                {
                    if (c == '\'')
                        inSingle = false;
                    else
                        current.Append(c);
                    continue;
                }

                if (inDouble)
                {
                    if (c == '"')
                    {
                        inDouble = false;
                    }
                    else if (c == '\\')
                    {
                        if (i + 1 >= commandLine.Length)
                        {
                            error = "Dangling backslash at end of command line";
                            return false;
                        }
                        current.Append(commandLine[++i]);
                    }
                    else
                    {
                        current.Append(c);
                    }
                    continue;
                }

                if (c == '\'')
                {
                    inSingle = true;
                    hasToken = true;
                }
                else if (c == '"')
                {
                    inDouble = true;
                    hasToken = true;
                }
                else if (c == '\\')
                {
                    if (i + 1 >= commandLine.Length)
                    {
                        error = "Dangling backslash at end of command line";
                        return false;
                    }
                    current.Append(commandLine[++i]);
                    hasToken = true;
                }
                else if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
                {
                    if (hasToken)
                    {
                        tokens.Add(current.ToString());
                        current.Length = 0;
                        hasToken = false;
                    }
                }
                else
                {
                    current.Append(c);
                    hasToken = true;
                }
            }

            if (inSingle)
            {
                error = "Unbalanced single quote in command line";
                return false;
            }

            if (inDouble)
            {
                error = "Unbalanced double quote in command line";
                return false;
            }

            if (hasToken)
                tokens.Add(current.ToString());

            return true;
        }
    }
}
