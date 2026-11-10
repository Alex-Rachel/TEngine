using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Compilation;
using Unity.Pipeline.MethodReload;
using UnityEditor;
using UnityEditor.Networking.PlayerConnection;
using UnityEngine;
using UnityEngine.Networking.PlayerConnection;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Editor.Commands
{
    /// <summary>
    /// Compile a source file's [MethodReload] override(s) in the editor and push the IL to a connected
    /// player over PlayerConnection — the Profiler/Console channel (tunnels over USB, no open port or
    /// token). The player applies it through the IlInterpreter interpreter (IL2CPP-safe: no Assembly.Load,
    /// no Roslyn on device).
    ///
    /// <c>filename</c> may be a single .cs file or a folder: a folder is walked recursively and every
    /// .cs that opts into method reload is compiled and pushed once. For a continuous watch, use the Hot
    /// Reload Interpreter Watch section of the Pipeline settings page, Project Settings > Pipeline > Editor (<see cref="EditorMethodReloadWatcher"/>).
    ///
    /// Requires a Development Build connected to the editor (Build Settings → Autoconnect Profiler,
    /// or attach via the Profiler/Console) with the runtime Pipeline enabled. The player's
    /// ack is asynchronous and logged to the editor console.
    /// </summary>
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    static class PushReloadCommand
    {
        const string Group = "script.methodreload";

        private static bool s_Registered;

        // Pending-ack watchdog. A push that reaches no receiver (e.g. the runtime Pipeline isn't
        // enabled in the player) is otherwise indistinguishable from success: the editor logs
        // "pushed … (ack async)" and then nothing, forever. One entry per expected ack; entries are
        // matched to acks oldest-first (acks carry no correlation id). Editor-session static: a
        // domain reload drops pending entries together with the ack handler itself.
        private const double AckTimeoutSeconds = 5.0;
        private static readonly List<(double deadline, string desc)> s_PendingAcks = new List<(double, string)>();
        private static bool s_AckPumpHooked;

        static PushReloadCommand()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Unregister;
        }

        [CliGroupCommand(Group,
            "Compile a file's (or folder's) [MethodReload] method(s) and push the IL to a connected player (IL2CPP-safe) over PlayerConnection. Folders are walked recursively and pushed once.",
            MainThreadRequired = true,
            Tags = new[] { "scripts/methodreload" })]
        public static object ReloadPlayer(
            [CliArg("filename", "Source .cs file or a folder containing [MethodReload] methods (e.g. Assets/pong.cs or Assets/Scripts)", Required = true)] string filename,
            [CliArg("player", "Target connected player id; -1 = broadcast to all connected players")] int player = -1)
        {
            if (string.IsNullOrWhiteSpace(filename))
                return new { success = false, error = "filename is required" };

            if (!TryResolvePath(filename, out var fullPath, out var isFolder))
                return new { success = false, error = $"Could not locate source file or folder: {filename}" };

            if (isFolder)
                return PushFolderOnce(fullPath, player);

            var ok = DoCompileAndSend(fullPath, player, out var summary, out var diagnostics);
            return new { success = ok, message = summary, diagnostics };
        }

        /// <summary>Compile and push every reloadable .cs under a folder once. Files that don't opt into
        /// method reload are skipped (not errors).</summary>
        private static object PushFolderOnce(string folder, int player)
        {
            List<string> files;
            try
            {
                files = Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
                    .Where(FileMentionsMethodReload)
                    .ToList();
            }
            catch (Exception ex)
            {
                return new { success = false, error = $"Could not enumerate {folder}: {ex.Message}" };
            }

            if (files.Count == 0)
                return new { success = false, error = $"No .cs files mentioning [MethodReload] found under {folder}" };

            var pushed = new List<string>();
            var failed = new List<object>();
            foreach (var f in files)
            {
                if (DoCompileAndSend(f, player, out var summary, out var diagnostics))
                    pushed.Add(Path.GetFileName(f));
                else
                    failed.Add(new { file = Path.GetFileName(f), error = summary, diagnostics });
            }

            return new
            {
                success = failed.Count == 0,
                message = $"Pushed {pushed.Count}/{files.Count} file(s) from {DisplayName(folder)}" +
                    (pushed.Count > 0 ? $": {string.Join(", ", pushed)}" : ""),
                pushed,
                failed
            };
        }

        /// <summary>Watcher-save push: like <see cref="ReloadPlayer"/> for a single file, but diffed
        /// against the watch-start <see cref="MethodReloadBaseline"/> — methods still matching it are
        /// left running compiled on the device, and a fully up-to-date file skips the compile and
        /// send entirely. The explicit CLI command stays unfiltered as the escape hatch when the
        /// connected player was NOT built from the watched sources (the baseline can't see a
        /// device's build state).</summary>
        internal static void PushChanged(string fullPath, int player)
        {
            DoCompileAndSend(fullPath, player, out _, out _, useBaseline: true);
        }

        /// <summary>Re-push the watched file's/folder's method-reload overrides to one player, compiled
        /// from current source. Called by <see cref="EditorMethodReloadWatcher"/> when a player
        /// (re)connects while a Player-target watch is active: a restarted player boots the original
        /// AOT code, so everything pushed during its previous run is gone until re-pushed.</summary>
        internal static void RepushAll(string fullPath, bool isFolder, int player)
        {
            if (!isFolder)
            {
                DoCompileAndSend(fullPath, player, out _, out _, useBaseline: true); // logs success and failure itself
                return;
            }

            List<string> files;
            try
            {
                files = Directory.EnumerateFiles(fullPath, "*.cs", SearchOption.AllDirectories)
                    .Where(FileMentionsMethodReload)
                    .ToList();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PushReload] Re-push to player {player} failed — could not enumerate {fullPath}: {ex.Message}");
                return;
            }

            int pushed = 0;
            foreach (var f in files)
                if (DoCompileAndSend(f, player, out _, out _, useBaseline: true))
                    pushed++;
            Debug.Log($"[PushReload] Re-pushed {pushed}/{files.Count} reloadable file(s) to player {player}.");
        }

        /// <summary>Preprocessor symbols the target player's copy of this file was compiled with:
        /// the defines of the player-variant assembly that owns the file, falling back to any
        /// player assembly's defines, then null (= editor defines downstream) if the compilation
        /// pipeline can't answer. Cheap relative to the compile that follows.</summary>
        internal static string[] PlayerDefinesFor(string fullPath)
        {
            try
            {
                var assemblies = UnityEditor.Compilation.CompilationPipeline
                    .GetAssemblies(UnityEditor.Compilation.AssembliesType.Player);
                var norm = Path.GetFullPath(fullPath).Replace('\\', '/');
                foreach (var a in assemblies)
                    foreach (var f in a.sourceFiles)
                        if (Path.GetFullPath(f).Replace('\\', '/')
                            .Equals(norm, StringComparison.OrdinalIgnoreCase))
                            return a.defines;
                return assemblies.Length > 0 ? assemblies[0].defines : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Compile the file's overrides and send them to the target player(s). Never throws.
        /// Failures are logged to the editor console as well as returned: the method-reload watcher
        /// (Player target) discards the return value, so the console is the only surface where a
        /// failing watch-mode push is visible at all.</summary>
        private static bool DoCompileAndSend(string fullPath, int player, out string summary, out List<string> diagnostics,
            bool useBaseline = false)
        {
            diagnostics = null;
            try
            {
                EnsureRegistered();

                // Compile with the PLAYER's preprocessor symbols, not the editor's: the pushed body
                // must resolve #if UNITY_EDITOR / platform regions the way the player's own
                // assemblies did, or the override diverges from the code it replaces.
                var playerDefines = PlayerDefinesFor(fullPath);
                // Methods already overridden on a device must reload even when they match the
                // baseline again: there is no remote unregister, so the only way to clear a stale
                // override is to replace it with one compiled from the (reverted) source.
                var mustReload = useBaseline ? GetPushedMethods(fullPath) : null;

                // Up-to-date pre-check BEFORE the pending notice: announcing a compile and then
                // sending nothing would leave the device overlay stuck on "compiling…".
                if (useBaseline && FileIsUpToDate(fullPath, playerDefines, mustReload))
                {
                    summary = "up to date — every [MethodReload] method matches the watch-start baseline; nothing pushed";
                    Debug.Log($"[PushReload] {Path.GetFileName(fullPath)}: {summary}");
                    return true;
                }

                // Compilation happens here in the editor — announce it so the player's status overlay
                // can show "compiling…" during the gap before the payload (or failure notice) arrives.
                TrySendNotice(PipelineMethodReloadConnect.ReloadPendingMsg, Path.GetFileName(fullPath), player);

                var compiled = InPlaceReloadProcessor.CompileOverrideForPush(fullPath, playerDefines,
                    useBaseline ? mustReload : null, useBaseline);
                diagnostics = compiled.Diagnostics;

                // The pre-check should have caught this; a save landing between the two reads can
                // still get here. Nothing to push either way.
                if (compiled.AllUpToDate)
                {
                    summary = "up to date — every [MethodReload] method matches the watch-start baseline; nothing pushed";
                    Debug.Log($"[PushReload] {Path.GetFileName(fullPath)}: {summary}");
                    return true;
                }
                if (!compiled.Success)
                {
                    summary = "compile failed: " + (compiled.Error ?? "unknown");
                    var details = diagnostics != null && diagnostics.Count > 0
                        ? ":\n- " + string.Join("\n- ", diagnostics)
                        : "";
                    Debug.LogError($"[PushReload] {Path.GetFileName(fullPath)}: {summary}{details}");
                    TrySendNotice(PipelineMethodReloadConnect.ReloadFailedMsg,
                        $"{Path.GetFileName(fullPath)}: {summary} (details in the editor console)", player);
                    return false;
                }

                // Run the same interpreter load pass the player runs on receipt, so binding gaps come
                // back in THIS response — the player's async ack carries only a warning count (the
                // member list goes to the device log). Never blocks the push: an unbound member is a
                // stub that throws only if executed.
                var unbound = InterpreterMethodReloadExecutor.ValidateBindingSurface(
                    compiled.IlBytes, compiled.TypeName, compiled.MethodNames, out var validationNote);
                if (unbound.Count > 0)
                {
                    diagnostics ??= new List<string>();
                    foreach (var member in unbound)
                        diagnostics.Add($"binding: host member '{member}' is not registered and will " +
                            "throw if the reloaded code reaches it");
                    Debug.LogWarning($"[PushReload] {Path.GetFileName(fullPath)}: {unbound.Count} host " +
                        $"member(s) referenced by the override are not in the interpreter binding and will " +
                        $"throw if reached: {string.Join(", ", unbound)}. Types outside the standard " +
                        "surface and demand-time auto-bind are not callable from pushed overrides; BCL " +
                        "members outside the standard surface need an explicit AllowBcl shim.");
                }
                if (validationNote != null)
                    Debug.LogWarning($"[PushReload] {Path.GetFileName(fullPath)}: binding validation note: {validationNote}");

                var players = EditorConnection.instance.ConnectedPlayers;
                if (players == null || players.Count == 0)
                {
                    summary = "no player connected (run a Development Build with Autoconnect Profiler)";
                    Debug.LogWarning($"[PushReload] {Path.GetFileName(fullPath)}: {summary}");
                    return false;
                }

                var payload = PipelineMethodReloadConnect.Encode(compiled.TypeName, compiled.MethodNames, compiled.IlBytes);
                var desc = $"{compiled.TypeName} ({Path.GetFileName(fullPath)})";

                // Every player gets its own signed envelope (session nonces differ), so "broadcast"
                // is a per-player loop rather than one connection-wide Send. PushSigner tracks the
                // expected ack per actual send, so no TrackExpectedAcks call here.
                int sent = 0, deferred = 0, failedSends = 0, matched = 0;
                foreach (var target in players)
                {
                    if (player >= 0 && target.playerId != player) continue;
                    matched++;
                    switch (PushSigner.PushOrDefer(target.playerId, payload, desc))
                    {
                        case PushSendStatus.Sent: sent++; break;
                        case PushSendStatus.Deferred: deferred++; break;
                        default: failedSends++; break;
                    }
                }

                if (matched == 0)
                {
                    summary = $"player {player} is not connected";
                    Debug.LogWarning($"[PushReload] {Path.GetFileName(fullPath)}: {summary}");
                    return false;
                }

                // A device can't unregister an override, so record every method sent or deferred — a
                // later baseline-filtered push must re-include it or a reverted edit would strand the
                // device on the stale override.
                RecordPushedMethods(fullPath, compiled.MethodNames);

                summary = $"pushed {compiled.TypeName} ({compiled.MethodNames.Count} method(s), {compiled.IlBytes.Length} B) " +
                          $"signed to {sent} player(s)" +
                          (deferred > 0 ? $"; {deferred} deferred awaiting player handshake" : "") +
                          (failedSends > 0 ? $"; {failedSends} failed (see console)" : "") +
                          (compiled.UpToDateMethods.Count > 0 ? $"; {compiled.UpToDateMethods.Count} method(s) up to date, left compiled" : "") +
                          (unbound.Count > 0 ? $"; {unbound.Count} binding warning(s) — see diagnostics" : "") +
                          " (ack async)";
                Debug.Log($"[PushReload] {summary}");
                return failedSends == 0;
            }
            catch (Exception ex)
            {
                summary = $"push error: {ex.Message}";
                Debug.LogError($"[PushReload] {Path.GetFileName(fullPath)}: {summary}");
                TrySendNotice(PipelineMethodReloadConnect.ReloadFailedMsg,
                    $"{Path.GetFileName(fullPath)}: {summary}", player);
                return false;
            }
        }

        /// <summary>Best-effort overlay notice to the target player(s); a notice failing must never
        /// fail the push itself.</summary>
        private static void TrySendNotice(Guid msg, string text, int player)
        {
            try
            {
                var payload = PipelineMethodReloadConnect.EncodeText(text);
                if (player >= 0)
                    EditorConnection.instance.Send(msg, payload, player);
                else
                    EditorConnection.instance.Send(msg, payload);
            }
            catch { /* overlay notices are cosmetic */ }
        }

        private static void EnsureRegistered()
        {
            EditorConnection.instance.Initialize();
            if (s_Registered) return;
            EditorConnection.instance.Register(PipelineMethodReloadConnect.ResultMsg, OnResult);
            s_Registered = true;
        }

        private static void Unregister()
        {
            s_PendingAcks.Clear();
            if (s_AckPumpHooked)
            {
                EditorApplication.update -= PumpPendingAcks;
                s_AckPumpHooked = false;
            }
            if (!s_Registered) return;
            try { EditorConnection.instance.Unregister(PipelineMethodReloadConnect.ResultMsg, OnResult); }
            catch { /* connection may already be gone during teardown */ }
            s_Registered = false;
        }

        // EditorConnection callbacks are dispatched on the main thread, same as the update pump —
        // no locking needed around s_PendingAcks.
        private static void OnResult(MessageEventArgs args)
        {
            if (s_PendingAcks.Count > 0)
                s_PendingAcks.RemoveAt(0);
            if (PipelineMethodReloadConnect.TryDecodeResult(args.data, out var ok, out var message))
            {
                // A pre-signing player build cannot parse a signed envelope and acks it as malformed
                // — name the actual fix instead of leaving a mystery.
                var hint = !ok && message != null && message.Contains("malformed method-reload push")
                    ? " (if this player build predates push signing, rebuild it with the current com.unity.pipeline package)"
                    : "";
                Debug.Log($"[PushReload] Player ack: {(ok ? "OK" : "FAILED")} — {message}{hint}");
            }
            else
                Debug.LogWarning("[PushReload] Received a player ack that could not be decoded.");
        }

        /// <summary>One expected-ack slot for a push <see cref="PushSigner"/> just sent.</summary>
        internal static void TrackAck(string desc)
        {
            EnsureRegistered();
            TrackExpectedAcks(1, desc);
        }

        private static void TrackExpectedAcks(int count, string desc)
        {
            double deadline = EditorApplication.timeSinceStartup + AckTimeoutSeconds;
            for (int i = 0; i < count; i++)
                s_PendingAcks.Add((deadline, desc));
            if (!s_AckPumpHooked)
            {
                EditorApplication.update += PumpPendingAcks;
                s_AckPumpHooked = true;
            }
        }

        private static void PumpPendingAcks()
        {
            double now = EditorApplication.timeSinceStartup;
            for (int i = s_PendingAcks.Count - 1; i >= 0; i--)
            {
                if (now < s_PendingAcks[i].deadline) continue;
                Debug.LogWarning($"[PushReload] No player ack for {s_PendingAcks[i].desc} within {AckTimeoutSeconds:0}s — " +
                    "is the player still connected, and does it have the runtime Pipeline enabled?");
                s_PendingAcks.RemoveAt(i);
            }
            if (s_PendingAcks.Count == 0)
            {
                EditorApplication.update -= PumpPendingAcks;
                s_AckPumpHooked = false;
            }
        }

        /// <summary>Resolve <paramref name="input"/> to an existing folder or .cs file. Folders are checked
        /// first (so a bare directory name isn't turned into "&lt;dir&gt;.cs"); files get a .cs suffix if missing.</summary>
        private static bool TryResolvePath(string input, out string fullPath, out bool isFolder)
        {
            fullPath = null;
            isFolder = false;

            foreach (var d in new[] { input, Path.Combine("Assets", input) })
            {
                if (Directory.Exists(d))
                {
                    fullPath = Path.GetFullPath(d);
                    isFolder = true;
                    return true;
                }
            }

            var file = input.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ? input : input + ".cs";
            fullPath = ResolveSourceFilePath(file);
            return fullPath != null;
        }

        private static string ResolveSourceFilePath(string filename)
        {
            if (Path.IsPathRooted(filename) && File.Exists(filename))
                return filename;
            foreach (var p in new[] { Path.Combine("Assets", filename), Path.Combine("Assets", "Scripts", filename), filename })
                if (File.Exists(p))
                    return Path.GetFullPath(p);
            return null;
        }

        /// <summary>Every [MethodReload] method in the file still matches the watch-start baseline
        /// (player defines) and none of them was ever pushed. Read failure = not up to date.</summary>
        private static bool FileIsUpToDate(string fullPath, string[] playerDefines, HashSet<string> mustReload)
        {
            try
            {
                var source = File.ReadAllText(fullPath);
                return MethodReloadBaseline.IsFileUpToDate(fullPath, source, playerDefines, mustReload);
            }
            catch
            {
                return false;
            }
        }

        // Method names pushed to any player this editor session, per file. A device can't
        // unregister an override, so once a method is pushed, every later baseline-filtered push
        // must include it again — otherwise reverting an edit would strand the device on the stale
        // override while the editor reports "up to date". SessionState-backed so the play-mode
        // domain reload doesn't forget what connected devices still hold.
        private const string SkPushedMethods = "Pipeline.PushReload.PushedMethods";

        private static HashSet<string> GetPushedMethods(string fullPath)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            var raw = SessionState.GetString(SkPushedMethods, "");
            if (string.IsNullOrEmpty(raw)) return set;
            var key = Path.GetFullPath(fullPath);
            foreach (var line in raw.Split('\n'))
            {
                int sep = line.IndexOf('\u001f');
                if (sep <= 0 || !string.Equals(line.Substring(0, sep), key, StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (var m in line.Substring(sep + 1).Split(';'))
                    if (m.Length > 0)
                        set.Add(m);
            }
            return set;
        }

        private static void RecordPushedMethods(string fullPath, List<string> methods)
        {
            if (methods == null || methods.Count == 0) return;
            var set = GetPushedMethods(fullPath);
            int before = set.Count;
            foreach (var m in methods)
                set.Add(m);
            if (set.Count == before) return;

            var key = Path.GetFullPath(fullPath);
            var lines = new List<string>();
            var raw = SessionState.GetString(SkPushedMethods, "");
            if (!string.IsNullOrEmpty(raw))
                foreach (var line in raw.Split('\n'))
                {
                    int sep = line.IndexOf('\u001f');
                    if (sep > 0 && !string.Equals(line.Substring(0, sep), key, StringComparison.OrdinalIgnoreCase))
                        lines.Add(line);
                }
            lines.Add(key + '\u001f' + string.Join(";", set));
            SessionState.SetString(SkPushedMethods, string.Join("\n", lines));
        }

        // Cheap opt-in filter shared with EditorMethodReloadWatcher: a file that never mentions
        // "MethodReload" cannot carry the attribute, so folder scans skip it without compiling.
        internal static bool FileMentionsMethodReload(string path)
        {
            try { return File.ReadAllText(path).Contains("MethodReload"); }
            catch { return false; }
        }

        private static string DisplayName(string path) =>
            string.IsNullOrEmpty(path) ? "?" : Path.GetFileName(path.TrimEnd('/', '\\'));
    }
}
