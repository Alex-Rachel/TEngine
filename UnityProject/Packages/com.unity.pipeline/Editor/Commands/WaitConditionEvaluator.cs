using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Unity.Pipeline;
using Unity.Pipeline.Editor.Authoring;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.Pipeline.Editor.Commands
{
    /// <summary>
    /// Thrown when a wait condition cannot be resolved or evaluated (bad member path, unresolved
    /// target, unreadable member, un-coercible operand). Distinct from a timeout: the wait reports
    /// these as a <c>failed</c> result with this message, never as a spurious <c>timedOut</c>
    /// (unless <c>tolerate-missing</c> is set, in which case resolution failures are retried as
    /// condition-not-met until the wait's own budget expires) — UNLESS <see cref="IsPermanent"/> is
    /// set, for a failure no amount of retrying can ever resolve (a malformed member path, a
    /// findType naming a non-<see cref="UnityEngine.Object"/> type, a member that does not exist on
    /// the resolved type): those fail fast even under tolerate-missing, whose contract is "does not
    /// exist YET", not "will never exist".
    /// </summary>
    internal sealed class WaitEvaluationException : Exception
    {
        internal bool IsPermanent { get; }

        public WaitEvaluationException(string message, bool isPermanent = false) : base(message)
        {
            IsPermanent = isPermanent;
        }
    }

    /// <summary>
    /// Cached resolution state for one wait's condition. The expensive part of member resolution —
    /// <see cref="WaitConditionEvaluator.ResolveType"/>'s whole-appdomain type scan — is cached here
    /// instead of running once per poll: a successful scan never repeats, and a failed one is only
    /// retried after the loaded-assembly set changes (relevant to <c>tolerate-missing</c> waits on
    /// not-yet-loadable paths). A <c>findType</c> instance binds once and re-scans
    /// only when the bound instance is destroyed; an explicit <c>target</c> handle is re-resolved
    /// every poll, because it can legitimately change mid-wait. Member lookups are
    /// memoized per encountered runtime type, so steady-state polls perform dictionary hits, not
    /// reflection walks.
    ///
    /// Created when the wait is registered (pure reflection state — safe off the main thread);
    /// afterwards it is only touched by <see cref="WaitJob.PollOnce"/> on the main thread.
    /// </summary>
    internal sealed class WaitConditionPlan
    {
        internal readonly WaitConditionInput Condition;
        internal readonly string[] Segments;

        /// <summary>Resolved type of a static member path's leading prefix (cached after the first successful scan).</summary>
        internal Type StaticType;

        /// <summary>Number of leading segments consumed by <see cref="StaticType"/>.</summary>
        internal int StaticSplit;

        /// <summary>Resolved <c>findType</c> (cached after the first successful scan).</summary>
        internal Type FindTypeResolved;

        /// <summary>The live instance a <c>findType</c> wait is bound to; re-resolved only when it
        /// has been destroyed (never silently re-bound while alive).</summary>
        internal Object BoundInstance;

        /// <summary>The ambiguity note captured when <see cref="BoundInstance"/> was bound,
        /// re-surfaced on every poll for the wait result.</summary>
        internal string BoundNote;

        /// <summary>Ambiguity note from type resolution (a bare type name matching several loaded
        /// types), captured once when the scan resolves and re-surfaced on every poll.</summary>
        internal string TypeNote;

        /// <summary>
        /// Signature of the loaded-assembly SET observed by the last FAILED type-resolution scan
        /// (combines identity hashes, not just <c>Count</c> — an unload-and-load-of-a-different-
        /// assembly can leave the count unchanged while still making a previously-unresolvable name
        /// resolvable). A failed scan is only re-attempted once this changes: within a wait's
        /// lifetime (waits don't survive domain reloads) the only way an unresolvable name becomes
        /// resolvable is a newly loaded assembly (e.g. <c>run_script</c>/<c>eval</c> loading one).
        /// Without this, a <c>tolerate-missing</c> wait on a never-resolving path would repeat the
        /// whole-appdomain scan every poll. Null means "never scanned" — distinct from any signature
        /// value, including a coincidental 0 — so the very first call always rescans.
        /// </summary>
        internal long? FailedScanAssemblySignature;

        /// <summary>Non-fatal note from the latest resolution (e.g. an ambiguous findType match), surfaced on the wait result.</summary>
        internal string ResolutionNote;

        private readonly Dictionary<(Type Lookup, int Segment, bool IsStatic), MemberInfo> m_Members =
            new Dictionary<(Type, int, bool), MemberInfo>();

        internal WaitConditionPlan(WaitConditionInput condition)
        {
            Condition = condition;
            Segments = (condition?.Member ?? string.Empty).Trim().Split('.');
        }

        internal bool TryGetCachedMember(Type lookup, int segment, bool isStatic, out MemberInfo member)
            => m_Members.TryGetValue((lookup, segment, isStatic), out member);

        internal void CacheMember(Type lookup, int segment, bool isStatic, MemberInfo member)
            => m_Members[(lookup, segment, isStatic)] = member;
    }

    /// <summary>
    /// Reads and compares a member value for the <c>wait_for</c> command (AUTHAPI-25).
    ///
    /// Member resolution mirrors the rules AUTHAPI-16's invoke/query surface will use (that shared
    /// resolver does not exist yet, so this is a self-contained implementation the two can later
    /// converge on — see PR notes):
    ///  - <c>target</c> (an <see cref="Unity.Pipeline.Models.ObjectRef"/>) resolves via
    ///    <see cref="ObjectResolver"/>; the member path then walks instance members from it.
    ///  - <c>findType</c> resolves a UnityEngine.Object type and walks from a live (non-persistent,
    ///    unhidden) instance — prefab assets and hidden editor internals are filtered out, scene
    ///    instances are preferred, and an ambiguous match binds deterministically to the lowest
    ///    instance id with a note on the result.
    ///  - Otherwise the member path is a fully-qualified static path (<c>Ns.Type.StaticMember.Nested</c>).
    ///
    /// Only readable fields and properties are resolved — write-only members and methods are rejected,
    /// so the surface is read-only by construction.
    /// </summary>
    internal static class WaitConditionEvaluator
    {
        // PUBLIC members only, deliberately: wait conditions observe the same surface any C#
        // consumer of the API sees. Reading private/internal state by name would let the `wait`
        // tag expose internals below eval's capability gate (a host can disable eval but allow
        // wait) — anyone who genuinely needs private state has eval/run_script on this server.
        private const BindingFlags MemberFlags = BindingFlags.Public;

        /// <summary>Convenience overload for one-shot reads (tests): builds a throwaway plan.</summary>
        internal static object ReadMember(WaitConditionInput condition, out Type memberType)
            => ReadMember(new WaitConditionPlan(condition), out memberType);

        /// <summary>
        /// Resolve the plan's member path and return the current value plus its declared type (used
        /// to coerce the comparison operand). Type resolution results are cached on
        /// <paramref name="plan"/> so repeated polls never repeat the appdomain scan; only the live
        /// instance (target / findType) is re-resolved per call. Throws
        /// <see cref="WaitEvaluationException"/> on any resolution failure.
        /// </summary>
        internal static object ReadMember(WaitConditionPlan plan, out Type memberType)
        {
            memberType = typeof(object);
            if (plan == null || plan.Condition == null)
                throw new WaitEvaluationException("condition is required.", isPermanent: true);

            plan.ResolutionNote = null;
            var condition = plan.Condition;
            var path = (condition.Member ?? string.Empty).Trim();
            if (path.Length == 0)
                throw new WaitEvaluationException("condition.member is required.", isPermanent: true);

            var segments = plan.Segments;

            object current;
            Type staticType = null;
            int start;
            var isFindTypeInstance = false;

            if (condition.Target != null && !condition.Target.IsEmpty)
            {
                // A malformed globalId is a property of the input STRING itself — no amount of
                // retrying ever makes it parseable, unlike every other TryResolve failure below
                // (empty/unknown/not-yet-loaded), which can legitimately resolve once the
                // referenced object appears — the same "wait for a spawned object" story `target`
                // shares with `findType`. Checked locally rather than widening ObjectResolver's
                // public TryResolve (20+ call sites) with a permanence flag it has no other use for.
                if (!string.IsNullOrEmpty(condition.Target.GlobalId) &&
                    !GlobalObjectId.TryParse(condition.Target.GlobalId, out _))
                    throw new WaitEvaluationException(
                        $"Invalid condition.target.globalId '{condition.Target.GlobalId}'.", isPermanent: true);

                // Re-resolved every poll on purpose: the referenced object can appear/change mid-wait.
                if (!ObjectResolver.TryResolve(condition.Target, out var obj, out var err))
                    throw new WaitEvaluationException($"Could not resolve condition.target: {err}");
                current = obj;
                start = 0;
            }
            else if (!string.IsNullOrEmpty(condition.FindType))
            {
                // The type scan is cached (failures too, until a new assembly loads); the instance
                // lookup stays per-poll (it can legitimately change).
                var type = plan.FindTypeResolved;
                if (type == null && ShouldRescan(plan))
                {
                    type = plan.FindTypeResolved = ResolveType(condition.FindType, out var typeNote);
                    if (type != null)
                        plan.TypeNote = typeNote;
                }
                if (type == null)
                    throw new WaitEvaluationException($"Unknown find-type '{condition.FindType}'.");
                if (plan.BoundInstance is Object boundInstance && boundInstance)
                {
                    // Bound once, reused until destroyed: re-scanning every loaded object on every
                    // poll (up to ~60/s at the minimum interval) scales with scene size for no
                    // benefit — the bind is deterministic anyway. Destruction of the bound
                    // instance triggers a re-scan, so the spawn-wait and replaced-after-destroy
                    // stories keep working.
                    current = boundInstance;
                }
                else
                {
                    current = FindInstance(type, out var note)
                        ?? throw new WaitEvaluationException($"No live instance of type '{type.FullName}' (persistent assets and hidden objects are excluded).");
                    plan.BoundInstance = (Object)current;
                    plan.BoundNote = note;
                }
                plan.ResolutionNote = plan.BoundNote;
                start = 0;
                isFindTypeInstance = true;
            }
            else
            {
                // Static path: find the longest leading dotted prefix that resolves to a type,
                // leaving at least one trailing member segment. Resolved once, then cached.
                if (segments.Length < 2)
                    // A property of the input STRING, not of anything that could load later —
                    // permanent regardless of tolerate-missing.
                    throw new WaitEvaluationException(
                        $"Static member path '{path}' must be 'Type.Member'; supply 'target' or 'find-type' for instance members.",
                        isPermanent: true);

                if (plan.StaticType == null && ShouldRescan(plan))
                {
                    for (var k = segments.Length - 1; k >= 1; k--)
                    {
                        var prefix = string.Join(".", segments, 0, k);
                        var t = ResolveType(prefix, out var typeNote);
                        if (t != null)
                        {
                            plan.StaticType = t;
                            plan.StaticSplit = k;
                            plan.TypeNote = typeNote;
                            break;
                        }
                    }
                }

                if (plan.StaticType == null)
                    throw new WaitEvaluationException(
                        $"Could not resolve a type from static path '{path}'. Supply 'target' or 'find-type' for instance members.");

                staticType = plan.StaticType;
                current = null;
                start = plan.StaticSplit;
            }

            object value = current;
            var firstStatic = current == null;

            for (var i = start; i < segments.Length; i++)
            {
                var segment = segments[i];
                var isStatic = value == null && firstStatic;

                if (value == null && !firstStatic)
                    throw new WaitEvaluationException($"Member path '{path}' hit null before segment '{segment}'.");

                // An object-typed `== null` misses UnityEngine.Object's fake-null: a destroyed
                // object would pass and the next getter would throw a raw
                // MissingReferenceException instead of a structured evaluation failure.
                if (value is Object destroyedCheck && !destroyedCheck)
                    throw new WaitEvaluationException(
                        $"Member path '{path}' hit a destroyed Unity object before segment '{segment}'.");

                var lookup = value?.GetType() ?? staticType;
                if (lookup == null)
                    throw new WaitEvaluationException($"Could not determine a type to resolve segment '{segment}'.", isPermanent: true);

                // TryReadMember's failures (no such readable member, write-only, indexer) are
                // ordinarily properties of the resolved TYPE's shape, which cannot change without
                // a domain reload — and a wait does not survive one. Permanent: never worth
                // retrying. EXCEPT the very first segment off a findType-bound instance: which
                // concrete subtype got bound is a deterministic-but-arbitrary tie-break among
                // whichever live candidates exist right now (FindInstance's lowest-instance-id
                // pick), not a fixed property of the wait's target the way `target`/static paths
                // are — a sibling instance of a different subtype (already alive, or not yet
                // spawned) might have this member even though the one we happened to bind doesn't.
                // Failing fast here would defeat tolerate-missing's "wait for the right kind of
                // spawned object" story; retrying until timeout is the honest answer instead.
                var ambiguousCandidate = isFindTypeInstance && i == start;
                if (!TryReadMember(plan, i, lookup, segment, isStatic, value, out var next, out var nextType, out var readError))
                    throw new WaitEvaluationException(readError, isPermanent: !ambiguousCandidate);

                value = next;
                memberType = nextType;
                firstStatic = false;
            }

            if (plan.TypeNote != null)
                plan.ResolutionNote = plan.ResolutionNote == null
                    ? plan.TypeNote
                    : plan.TypeNote + " " + plan.ResolutionNote;

            // A destroyed Unity object as the TERMINAL value is observed as null — Unity's own
            // == semantics — so a condition can express "wait until destroyed" as
            // { op: "equals", value: null }. (Traversal THROUGH a destroyed object still throws
            // the structured error above; only the leaf value normalizes.)
            if (value is Object terminalUnityObject && !terminalUnityObject)
                value = null;

            if (memberType == typeof(object) && value != null)
                memberType = value.GetType();

            return value;
        }

        /// <summary>
        /// Evaluate an operator against a freshly read <paramref name="current"/> value. For
        /// <c>changed</c>, compares against <paramref name="initialValue"/> (the first observed value)
        /// and never needs an operand. All other operators coerce the JSON operand to
        /// <paramref name="memberType"/> first. Sets <paramref name="error"/> (and returns false) when
        /// the operator or operand cannot be applied.
        /// </summary>
        internal static bool Evaluate(string op, object current, object operandRaw, Type memberType,
            object initialValue, bool haveInitial, out string error)
        {
            error = null;
            // Mirrors WaitForCommands.ValidateCondition's normalization exactly: an empty string is
            // as much "not specified" as a null, and the two must agree, or a condition that passed
            // validation as a legal equals-wait (op:"") would fail every poll on "Unknown op ''".
            op = string.IsNullOrEmpty(op) ? "equals" : op.Trim();

            if (op == "changed")
                return haveInitial && !ValuesEqual(current, initialValue);

            var operandIsNull = operandRaw == null
                || (operandRaw is JToken nullToken && nullToken.Type == JTokenType.Null);
            if (operandIsNull && (op == "equals" || op == "notEquals"))
            {
                // A null operand is meaningful for (in)equality — including "wait until
                // destroyed", since ReadMember observes a destroyed leaf as null. Bypass coercion
                // (there is no type to coerce null to) and compare null-ness directly,
                // fake-null-aware for direct evaluator calls.
                var isNull = current == null || (current is Object unityCurrent && !unityCurrent);
                return op == "equals" ? isNull : !isNull;
            }

            if (!TryCoerceOperand(operandRaw, memberType, out var operand, out error))
                return false;

            switch (op)
            {
                case "equals":
                    return ValuesEqual(current, operand);
                case "notEquals":
                    return !ValuesEqual(current, operand);
                case "greaterThan":
                case "lessThan":
                    if (!TryCompareNumeric(current, operand, out var comparison, out error))
                        return false;
                    return op == "greaterThan" ? comparison > 0 : comparison < 0;
                case "contains":
                    if (!(current is string s))
                    {
                        error = "op 'contains' requires a string member.";
                        return false;
                    }
                    var sub = operand as string ?? operand?.ToString() ?? string.Empty;
                    return s.IndexOf(sub, StringComparison.Ordinal) >= 0;
                default:
                    error = $"Unknown op '{op}'.";
                    return false;
            }
        }

        /// <summary>
        /// Resolve a type by assembly-qualified name, full name, or simple name across loaded
        /// assemblies. This is the expensive whole-appdomain scan — callers cache the result on the
        /// wait's <see cref="WaitConditionPlan"/> so it runs at most once per wait, not per poll.
        /// </summary>
        internal static Type ResolveType(string name) => ResolveType(name, out _);

        internal static Type ResolveType(string name, out string ambiguityNote)
        {
            ambiguityNote = null;
            if (string.IsNullOrEmpty(name))
                return null;

            var direct = Type.GetType(name, throwOnError: false);
            if (direct != null)
                return direct;

            foreach (var asm in PipelineUtils.GetLoadedAssemblies())
            {
                var t = asm.GetType(name, throwOnError: false);
                if (t != null)
                    return t;
            }

            // A dotted name can never equal a simple Type.Name (which never contains '.') — skip
            // the whole-appdomain scan below for it. This matters: ReadMember's static-path prefix
            // loop calls ResolveType once per FAILING dotted prefix (e.g. "Ns.Type.Nested", "Ns.Type"
            // for a 4-segment path), and without this guard every one of those burns a full scan that
            // can never succeed.
            if (name.IndexOf('.') >= 0)
                return null;

            // Simple-name fallback: a bare name can match several loaded types (e.g. "Debug" in
            // UnityEngine and System.Diagnostics). Bind DETERMINISTICALLY (lowest full name
            // ordinally) instead of assembly-enumeration order, and surface the ambiguity as a
            // note so the caller learns to fully qualify.
            Type chosen = null;
            List<string> matches = null;
            foreach (var asm in PipelineUtils.GetLoadedAssemblies())
            {
                foreach (var candidate in SafeGetTypes(asm))
                {
                    // No FullName check here: the asm.GetType(name) pass above already covered
                    // every exact-full-name match, so by this point only simple names can hit.
                    if (candidate.Name != name)
                        continue;
                    if (chosen == null || string.CompareOrdinal(candidate.FullName, chosen.FullName) < 0)
                        chosen = candidate;
                    (matches ?? (matches = new List<string>())).Add(candidate.FullName);
                }
            }

            if (matches != null && matches.Count > 1)
            {
                matches.Sort(StringComparer.Ordinal);
                ambiguityNote = $"Type name '{name}' matched {matches.Count} loaded types " +
                    $"({string.Join(", ", matches.GetRange(0, Math.Min(matches.Count, 4)))}" +
                    (matches.Count > 4 ? ", ..." : "") +
                    $"); bound to '{chosen.FullName}'. Use the fully-qualified name to disambiguate.";
            }

            return chosen;
        }

        /// <summary>
        /// Gate for retrying a previously failed type-resolution scan: true only when the set of
        /// loaded assemblies has changed since the last attempt (recording a signature of the
        /// current set, so the next failure waits for the next actual change). If the scan succeeds
        /// the positive cache on the plan short-circuits all later polls and the recorded signature
        /// is never consulted again.
        /// </summary>
        private static bool ShouldRescan(WaitConditionPlan plan)
        {
            var signature = AssemblySetSignature(PipelineUtils.GetLoadedAssemblies());
            if (signature == plan.FailedScanAssemblySignature)
                return false;
            plan.FailedScanAssemblySignature = signature;
            return true;
        }

        /// <summary>
        /// Identity-based signature of a loaded-assembly set: combines each assembly's own
        /// (identity) hash code, not just the set's <c>Count</c>. An unload-and-load-of-a-different-
        /// assembly (e.g. a hot-reloaded script assembly) can leave the count unchanged while
        /// swapping every member of the set — a count-only gate would then never re-attempt a scan
        /// that just became resolvable.
        /// </summary>
        private static long AssemblySetSignature(IReadOnlyList<Assembly> assemblies)
        {
            unchecked
            {
                long hash = 17;
                for (var i = 0; i < assemblies.Count; i++)
                    hash = hash * 31 + assemblies[i].GetHashCode();
                return hash;
            }
        }

        private static Type[] SafeGetTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types?.Where(t => t != null).ToArray() ?? Array.Empty<Type>();
            }
            catch
            {
                return Array.Empty<Type>();
            }
        }

        /// <summary>
        /// Pick the live instance a <c>findType</c> wait binds to. Persistent objects
        /// (prefab/asset files on disk — <see cref="EditorUtility.IsPersistent"/>) and hidden
        /// editor internals (<c>hideFlags != None</c>) are excluded so the wait observes the live
        /// instance, never the asset. Scene instances are preferred over loose runtime objects;
        /// if several candidates still remain, the lowest instance id wins deterministically and
        /// the ambiguity is reported via <paramref name="note"/> on the wait result.
        /// </summary>
        private static Object FindInstance(Type type, out string note)
        {
            note = null;
            if (!typeof(Object).IsAssignableFrom(type))
                // A property of the resolved TYPE, not of whether an instance exists yet —
                // permanent regardless of tolerate-missing.
                throw new WaitEvaluationException(
                    $"find-type '{type.FullName}' is not a UnityEngine.Object type; use a static member path or a 'target' handle.",
                    isPermanent: true);

            var all = Resources.FindObjectsOfTypeAll(type);
            if (all == null || all.Length == 0)
                return null;

            var candidates = new List<Object>();
            foreach (var o in all)
            {
                if (o == null)
                    continue;
                if (EditorUtility.IsPersistent(o))
                    continue; // a prefab or other asset on disk, not the live instance
                if (o.hideFlags != HideFlags.None)
                    continue; // hidden editor internals / preview objects
                candidates.Add(o);
            }

            if (candidates.Count == 0)
                return null;
            if (candidates.Count == 1)
                return candidates[0];

            var sceneInstances = candidates.Where(IsSceneInstance).ToList();
            var pool = sceneInstances.Count > 0 ? sceneInstances : candidates;
            // Version-stable ids: GetInstanceID() is obsolete-as-error from 6000.4.
            var chosen = pool.OrderBy(o => PipelineUtils.GetObjectId(o).RawValue).First();
            if (pool.Count > 1)
                note = $"find-type '{type.FullName}' matched {pool.Count} live instances; bound to instanceId {PipelineUtils.GetObjectId(chosen).RawValue} (lowest). Use 'target' to select one explicitly.";
            return chosen;
        }

        private static bool IsSceneInstance(Object o)
        {
            var go = o as GameObject ?? (o as Component)?.gameObject;
            return go != null && go.scene.IsValid();
        }

        /// <summary>
        /// Read a single named field or property. The member lookup is memoized on the plan per
        /// encountered runtime type, so only the first poll pays for reflection resolution.
        /// Rejects write-only properties, indexers, and anything that is not a readable field/property.
        /// </summary>
        private static bool TryReadMember(WaitConditionPlan plan, int segmentIndex, Type lookup, string name,
            bool isStatic, object instance, out object value, out Type type, out string error)
        {
            value = null;
            type = null;
            error = null;

            if (!plan.TryGetCachedMember(lookup, segmentIndex, isStatic, out var member))
            {
                if (!TryResolveMember(lookup, name, isStatic, out member, out error))
                    return false;
                plan.CacheMember(lookup, segmentIndex, isStatic, member);
            }

            if (member is PropertyInfo property)
            {
                value = property.GetValue(isStatic ? null : instance);
                type = property.PropertyType;
                return true;
            }

            var field = (FieldInfo)member;
            value = field.GetValue(isStatic ? null : instance);
            type = field.FieldType;
            return true;
        }

        /// <summary>Resolve a named field or property, walking base types for inherited PUBLIC
        /// members. Public-only is a deliberate security boundary (see <see cref="MemberFlags"/>) —
        /// the base walk exists because Type.GetField/GetProperty with declared-only semantics
        /// would miss inherited members, not to reach private state.</summary>
        private static bool TryResolveMember(Type lookup, string name, bool isStatic,
            out MemberInfo member, out string error)
        {
            member = null;
            error = null;

            var flags = MemberFlags | (isStatic ? BindingFlags.Static : BindingFlags.Instance) | BindingFlags.DeclaredOnly;

            for (var t = lookup; t != null; t = t.BaseType)
            {
                var property = t.GetProperty(name, flags);
                if (property != null)
                {
                    if (!property.CanRead)
                    {
                        error = $"Member '{name}' on '{lookup.FullName}' is write-only and cannot be observed.";
                        return false;
                    }
                    if (property.GetIndexParameters().Length > 0)
                    {
                        error = $"Member '{name}' on '{lookup.FullName}' is an indexer and cannot be observed.";
                        return false;
                    }

                    member = property;
                    return true;
                }

                var field = t.GetField(name, flags);
                if (field != null)
                {
                    member = field;
                    return true;
                }
            }

            error = $"No readable field or property '{name}' on type '{lookup.FullName}' " +
                    $"({(isStatic ? "static" : "instance")} members only; methods and write-only members are rejected).";
            return false;
        }

        /// <summary>Coerce a JSON operand (JToken or boxed primitive/string) to the member's runtime type.</summary>
        private static bool TryCoerceOperand(object raw, Type targetType, out object coerced, out string error)
        {
            coerced = null;
            error = null;

            if (raw == null)
            {
                error = "condition.value is required for this operator.";
                return false;
            }

            try
            {
                var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

                if (underlying.IsEnum)
                {
                    var text = RawToString(raw);
                    if (!string.IsNullOrEmpty(text) && !LooksNumeric(text))
                    {
                        coerced = Enum.Parse(underlying, text, ignoreCase: true);
                        return true;
                    }
                    var numeric = Convert.ToInt64(RawToScalar(raw));
                    coerced = Enum.ToObject(underlying, numeric);
                    return true;
                }

                var token = raw as JToken ?? JToken.FromObject(raw);
                coerced = token.ToObject(underlying);
                return true;
            }
            catch (Exception ex)
            {
                error = $"Could not coerce value '{RawToString(raw)}' to {targetType.Name}: {ex.Message}";
                return false;
            }
        }

        private static string RawToString(object raw)
        {
            if (raw is JToken token)
                return token.Type == JTokenType.Null ? null : token.ToObject<string>();
            return raw?.ToString();
        }

        private static object RawToScalar(object raw)
        {
            if (raw is JValue value)
                return value.Value;
            return raw;
        }

        private static bool LooksNumeric(string text)
        {
            return long.TryParse(text, out _) || double.TryParse(text, out _);
        }

        private static bool ValuesEqual(object a, object b)
        {
            if (a == null && b == null)
                return true;
            if (a == null || b == null)
                return false;

            if (IsNumeric(a) && IsNumeric(b))
            {
                // A float member must be compared at float precision: widening float to double
                // manufactures noise digits (0.9f -> 0.90000003576...) that never equal the double
                // operand parsed from JSON, so 'equals' on a float would never match.
                if (a is float || b is float)
                    return Convert.ToSingle(a).Equals(Convert.ToSingle(b));
                if (a is double || b is double)
                    return Convert.ToDouble(a).Equals(Convert.ToDouble(b));
                // Both integral/decimal: compare exactly via decimal — a double round-trip
                // collapses distinct values above 2^53 (9007199254740993 == 9007199254740992.0d).
                return Convert.ToDecimal(a).Equals(Convert.ToDecimal(b));
            }

            if (a.Equals(b))
                return true;

            // Enum-vs-name / cross-type tolerance (e.g. an enum compared to its string name).
            return string.Equals(a.ToString(), b.ToString(), StringComparison.Ordinal);
        }

        /// <summary>
        /// Ordering comparison that stays exact for integral/decimal operands (decimal compare —
        /// double collapses distinct values above 2^53) and uses double only when a floating-point
        /// side or an enum is genuinely involved.
        /// </summary>
        private static bool TryCompareNumeric(object a, object b, out int comparison, out string error)
        {
            comparison = 0;
            if (IsExactNumeric(a) && IsExactNumeric(b))
            {
                try
                {
                    comparison = Convert.ToDecimal(a).CompareTo(Convert.ToDecimal(b));
                    error = null;
                    return true;
                }
                catch (Exception ex)
                {
                    error = $"Values '{a}' and '{b}' are not comparable: {ex.Message}";
                    return false;
                }
            }

            if (!TryToDouble(a, out var da, out error))
                return false;
            if (!TryToDouble(b, out var db, out error))
                return false;
            comparison = da.CompareTo(db);
            return true;
        }

        private static bool IsExactNumeric(object value) =>
            value is sbyte || value is byte || value is short || value is ushort ||
            value is int || value is uint || value is long || value is ulong || value is decimal;

        private static bool TryToDouble(object value, out double result, out string error)
        {
            result = 0;
            error = null;

            if (value == null)
            {
                error = "Cannot compare a null value numerically.";
                return false;
            }

            if (IsNumeric(value) || value is Enum)
            {
                try
                {
                    result = Convert.ToDouble(value);
                    return true;
                }
                catch (Exception ex)
                {
                    error = $"Value '{value}' is not numeric: {ex.Message}";
                    return false;
                }
            }

            error = $"greaterThan/lessThan require a numeric member; got '{value.GetType().Name}'.";
            return false;
        }

        private static bool IsNumeric(object value)
        {
            switch (value)
            {
                case sbyte _:
                case byte _:
                case short _:
                case ushort _:
                case int _:
                case uint _:
                case long _:
                case ulong _:
                case float _:
                case double _:
                case decimal _:
                    return true;
                default:
                    return false;
            }
        }
    }
}
