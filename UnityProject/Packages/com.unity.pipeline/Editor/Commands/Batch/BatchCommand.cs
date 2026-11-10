using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;
using UnityEditor;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Editor.Commands.Batch
{
    /// <summary>
    /// The <c>batch</c> command (AUTHAPI-27): transactional multi-operation execution with cross-op
    /// result references, atomic Undo, and dry-run.
    ///
    /// <para>WHY it exists: a "create → add components → wire fields" job is otherwise 10–90 sequential
    /// calls at ~0.8s each, and <c>eval</c> — the only alternative today — leaves partial state when it
    /// throws mid-way, which is unacceptable on user-owned scenes. <c>batch</c> collapses the round
    /// trips AND adds what eval structurally lacks: atomicity/rollback, dry-run validation, and
    /// cross-step references.</para>
    ///
    /// <para>Scope guard (epic AUTHAPI-24 design principle): batch is transactional object CRUD with
    /// references. It deliberately has NO <c>invoke</c>/loop/conditional op types — that path reinvents
    /// eval inside JSON and collapses the auditability story. Arbitrary logic belongs in
    /// <c>run_script</c> (code as files on disk), not here.</para>
    ///
    /// <para>Dispatch reuse: each sub-operation runs through
    /// <see cref="CommandContext.DispatchOnMainThread"/>, the exact parameter
    /// extraction / required-validation / reflection-invoke path <c>/api/exec</c> uses, so every per-op
    /// result is shaped identically to running that command standalone.</para>
    ///
    /// <para>Atomicity: the whole batch runs inside one <see cref="AuthoringUndoScope"/>, so all ops
    /// (each of which registers its own Undo operations) collapse into a single Editor Undo step. When
    /// <c>transactional</c> (the default) and an op fails under <c>on-error=abort</c>, every applied op
    /// is reverted with <see cref="Undo.RevertAllDownToGroup(int)"/> — the scene returns to its
    /// pre-batch state and there is nothing left on the Undo stack. Rollback is only real for
    /// Undo-tracked mutations, which is why commands that mutate state OUTSIDE the Undo system
    /// (<see cref="m_NonRevertibleCommands"/>) are rejected in a transactional batch — see that field's
    /// doc for the v1 caveat.</para>
    ///
    /// <para>Timing: the batch executes in a single main-thread turn (no frame-spanning), so the
    /// Editor UI is frozen for its duration. <c>timeout</c> is the cooperative bound on that
    /// freeze: the elapsed time is checked before each operation after the first, and when the timeout
    /// is about to run out the remaining ops are skipped and a transactional batch rolls back. Long batches
    /// should be submitted as detached jobs (<c>"job": true</c>) — cancellation is checked between
    /// ops, and per-op progress is reported via <see cref="Unity.Pipeline.CliProgress"/>.</para>
    /// </summary>
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    static class BatchCommand
    {
        const string Group = "job";

        /// <summary>Hard cap on operations per batch (payload bound).</summary>
        private const int MaxOperations = 200;

        /// <summary>
        /// Per-op result size (serialized UTF-8 BYTES — what actually goes on the wire, not UTF-16
        /// chars, which undercount CJK/emoji-heavy payloads 3x) above which the result is replaced
        /// by a truncation marker so a large sub-result cannot bloat the batch envelope.
        /// </summary>
        private const int MaxPerOpResultBytes = 16 * 1024;

        /// <summary>
        /// Aggregate budget for ALL per-op results in one batch response (UTF-8 bytes). Bounds the
        /// whole envelope: 200 ops each just under the per-op cap would otherwise produce a ~3.2 MB
        /// reply. Once exhausted, later results are replaced by truncation markers (the ops still
        /// execute normally — only the echoed result payload is elided).
        /// </summary>
        internal const int MaxBatchResultBytes = 256 * 1024;

        /// <summary>
        /// Operation ids must look like identifiers: they may not be purely numeric (a numeric id
        /// would be ambiguous with a 0-based index in a <c>"$&lt;id-or-index&gt;"</c> reference) and
        /// may not contain <c>'.'</c> (the reference path separator).
        /// </summary>
        private static readonly Regex m_IdPattern = new Regex("^[A-Za-z_][A-Za-z0-9_-]*$", RegexOptions.Compiled);

        /// <summary>
        /// Commands excluded from batch (structured <c>not_batchable</c> error). These either escape
        /// the "object CRUD with references" scope (build/platform/package/play-mode/recompile), carry
        /// arbitrary code that belongs in <c>run_script</c> (<c>eval</c>/<c>eval_file</c>/<c>run_script</c>),
        /// can pop modal dialogs that wedge an unattended batch (<c>menu</c>), or would nest a batch
        /// inside a batch. (The ticket names <c>eval</c>; <c>eval_file</c> is its file-based twin and
        /// is excluded on the same grounds.)
        /// </summary>
        private static readonly HashSet<string> m_ExcludedCommands = new HashSet<string>(StringComparer.Ordinal)
        {
            CommandNames.BuildRun, CommandNames.BuildTargetSwitch,
            CommandNames.EditorPlaymodeStart, CommandNames.EditorPlaymodeStop, CommandNames.EditorPlaymodePause,
            CommandNames.ScriptRecompile, CommandNames.ScriptEval, CommandNames.ScriptEvalFile, CommandNames.ScriptRun, CommandNames.EditorInvokeMenu, CommandNames.EditorQuit, CommandNames.JobBatch,
            // A synchronous wait inside a batch cannot make progress: the batch holds the main
            // thread for its whole turn, so the editor state the condition watches cannot change —
            // the wait just burns its full timeout inside the batch. Use an async wait_for before
            // or after the batch instead. (wait_status/wait_cancel are quick reads and stay
            // batchable.)
            CommandNames.JobWait,
            // Requests a script reload by default (force-domain-reload), on the same grounds as
            // recompile: the domain reload tears down the batch mid-run. Listed explicitly because
            // it carries no Player-surface tag, so the runtime-tag guard does not cover it.
            CommandNames.ScriptMethodreloadCleanup,
            // The Package Manager commands, excluded as a family: they escape the object-CRUD scope,
            // and add/remove/resolve trigger a domain reload that tears the batch down mid-run.
            CommandNames.PackageAdd, CommandNames.PackageList, CommandNames.PackageRemove,
            CommandNames.PackageResolve, CommandNames.PackageSearch, CommandNames.PackageStatus
        };

        /// <summary>
        /// Commands that CLEAR the Editor Undo stack, destroying the batch's ability to revert any
        /// prior operation. Always rejected inside a batch (even with <c>transactional=false</c> the
        /// single-Undo-step contract would silently break).
        ///
        /// <para>V1 CAVEAT: like <see cref="m_NonRevertibleCommands"/> below, this is a curated list —
        /// any new command that clears or truncates the Undo stack must be added here, or it will
        /// silently break the revert-integrity contract. The durable fix is command-site metadata
        /// (<c>[CliGroupCommand(Revertible = ...)]</c>).</para>
        /// </summary>
        private static readonly HashSet<string> m_UndoStackWipingCommands = new HashSet<string>(StringComparer.Ordinal)
        {
            CommandNames.SceneOpen, CommandNames.SceneCreate
        };

        /// <summary>
        /// Commands whose mutations live OUTSIDE the Editor Undo system — asset/file writes
        /// (AssetDatabase / File IO), scene saves, Build Settings, project settings — so
        /// <see cref="Undo.RevertAllDownToGroup(int)"/> cannot roll them back. In a transactional
        /// batch they are rejected (<c>not_batchable_transactional</c>): allowing them would make
        /// <c>reverted: true</c> a lie. With <c>transactional=false</c> they run, and their per-op
        /// result reports <c>revertible: false</c>.
        ///
        /// <para>V1 CAVEAT: this is a curated list, assembled by auditing each command's
        /// implementation (AssetDatabase.SaveAssets / File writes / settings assets). The durable fix
        /// is a <c>[CliGroupCommand(Revertible = ...)]</c> metadata flag declared at the command site so
        /// new commands can't silently fall through; until then, new non-Undo-tracked commands must
        /// be added here.</para>
        /// </summary>
        private static readonly HashSet<string> m_NonRevertibleCommands = new HashSet<string>(StringComparer.Ordinal)
        {
            // Assets & files (AssetDatabase ops / direct file IO).
            CommandNames.AssetCreate, CommandNames.AssetImport, CommandNames.AssetMove, CommandNames.AssetCopy, CommandNames.AssetRename, CommandNames.AssetDelete,
            CommandNames.AssetCreateFolder, CommandNames.AssetSetImportSettings, CommandNames.AssetWriteText, CommandNames.ScriptCreate,
            // Prefab ASSET writes (scene-side prefab ops — instantiate/unpack/revert overrides — are
            // Undo-tracked and stay batchable).
            CommandNames.PrefabCreate, CommandNames.PrefabCreateVariant, CommandNames.PrefabApplyOverrides, CommandNames.PrefabSaveContents,
            // Scene persistence and Build Settings scene list.
            CommandNames.SceneSave, CommandNames.EditorSaveAll, CommandNames.SceneAddToBuild, CommandNames.SceneRemoveFromBuild, CommandNames.BuildSetSettings,
            // Project settings (their own descriptions say "Not undoable via Ctrl+Z").
            CommandNames.SettingsAudioSet, CommandNames.SettingsGraphicsSet, CommandNames.SettingsInputSet, CommandNames.LightingSetSettings,
            CommandNames.NavmeshSetSettings, CommandNames.SettingsPhysicsSet, CommandNames.SettingsPlayerSet, CommandNames.SettingsQualitySet,
            CommandNames.SettingsTagsLayersSet, CommandNames.SettingsTimeSet, CommandNames.AuthoringSetRoot,
#if UNITY_6000_6_OR_NEWER
            CommandNames.SettingsMultiplayerRolesSet,
#endif
            // Writes the Run In Background Player Setting and the Pipeline settings file (CLI-1432).
            CommandNames.EditorPlaymodeSetRunInBackground,
            // Animation / Animator / Timeline commands persist their asset with AssetDatabase.SaveAssets.
            CommandNames.AnimationClipCreate, CommandNames.AnimationClipSetCurve, CommandNames.AnimationClipRemoveCurve,
            CommandNames.AnimationAnimatorCreateController, CommandNames.AnimationAnimatorAddLayer, CommandNames.AnimationAnimatorAddParameter,
            CommandNames.AnimationAnimatorAddState, CommandNames.AnimationAnimatorAddTransition,
            CommandNames.AnimationTimelineCreate, CommandNames.AnimationTimelineAddTrack, CommandNames.AnimationTimelineAddClip,
            // Persists the .mat asset with AssetDatabase.SaveAssetIfDirty (the in-memory change is
            // Undo-recorded, but the disk write is not reverted by Undo).
            CommandNames.MaterialSetProperties,
            // Build Profile assets: create_build_profile writes through the AssetDatabase (and may change
            // the active profile); edit_build_profile saves the asset like set_material_properties.
            CommandNames.BuildCreateProfile, CommandNames.BuildSetProfile,
            // Baking writes/deletes baked data on disk (GI / NavMesh / occlusion).
            CommandNames.LightingBake, CommandNames.LightingClearBake,
            CommandNames.NavmeshBake, CommandNames.NavmeshBakeSurfaces, CommandNames.NavmeshClearBake,
            CommandNames.OcclusionBake, CommandNames.OcclusionClearBake
        };

        [CliGroupCommand(Group,
            "Run multiple registered commands in one transactional request. Later ops can reference earlier " +
            "results with \"$<id-or-index>.<jsonPath>\" (e.g. \"$0.instanceId\"; \"$$\" escapes a literal '$'). " +
            "transactional=true (default) groups every op into one Undo step and reverts all applied ops if any " +
            "op fails; commands that mutate outside the Undo system (asset/file/settings writes) are rejected " +
            "unless transactional=false. on-error=abort|continue (continue forces transactional=false); dry-run " +
            "validates without mutating; timeout (seconds, default 60) bounds execution — when it is about to run out the " +
            "remaining ops are skipped and a transactional batch rolls back (submit long batches with " +
            "\"job\": true). Excludes " + CommandNames.BuildRun + ", " + CommandNames.BuildTargetSwitch + ", the package commands, " +
            CommandNames.EditorPlaymodeStart + "/stop/pause, " + CommandNames.ScriptRecompile + ", " +
            CommandNames.ScriptEval + ", " + CommandNames.ScriptEvalFile + ", " + CommandNames.ScriptRun + ", " +
            CommandNames.EditorInvokeMenu + ", " + CommandNames.SceneOpen + "/" + CommandNames.SceneCreate +
            ", async commands (" + CommandNames.TestRun + "/" + CommandNames.TestList + "), " +
            "runtime-only commands, and nested " + CommandNames.JobBatch + ".",
            MainThreadRequired = true,
            Tags = new[] { "batch" })]
        public static BatchResult Batch(
            [CliArg("operations", "Ordered operations to run; each is { id?, command, params }, with command written as \"<group> <name>\" (e.g. \"gameobject create\"). Max 200.", Required = true)] List<BatchOperationInput> operations = null,
            [CliArg("transactional", "Group all ops into one Undo step and revert every applied op if any op fails. Default true. Forced false when on-error=continue. Rejects commands whose mutations Undo cannot revert (asset/file/settings writes).")] bool transactional = true,
            [CliArg("on-error", "abort (default): stop at the first failing op. continue: run every op, collecting per-op errors (forces transactional=false).")] string onError = "abort",
            [CliArg("dry-run", "Validate command names, parameters, reference topology and excluded commands without mutating anything.")] bool dryRun = false,
            [CliArg("result-fields", "Optional per-op result projection: map of op id-or-index -> array of result field paths to keep (context economy).")] JObject resultFields = null,
            [CliArg("timeout", "Timeout for the whole batch in seconds (default 60). Checked before each op after the first; when it is about to run out the remaining ops are skipped and a transactional batch rolls back. Use \"job\": true for long batches.")] double timeoutInSeconds = 60)
        {
            if (operations == null || operations.Count == 0)
                throw new ArgumentException(CommandNames.JobBatch + " 'operations' must contain at least one operation.");
            if (operations.Count > MaxOperations)
                throw new ArgumentException(
                    $"{CommandNames.JobBatch} supports at most {MaxOperations} operations, but {operations.Count} were provided.");

            onError = string.IsNullOrWhiteSpace(onError) ? "abort" : onError.Trim().ToLowerInvariant();
            if (onError != "abort" && onError != "continue")
                throw new ArgumentException($"{CommandNames.JobBatch} 'on-error' must be 'abort' or 'continue', got '{onError}'.");

            // "continue" collects per-op errors and applies the independent ops, so it cannot also
            // promise an all-or-nothing rollback — it forces transactional off (per the ticket).
            var effectiveTransactional = transactional && onError != "continue";

            var idToIndex = BuildAndValidateIdIndex(operations);

            if (dryRun)
                return DryRun(operations, idToIndex, effectiveTransactional, onError);

            return Execute(operations, idToIndex, effectiveTransactional, onError, resultFields,
                PipelineTimeout.FromSeconds(timeoutInSeconds));
        }

        /// <summary>
        /// Validate operation ids (well-formed and unique when supplied) and build the id → index map
        /// used to resolve <c>"$id.field"</c> references.
        /// </summary>
        private static Dictionary<string, int> BuildAndValidateIdIndex(List<BatchOperationInput> operations)
        {
            var idToIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < operations.Count; i++)
            {
                var op = operations[i];
                if (op == null)
                    throw new ArgumentException($"batch operation at index {i} is null.");
                if (op.Id == null)
                    continue;
                if (!m_IdPattern.IsMatch(op.Id))
                    throw new ArgumentException(
                        $"batch operation id '{op.Id}' (index {i}) is invalid: ids must match " +
                        "^[A-Za-z_][A-Za-z0-9_-]*$ (start with a letter or '_'; letters, digits, '_' or '-' " +
                        "after). Purely numeric ids would be ambiguous with 0-based index selectors in " +
                        "\"$<id-or-index>\" references, and '.' collides with the reference path separator.");
                if (idToIndex.ContainsKey(op.Id))
                    throw new ArgumentException(
                        $"batch operation id '{op.Id}' is used more than once; ids must be unique within a batch.");
                idToIndex[op.Id] = i;
            }
            return idToIndex;
        }

        /// <summary>
        /// Execute the batch: run each op (resolving its references against earlier results) inside one
        /// Undo group, and on a transactional abort revert every applied op. Between ops, cooperative
        /// cancellation and the timeout are checked, and per-op progress is reported.
        /// </summary>
        private static BatchResult Execute(List<BatchOperationInput> operations, Dictionary<string, int> idToIndex,
            bool transactional, string onError, JObject resultFields, TimeSpan timeout)
        {
            var context = CommandContext.Current;
            if (context == null)
                throw new InvalidOperationException(
                    CommandNames.JobBatch + " must run inside a pipeline server request (no active server on this thread).");

            var result = new BatchResult { Transactional = transactional, OnError = onError };
            var resultsByIndex = new JToken[operations.Count];
            var stoppedAt = -1;     // index of the op that failed under on-error=abort
            var skippedFrom = -1;   // first op never attempted because of a batch-level abort (timeout/cancel)
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var remainingResultBytes = MaxBatchResultBytes;

            using (var undoScope = new AuthoringUndoScope(CommandNames.JobBatch))
            {
                var batchGroup = Undo.GetCurrentGroup();
                result.UndoGroup = batchGroup;

                for (int i = 0; i < operations.Count; i++)
                {
                    // Cooperative cancellation (detached jobs: POST /api/job/cancel) — abort between
                    // ops and let the transactional rollback below restore the pre-batch state.
                    if (PipelineCancellation.IsCancellationRequested)
                    {
                        result.Error = "batch canceled: cooperative cancellation was requested; " +
                            (transactional ? "applied operations were reverted and " : "") +
                            "remaining operations were skipped.";
                        skippedFrom = i;
                        break;
                    }

                    // Cooperative timeout: the batch holds the main thread for its whole duration,
                    // so this is the bound on how long the Editor stays frozen. Op 0 always gets to
                    // run; every later op requires time left before the stop point.
                    if (i > 0 && !PipelineTimeout.IsInfinite(timeout) && stopwatch.Elapsed >= PipelineTimeout.StopBefore(timeout))
                    {
                        result.Error = $"batch timeout about to run out: {stopwatch.ElapsedMilliseconds}ms elapsed " +
                            $"after {i} of {operations.Count} operations ({PipelineTimeout.Describe(timeout)} timeout). " +
                            (transactional ? "Applied operations were reverted and remaining operations were skipped. "
                                           : "Remaining operations were skipped. ") +
                            "For long batches submit the request with \"job\": true or a larger --timeout.";
                        skippedFrom = i;
                        break;
                    }

                    var op = operations[i];
                    CliProgress.Report(CommandNames.JobBatch, $"op {i + 1}/{operations.Count}: {OperationLabel(op.Command)}",
                        i, operations.Count, i / (double)operations.Count);

                    var opResult = new BatchOperationResult { Id = op.Id, Command = OperationLabel(op.Command) };
                    result.Results.Add(opResult);

                    try
                    {
                        var command = ResolveBatchable(op.Command, transactional);
                        opResult.Revertible = !m_NonRevertibleCommands.Contains(command.FullName);
                        var resolvedParams = BatchReferenceResolver.Resolve(
                            op.Params, i, operations, idToIndex, resultsByIndex, result.Results);

                        // Dispatched WITHOUT re-checking the executor's settle gate:
                        // the batch itself was admitted through it when the executor took it, the settle
                        // gate is a one-way latch that never re-arms mid-session, and the batch
                        // holds the main thread for its entire turn — the Editor cannot transition
                        // into a busy state between two ops of the same batch.
                        var raw = context.DispatchOnMainThread(command, resolvedParams,
                            PipelineTimeout.IsInfinite(timeout) ? timeout : timeout - stopwatch.Elapsed);

                        // Capture the result as a token for later references. A serialization failure
                        // here must NOT fail an op that already ran — leave the slot null (a later
                        // reference into it will error clearly) and still report success.
                        try
                        {
                            resultsByIndex[i] = raw == null ? JValue.CreateNull() : JToken.FromObject(raw);
                        }
                        catch
                        {
                            resultsByIndex[i] = null;
                        }

                        opResult.Success = true;
                        opResult.Result = ProjectResult(raw, resultsByIndex[i], op, i, resultFields, opResult, ref remainingResultBytes);
                        result.Applied++;
                    }
                    catch (Exception ex)
                    {
                        opResult.Success = false;
                        opResult.Error = ex.Message;

                        if (onError == "abort")
                        {
                            stoppedAt = i;
                            break;
                        }
                        // on-error=continue: keep going with the remaining independent ops.
                    }
                }

                if ((stoppedAt >= 0 || skippedFrom >= 0) && transactional)
                {
                    // Revert every mutation registered since the batch started — one atomic rollback
                    // to the pre-batch state (nothing is left on the Undo stack). Deliberately NOT
                    // conditioned on Applied > 0: a FIRST op that partially mutates and then throws
                    // has registered exactly the Undo operations this revert must unwind, and
                    // reverting an empty group is harmless.
                    Undo.RevertAllDownToGroup(batchGroup);
                    result.Reverted = true;
                    // The revert discarded batchGroup from the Undo stack; the scope must not
                    // collapse a group id that no longer refers to this batch's operations.
                    undoScope.Cancel();
                }
            }

            // Record the operations that were never attempted because the batch aborted (op failure
            // under on-error=abort, cancellation, or a timeout about to run out), so the caller can see
            // exactly where it stopped.
            var firstSkipped = skippedFrom >= 0 ? skippedFrom
                : stoppedAt >= 0 ? stoppedAt + 1
                : operations.Count;
            for (int j = firstSkipped; j < operations.Count; j++)
            {
                result.Results.Add(new BatchOperationResult
                {
                    Id = operations[j].Id,
                    Command = OperationLabel(operations[j].Command),
                    Success = false,
                    Skipped = true
                });
            }

            return result;
        }

        /// <summary>
        /// Validate every operation without executing any of it: command names, that no excluded /
        /// non-batchable command is used (including the transactional non-revertible rejection),
        /// unknown/missing parameters, and reference topology (unknown/forward refs). Mutates nothing.
        /// </summary>
        private static BatchResult DryRun(List<BatchOperationInput> operations, Dictionary<string, int> idToIndex,
            bool transactional, string onError)
        {
            var result = new BatchResult
            {
                DryRun = true,
                Valid = true,
                Transactional = transactional,
                OnError = onError
            };
            for (int i = 0; i < operations.Count; i++)
            {
                var op = operations[i];
                var opResult = new BatchOperationResult { Id = op.Id, Command = OperationLabel(op.Command) };
                result.Results.Add(opResult);

                try
                {
                    var command = ResolveBatchable(op.Command, transactional);
                    opResult.Revertible = !m_NonRevertibleCommands.Contains(command.FullName);
                    ValidateKnownParameters(command, op.Params);
                    ValidateRequiredParameters(command, op.Params);
                    BatchReferenceResolver.Validate(op.Params, i, operations, idToIndex);
                    opResult.Success = true; // would run
                }
                catch (Exception ex)
                {
                    opResult.Success = false;
                    opResult.Error = ex.Message;
                    result.Valid = false;
                }
            }

            return result;
        }

        /// <summary>
        /// Resolve an operation's <c>command</c> to its <see cref="CommandInfo"/> — written as
        /// <c>group name</c> (<c>gameobject create</c>) or as a single token in its one-word
        /// spelling — and reject non-batchable commands. Resolution comes FIRST and the policy checks
        /// run on the resolved command's full name, so a command is refused however it was typed: the excluded list
        /// (<c>not_batchable</c>), Undo-stack-wiping scene loads, runtime-only commands, async
        /// (<c>Task</c>-returning) commands — which are completed by <c>EditorApplication.update</c>
        /// callbacks that can never fire while the batch holds the main thread, so awaiting them
        /// inline would freeze the Editor permanently — and, when <paramref name="transactional"/>,
        /// commands whose mutations Undo cannot revert (<c>not_batchable_transactional</c>). An
        /// unknown command gets an <c>unknown command</c> error, a group a "is a group" one. Errors
        /// name the command in its <c>group name</c> form. Resolution is quiet (no server-side
        /// logging) so dry-run validation of an intentionally bad name stays quiet.
        /// </summary>
        private static CommandInfo ResolveBatchable(string typed, bool transactional)
        {
            var command = ResolveOperationCommand(typed);
            var name = command.FullName;

            if (IsExcluded(command.FullName))
                throw new ArgumentException(
                    $"Command '{name}' is not batchable (not_batchable): commands that build, switch platform, " +
                    "manage packages, control play mode, recompile, evaluate code, execute Editor menu items " +
                    "(which can open modal dialogs and wedge an unattended batch), or nest a batch are excluded.");

            if (m_UndoStackWipingCommands.Contains(command.FullName))
                throw new ArgumentException(
                    $"Command '{name}' is not batchable (not_batchable): it clears the Editor Undo stack, which " +
                    "would make every prior batch operation unrevertible. Call it standalone instead.");

            if (command.HasTagInSubtree(CommandInfo.RuntimeTag))
                throw new ArgumentException(
                    $"Command '{name}' is not batchable (not_batchable): it belongs to the Player command " +
                    $"surface (tagged '{CommandInfo.RuntimeTag}'); it cannot execute on this Editor server.");

            if (typeof(Task).IsAssignableFrom(command.Method.ReturnType))
                throw new ArgumentException(
                    $"Command '{name}' is not batchable (not_batchable): async commands cannot run inside a " +
                    "synchronous batch. They complete via EditorApplication.update callbacks, which cannot fire " +
                    "while the batch holds the main thread — the batch would deadlock the Editor. Call it " +
                    "standalone instead.");

            if (transactional && m_NonRevertibleCommands.Contains(command.FullName))
                throw new ArgumentException(
                    $"Command '{name}' is not batchable in a transactional batch (not_batchable_transactional): " +
                    "it mutates state outside the Editor Undo system (asset/file/settings writes), so a rollback " +
                    "could not revert it. Run the batch with transactional:false, or call the command standalone.");

            return command;
        }

        /// <summary>
        /// Resolve the text of an operation's <c>command</c> the way a raw request names a command:
        /// the first token a group makes the second a name in it, a group alone is refused, and
        /// otherwise the single token is the one-word spelling of a full name. The core is the one
        /// /api/exec uses (<see cref="BasePipelineServer.TryResolveRawCommand"/>), without its logging.
        /// </summary>
        private static CommandInfo ResolveOperationCommand(string typed)
        {
            if (string.IsNullOrWhiteSpace(typed))
                throw new ArgumentException("operation 'command' is required.");

            var tokens = typed.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            var resolution = BasePipelineServer.TryResolveRawCommand(tokens, CommandRegistry.Index);

            if (resolution.Miss != null)
            {
                var message = ObsoleteCommandNameMessage.ForMiss(resolution.Miss.TypedName, ObsoleteCommandNamesFile.Load())
                    ?? resolution.Miss.Message;
                // Keep the `unknown command` marker next to the name that missed.
                var head = $"No command named '{resolution.Miss.TypedName}' is available";
                throw new ArgumentException(message.StartsWith(head, StringComparison.Ordinal)
                    ? head + " (unknown command)" + message.Substring(head.Length)
                    : message + " (unknown command)");
            }

            if (resolution.ListedGroup != null)
                throw new ArgumentException(
                    $"'{resolution.ListedGroup}' is a group, not a command. Name a command in it, as '{resolution.ListedGroup} <name>'.");

            // An operation names the command only; its arguments go in 'params'.
            if (resolution.ArgumentsStart != tokens.Length)
                throw new ArgumentException(
                    $"operation 'command' must name only the command, as '<group> <name>', but got '{string.Join(" ", tokens)}'. " +
                    "Pass arguments in 'params'.");

            return resolution.Command;
        }

        /// <summary>
        /// How an operation's command is shown in results and progress: its <c>group name</c> form
        /// when it resolves, otherwise what was typed. Quiet for a name that does not resolve.
        /// </summary>
        private static string OperationLabel(string typed)
        {
            if (string.IsNullOrWhiteSpace(typed))
                return typed;
            try
            {
                var tokens = typed.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                var resolution = BasePipelineServer.TryResolveRawCommand(tokens, CommandRegistry.Index);
                if (resolution.Command != null && resolution.ArgumentsStart == tokens.Length)
                    return resolution.Command.FullName;
                if (resolution.ListedGroup != null)
                    return resolution.ListedGroup;
            }
            catch (Exception)
            {
                // A label must never fail an operation.
            }
            return typed.Trim();
        }

        private static bool IsExcluded(string fullName) => m_ExcludedCommands.Contains(fullName);

        /// <summary>
        /// Reject parameter keys that are not parameters of the command (dry-run check), with the
        /// same rule and wording as a structured request's own <c>parameters</c>.
        /// </summary>
        private static void ValidateKnownParameters(CommandInfo command, JObject parameters)
        {
            var unknown = CommandLineBinder.FindUnknownParameters(command, parameters);
            if (unknown.Count > 0)
                throw new ArgumentException(BasePipelineServer.DescribeArgProblems(command, unknown, structured: true));
        }

        /// <summary>
        /// Ensure every required parameter is present (dry-run check). A reference string counts as
        /// present; an explicit JSON null does not.
        /// </summary>
        private static void ValidateRequiredParameters(CommandInfo command, JObject parameters)
        {
            foreach (var p in command.Parameters)
            {
                if (!p.Required)
                    continue;
                var token = parameters?[p.Name];
                if (token == null || token.Type == JTokenType.Null)
                    throw new ArgumentException(
                        $"Command '{command.FullName}' is missing required parameter '{p.Name}'.");
            }
        }

        /// <summary>
        /// Shape an op's result: apply the optional <c>result-fields</c> projection, then replace an
        /// oversized result with a truncation marker. With neither in play the raw result object is
        /// returned unchanged, so it serializes byte-identically to the standalone command.
        /// </summary>
        internal static object ProjectResult(object raw, JToken rawToken, BatchOperationInput op, int index,
            JObject resultFields, BatchOperationResult opResult, ref int remainingResultBytes)
        {
            object projected = raw;

            var fields = LookupResultFields(resultFields, op, index);
            if (fields != null && rawToken != null && rawToken.Type != JTokenType.Null)
            {
                var projection = new JObject();
                List<string> invalidPaths = null;
                foreach (var path in fields)
                {
                    if (string.IsNullOrEmpty(path))
                        continue;
                    JToken value;
                    try
                    {
                        value = rawToken.SelectToken(path);
                    }
                    catch (Exception)
                    {
                        // A malformed JSONPath must NEVER throw here: by this point the op has
                        // already run and mutated state, and the outer catch would flip its
                        // Success to false — triggering a rollback of a succeeded op under
                        // on-error=abort, and poisoning later $refs into a perfectly valid
                        // captured result under on-error=continue. Report the bad path in the
                        // projection instead.
                        (invalidPaths ?? (invalidPaths = new List<string>())).Add(path);
                        continue;
                    }
                    if (value != null)
                        projection[path] = value;
                }
                if (invalidPaths != null)
                    projection["invalidResultFields"] = new JArray(invalidPaths);
                projected = projection;
            }

            string json;
            try
            {
                // Measure with the same formatting the wire envelope uses: since the lean envelope
                // (AUTHAPI-21) /api/exec serializes compact (Formatting.None) in every mode, so the
                // measurement matches what actually goes on the wire.
                json = JsonConvert.SerializeObject(projected, Formatting.None);
            }
            catch (Exception ex)
            {
                // A result Newtonsoft cannot serialize would fail (or bloat) the whole batch envelope
                // later. Replace it with a structured marker — and still flag resultTruncated — rather
                // than skipping truncation for exactly the results we know nothing about.
                opResult.ResultTruncated = true;
                return new JObject
                {
                    ["resultTruncated"] = true,
                    ["error"] = $"op result could not be serialized: {ex.Message}"
                };
            }

            var bytes = System.Text.Encoding.UTF8.GetByteCount(json);
            if (bytes > MaxPerOpResultBytes)
            {
                opResult.ResultTruncated = true;
                return new JObject
                {
                    ["resultTruncated"] = true,
                    ["length"] = bytes,
                    ["preview"] = json.Substring(0, Math.Min(json.Length, 512))
                };
            }

            if (bytes > remainingResultBytes)
            {
                // Aggregate budget exhausted: the op executed normally, only its echoed result is
                // elided so the whole batch reply stays bounded ($refs still resolve — they read
                // the captured token, not this projection).
                opResult.ResultTruncated = true;
                return new JObject
                {
                    ["resultTruncated"] = true,
                    ["length"] = bytes,
                    ["error"] = $"batch aggregate result budget ({MaxBatchResultBytes} bytes) exhausted; " +
                        "the operation ran and later references to it still resolve, only this echoed result was elided. " +
                        "Use result-fields to project results down, or split the batch.",
                    ["preview"] = json.Substring(0, Math.Min(json.Length, 512))
                };
            }
            remainingResultBytes -= bytes;

            return projected;
        }

        /// <summary>
        /// Look up the projection field list for an op in <c>result-fields</c> (keyed by op id, then
        /// by index). Returns null when no projection is configured for the op.
        /// </summary>
        private static string[] LookupResultFields(JObject resultFields, BatchOperationInput op, int index)
        {
            if (resultFields == null)
                return null;

            JToken entry = null;
            if (!string.IsNullOrEmpty(op.Id))
                entry = resultFields[op.Id];
            if (entry == null)
                entry = resultFields[index.ToString()];
            if (entry == null)
                return null;

            if (entry.Type == JTokenType.Array)
                return entry.ToObject<string[]>();
            if (entry.Type == JTokenType.String)
                return new[] { (string)entry };
            return null;
        }
    }
}
