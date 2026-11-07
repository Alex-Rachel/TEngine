using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline
{
    /// <summary>
    /// Binds argv tokens (the tokens AFTER the command name) to a command's declared
    /// parameters, producing the same <see cref="JObject"/> a client would have sent as
    /// structured <c>parameters</c>.
    ///
    /// <b>Values are stored as the structured form would carry them.</b> For a parameter with a
    /// declared type that means a string JValue, never pre-converted — the single most important
    /// constraint here, because it makes <c>{"commandLine":"log_editor hi"}</c> and
    /// <c>{"command":"log_editor","parameters":{"message":"hi"}}</c> indistinguishable from
    /// <c>ExtractCommandParameters</c> downwards, so there is exactly one execution path and
    /// one place where coercion can differ. An UNTYPED parameter has no type to coerce toward, so
    /// there the token's shape decides — see <c>StoreValue</c>.
    ///
    /// Every defect is accumulated rather than bailing on the first, so a user fixing a command
    /// line sees all of its problems at once. Required-parameter checking deliberately stays
    /// OUT of here — <c>ValidateCommandParameters</c> owns it, so the message and error type are
    /// byte-identical across both request forms.
    ///
    /// Grammar: <c>--key value</c>, <c>--key=value</c>, bare <c>--key</c>. A follower is
    /// consumed unless it starts with <c>--</c>, which is what makes <c>--path -foo</c> work.
    /// <c>--</c> ends flag parsing. No short flags, no <c>-abc</c> bundling, no <c>--no-x</c>,
    /// no repeated-flag-becomes-array.
    /// </summary>
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    internal static class CommandLineBinder
    {
        private static readonly CommandParameterInfo[] k_NoParameters = new CommandParameterInfo[0];

        /// <summary>
        /// Binds <paramref name="args"/> against <paramref name="command"/>. Returns true only
        /// when no problems were found; <paramref name="parameters"/> is always populated as far
        /// as binding got, so a caller may inspect a partial bind.
        /// </summary>
        public static bool TryBind(CommandInfo command, IReadOnlyList<string> args,
            out JObject parameters, out List<ArgProblem> problems)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            parameters = new JObject();
            problems = new List<ArgProblem>();

            var declared = command.Parameters ?? (IReadOnlyList<CommandParameterInfo>)k_NoParameters;
            if (args == null || args.Count == 0)
                return true;

            // Pass 1 - classify flags. Positionals are collected with a flag recording whether
            // they appeared after "--", which exempts them from the -foo and key=value
            // heuristics below.
            var flagFilled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Which parameters were set by a BARE flag. Tracked rather than inferred from the
            // stored value being boolean: an untyped parameter given the literal token `true`
            // also stores a boolean, and conflating the two reported a phantom missing value.
            // The token as typed, per parameter, so a type error can quote the user verbatim even
            // when the stored value was literal-parsed into a non-string.
            var rawTokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var bareFlagged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var positionals = new List<string>();
            var positionalExempt = new List<bool>();
            var endOfFlags = false;

            for (var i = 0; i < args.Count; i++)
            {
                var arg = args[i] ?? string.Empty;

                if (!endOfFlags && arg == "--")
                {
                    endOfFlags = true;
                    continue;
                }

                if (endOfFlags || !arg.StartsWith("--", StringComparison.Ordinal))
                {
                    positionals.Add(arg);
                    positionalExempt.Add(endOfFlags);
                    continue;
                }

                var body = arg.Substring(2);
                var equals = body.IndexOf('=');
                var name = equals >= 0 ? body.Substring(0, equals) : body;
                var inlineValue = equals >= 0 ? body.Substring(equals + 1) : null;

                if (name.Length == 0)
                {
                    problems.Add(new ArgProblem { Kind = ArgProblemKind.EmptyName, Token = arg });
                    continue;
                }

                var parameter = FindParameter(declared, name);
                if (parameter == null)
                {
                    problems.Add(new ArgProblem
                    {
                        Kind = ArgProblemKind.UnknownName,
                        Name = name,
                        Suggestion = SuggestParameterName(name, declared),
                    });
                    // Swallow what was clearly meant as this flag's value, so it does not
                    // resurface as a spurious excess positional.
                    if (inlineValue == null && HasConsumableFollower(args, i))
                        i++;
                    continue;
                }

                // Checked before the empty-value bail below: a second `--key=` is BOTH a
                // duplicate and an empty value, and reporting only the second would let a user
                // fix what they were told about and still be duplicating the flag.
                if (flagFilled.Contains(parameter.Name))
                    problems.Add(new ArgProblem { Kind = ArgProblemKind.Duplicate, Name = parameter.Name });

                if (inlineValue != null && inlineValue.Length == 0)
                {
                    problems.Add(new ArgProblem { Kind = ArgProblemKind.EmptyValue, Name = parameter.Name });
                    continue;
                }

                JToken value;
                if (inlineValue != null)
                {
                    value = inlineValue;
                }
                else if (HasConsumableFollower(args, i))
                {
                    // Absence and emptiness are different: `--message ""` is the empty string,
                    // not the boolean true.
                    value = args[++i] ?? string.Empty;
                }
                else
                {
                    value = new JValue(true);
                    bareFlagged.Add(parameter.Name);
                }

                if (value.Type == JTokenType.String)
                {
                    var text = value.Value<string>();
                    parameters[parameter.Name] = StoreValue(parameter, text);
                    rawTokens[parameter.Name] = text;
                }
                else
                {
                    parameters[parameter.Name] = value;
                }
                flagFilled.Add(parameter.Name);
            }

            // Pass 2 - positional slots: required parameters in declaration order, then
            // optional in declaration order. A slot already filled by a flag is SKIPPED, not
            // overwritten, so the explicit flag always wins its own slot.
            var slots = new List<CommandParameterInfo>(declared.Count);
            for (var i = 0; i < declared.Count; i++)
                if (declared[i].Required)
                    slots.Add(declared[i]);
            for (var i = 0; i < declared.Count; i++)
                if (!declared[i].Required)
                    slots.Add(declared[i]);

            var free = new List<CommandParameterInfo>(slots.Count);
            for (var i = 0; i < slots.Count; i++)
                if (!flagFilled.Contains(slots[i].Name))
                    free.Add(slots[i]);

            var next = 0;
            var firstOverflow = -1;

            for (var i = 0; i < positionals.Count; i++)
            {
                var token = positionals[i];

                if (!positionalExempt[i])
                {
                    // Both of these are reported but STILL consume a slot: the token was
                    // clearly meant as a positional, and pretending otherwise would cascade
                    // into misleading downstream problems.
                    if (IsSingleDashFlag(token))
                    {
                        problems.Add(new ArgProblem { Kind = ArgProblemKind.SingleDash, Token = token });
                    }
                    else if (next < free.Count && IsBareAssignmentNamingItsOwnSlot(token, free[next]))
                    {
                        problems.Add(new ArgProblem { Kind = ArgProblemKind.BareAssignment, Token = token });
                    }
                }

                if (next < free.Count)
                {
                    var slot = free[next++];
                    parameters[slot.Name] = StoreValue(slot, token);
                    rawTokens[slot.Name] = token;
                }
                else if (firstOverflow < 0)
                {
                    firstOverflow = i;
                }
            }

            // Pass 3 - coerce, as a DRY RUN only. We never rewrite what we stored: the executor
            // re-converts from the same string, so binding and execution cannot diverge.
            for (var i = 0; i < declared.Count; i++)
            {
                var parameter = declared[i];
                var value = parameters[parameter.Name];
                if (value == null)
                    continue;

                rawTokens.TryGetValue(parameter.Name, out var rawToken);
                var problem = CheckCoercible(
                    value, parameter, bareFlagged.Contains(parameter.Name), rawToken);
                if (problem != null)
                    problems.Add(problem);
            }

            if (firstOverflow >= 0)
            {
                // Which of the two overflow kinds this is depends on the slot the token WOULD
                // have taken, not on whether the request used a flag anywhere. Positionals fill
                // `free` in order, so the token at index n would have taken slots[n]: if that
                // slot exists and a flag claimed it, the token collided with that flag and the
                // parameter can be named. Otherwise the command simply received more values than
                // it declares parameters.
                var collided = firstOverflow < slots.Count && flagFilled.Contains(slots[firstOverflow].Name)
                    ? slots[firstOverflow]
                    : null;

                problems.Add(collided != null
                    ? new ArgProblem
                    {
                        Kind = ArgProblemKind.PositionalConflict,
                        Name = collided.Name,
                        Token = positionals[firstOverflow],
                    }
                    : new ArgProblem
                    {
                        Kind = ArgProblemKind.ExcessPositional,
                        Token = positionals[firstOverflow],
                        // Capacity and given must be commensurable, because they are rendered as
                        // one sentence: slots still FREE after the flags took theirs, against
                        // positionals supplied. Pairing the declared total with the positional
                        // count compares two different things and reads as nonsense whenever a
                        // flag is involved ("takes 2 but got 2").
                        Capacity = free.Count,
                        Given = positionals.Count,
                    });
            }

            return problems.Count == 0;
        }

        /// <summary>
        /// Decides what JSON type a token is stored as.
        ///
        /// <para>For a parameter with a DECLARED type the answer is always a string: the type
        /// itself disambiguates, and <c>ConvertParameterToken</c> widens it downstream. That is
        /// also the fix over the CLI's old client-side guessing, which turned <c>007</c> into the
        /// number 7 even for a declared <c>string</c>.</para>
        ///
        /// <para>An UNTYPED parameter (<c>JToken</c>/<c>JObject</c>/<c>JArray</c>/<c>object</c> —
        /// 14 of them across 12 shipped commands, e.g. <c>set_serialized_field.value</c>) has
        /// nothing to coerce toward, so the token's shape is the only signal there is. Those keep
        /// the literal rules the CLI used, or a working <c>--value 5</c> would silently start
        /// sending the string "5".</para>
        /// </summary>
        private static JToken StoreValue(CommandParameterInfo parameter, string token)
        {
            return IsUntypedTarget(parameter.ParameterType) && TryParseLiteral(token, out var literal)
                ? literal
                : token;
        }

        /// <summary>A declared type that can hold any JSON value, so it constrains nothing.</summary>
        private static bool IsUntypedTarget(Type type)
        {
            return type == typeof(object) || typeof(JToken).IsAssignableFrom(type);
        }

        /// <summary>
        /// Whether a bare flag's implied <c>true</c> is a value this parameter could hold.
        /// <c>JObject</c> and <c>JArray</c> are untyped but still cannot, so this is narrower
        /// than <see cref="IsUntypedTarget"/>.
        /// </summary>
        private static bool CanHoldBoolean(Type type)
        {
            var underlying = Nullable.GetUnderlyingType(type) ?? type;
            return underlying == typeof(bool)
                || underlying == typeof(object)
                || underlying == typeof(JToken)
                || underlying == typeof(JValue);
        }

        /// <summary>
        /// The CLI's own literal rules, reproduced exactly: <c>true</c>/<c>false</c>
        /// case-insensitively, an all-digit integer with an optional sign (so <c>07</c> is 7 — no
        /// radix handling), and a single-dot decimal. Everything else stays a string, which is why
        /// <c>1.2.3</c>, a bare <c>-</c> and asset paths pass through untouched.
        /// </summary>
        private static bool TryParseLiteral(string token, out JToken value)
        {
            value = null;
            if (string.IsNullOrEmpty(token))
                return false;

            var lowered = token.ToLowerInvariant();
            if (lowered == "true") { value = new JValue(true); return true; }
            if (lowered == "false") { value = new JValue(false); return true; }

            var body = token[0] == '-' ? token.Substring(1) : token;
            if (body.Length == 0)
                return false;

            var dot = body.IndexOf('.');
            if (dot < 0)
            {
                if (!AllAsciiDigits(body))
                    return false;
                if (long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole))
                {
                    value = new JValue(whole);
                    return true;
                }
                return false;
            }

            // Exactly one dot, digits on both sides.
            if (dot == 0 || dot == body.Length - 1
                || body.IndexOf('.', dot + 1) >= 0
                || !AllAsciiDigits(body.Substring(0, dot))
                || !AllAsciiDigits(body.Substring(dot + 1)))
            {
                return false;
            }

            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var real))
            {
                value = new JValue(real);
                return true;
            }
            return false;
        }

        private static bool AllAsciiDigits(string s)
        {
            for (var i = 0; i < s.Length; i++)
                if (s[i] < '0' || s[i] > '9')
                    return false;
            return s.Length > 0;
        }

        /// <summary>
        /// Dry-runs the executor's own converter against a bound value and reports the defect,
        /// or null when the value is acceptable.
        ///
        /// Three distinct failures fold into one report: the converter throwing; the converter
        /// returning NULL for a non-empty token, which would otherwise make the parameter vanish
        /// and let the command run with a default; and a bare flag on a non-bool parameter, which
        /// <c>JValue(true).ToObject&lt;string&gt;()</c> would happily turn into the word "True".
        /// </summary>
        private static ArgProblem CheckCoercible(
            JToken value, CommandParameterInfo parameter, bool wasBareFlag, string rawToken)
        {
            var targetType = parameter.ParameterType;

            // A bare flag implied `true`. On a parameter that cannot hold a boolean at all the
            // user simply omitted the value, which reads better as EmptyValue than as a type
            // error. Untyped is not the same as bool-capable: `object`, `JToken` and `JValue`
            // hold `true` happily, while `JObject` and `JArray` cannot, and letting the implied
            // `true` reach the converter reported those as a type error instead.
            if (wasBareFlag && !CanHoldBoolean(targetType))
            {
                return new ArgProblem { Kind = ArgProblemKind.EmptyValue, Name = parameter.Name };
            }

            // The token as the user typed it. An untyped target stores a literal-parsed JValue,
            // so the stored value is not always a string and its text is not always the token
            // (`007` parses to 7) — reading it back from the value dropped or altered the quote
            // the diagnostic exists to show.
            var token = rawToken ?? (value.Type == JTokenType.String ? value.Value<string>() : null);

            // Enums are NOT pre-matched against Enum.GetNames. The converter accepts a [Flags]
            // comma combination and a numeric value; a hand-rolled name match rejects both, which
            // would make the raw form refuse what the structured form binds. TypeMismatch already
            // unwraps Nullable<T> for the type name and the prose appends the valid values, so
            // deferring costs the message nothing.
            try
            {
                var converted = BasePipelineServer.ConvertParameterToken(value, targetType);
                if (converted == null && !string.IsNullOrEmpty(token))
                    return TypeMismatch(parameter, token, targetType);
            }
            catch (Exception)
            {
                return TypeMismatch(parameter, token, targetType);
            }

            return null;
        }

        private static ArgProblem TypeMismatch(CommandParameterInfo parameter, string token, Type targetType)
        {
            return new ArgProblem
            {
                Kind = ArgProblemKind.TypeMismatch,
                Name = parameter.Name,
                Token = token,
                ExpectedType = (Nullable.GetUnderlyingType(targetType) ?? targetType).Name,
            };
        }

        /// <summary>
        /// True when the token after <paramref name="index"/> should be taken as the current
        /// flag's value. Anything starting with <c>--</c> is the next flag, not a value; a
        /// single-dash token IS consumable, which is the escape hatch for values like
        /// <c>--message -foo</c>.
        /// </summary>
        private static bool HasConsumableFollower(IReadOnlyList<string> args, int index)
        {
            if (index + 1 >= args.Count)
                return false;
            var follower = args[index + 1];
            return follower == null || !follower.StartsWith("--", StringComparison.Ordinal);
        }

        private static CommandParameterInfo FindParameter(IReadOnlyList<CommandParameterInfo> declared, string name)
        {
            for (var i = 0; i < declared.Count; i++)
                if (string.Equals(declared[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return declared[i];
            return null;
        }

        /// <summary>
        /// A <c>-x</c> token in positional position: a single dash followed by a LETTER.
        ///
        /// Letter-anchored, deliberately matching the `unity` CLI's own rule byte for byte.
        /// The CLI keeps this check client-side so a typo costs nothing (no Editor boot, no CI
        /// license seat), which only stays safe while the local check is a strict SUBSET of this
        /// one — any divergence would surface as the server rejecting what the CLI let through.
        /// A looser test such as "not parseable as a number" would do exactly that for tokens
        /// like <c>-5x</c>. Negative numbers and a bare <c>-</c> remain valid positionals.
        /// </summary>
        private static bool IsSingleDashFlag(string token)
        {
            return token != null && token.Length >= 2 && token[0] == '-' && IsAsciiLetter(token[1]);
        }

        private static bool IsAsciiLetter(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        }

        /// <summary>
        /// A bare <c>key=value</c> positional whose left side names <paramref name="slot"/> — the
        /// very parameter the token is about to fill, which is what makes it a misremembered
        /// <c>--key value</c> rather than a value.
        ///
        /// <para>Testing the token against its OWN slot, rather than against every declared name,
        /// is what keeps legal code clean: <c>eval "timeout=5;"</c> lands on <c>code</c>, and
        /// <c>timeout</c> is a different parameter, so the token is a statement. Matching any
        /// declared name rejected it while the equivalent structured request succeeded.</para>
        ///
        /// <para>A token that assigns its own slot (<c>eval "code=1;"</c>) stays ambiguous by
        /// nature and is still reported; <c>--</c> is the documented way through.</para>
        /// </summary>
        private static bool IsBareAssignmentNamingItsOwnSlot(string token, CommandParameterInfo slot)
        {
            if (string.IsNullOrEmpty(token) || token[0] == '-')
                return false;
            var equals = token.IndexOf('=');
            if (equals <= 0)
                return false;
            return string.Equals(slot.Name, token.Substring(0, equals), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The keys of a structured <c>parameters</c> object that name no parameter of
        /// <paramref name="command"/>, as <see cref="ArgProblemKind.UnknownName"/> problems. Keys
        /// match a parameter name exactly, the way <c>ExtractCommandParameters</c> reads them, so a
        /// key that differs only in case is unknown too, and its suggestion names the real one.
        /// Without this a misspelled or renamed key was dropped and the command ran on its default.
        /// </summary>
        internal static List<ArgProblem> FindUnknownParameters(CommandInfo command, JObject parameters)
        {
            var problems = new List<ArgProblem>();
            if (parameters == null)
                return problems;

            var declared = command.Parameters;
            foreach (var property in parameters.Properties())
            {
                var known = false;
                for (var i = 0; i < declared.Count && !known; i++)
                    known = string.Equals(declared[i].Name, property.Name, StringComparison.Ordinal);
                if (!known)
                    problems.Add(new ArgProblem
                    {
                        Kind = ArgProblemKind.UnknownName,
                        Name = property.Name,
                        Suggestion = SuggestParameterName(property.Name, declared),
                    });
            }
            return problems;
        }

        private static string SuggestParameterName(string name, IReadOnlyList<CommandParameterInfo> declared)
        {
            var names = new string[declared.Count];
            for (var i = 0; i < declared.Count; i++)
                names[i] = declared[i].Name;
            return SuggestClosestName(name, names);
        }

        /// <summary>
        /// Closest candidate name within an edit-distance budget that scales with length, or
        /// null. Budget is <c>min(3, max(1, longest/3))</c>: tight enough that an unrelated name
        /// gets no suggestion at all, loose enough to catch real typos. Shared by unknown
        /// parameter names and unknown command names.
        /// <para>The distance is at least the length difference, so a candidate whose length
        /// differs by more than the budget is skipped without computing it. That keeps a
        /// malformed request cheap: a 100 KB name costs one length comparison per candidate
        /// instead of a full edit-distance table.</para>
        /// </summary>
        internal static string SuggestClosestName(string name, IEnumerable<string> candidates)
        {
            string best = null;
            var bestDistance = int.MaxValue;

            foreach (var candidate in candidates)
            {
                var budget = Math.Min(3, Math.Max(1, Math.Max(name.Length, candidate.Length) / 3));
                if (Math.Abs(name.Length - candidate.Length) > budget)
                    continue;
                var distance = EditDistance(name, candidate);
                if (distance <= budget && distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            return best;
        }

        /// <summary>Case-insensitive Levenshtein distance.</summary>
        private static int EditDistance(string a, string b)
        {
            a = a.ToLowerInvariant();
            b = b.ToLowerInvariant();

            var previous = new int[b.Length + 1];
            var current = new int[b.Length + 1];

            for (var j = 0; j <= b.Length; j++)
                previous[j] = j;

            for (var i = 1; i <= a.Length; i++)
            {
                current[0] = i;
                for (var j = 1; j <= b.Length; j++)
                {
                    var substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                    var deletion = previous[j] + 1;
                    var insertion = current[j - 1] + 1;
                    current[j] = Math.Min(substitution, Math.Min(deletion, insertion));
                }

                var swap = previous;
                previous = current;
                current = swap;
            }

            return previous[b.Length];
        }
    }
}
