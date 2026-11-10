using System;
using System.Collections.Generic;
using Unity.Pipeline.MethodReload;
using UnityEditor;
using UnityEditor.Networking.PlayerConnection;
using UnityEngine;
using UnityEngine.Networking.PlayerConnection;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Editor
{
    /// <summary>
    /// Editor side of method-reload push signing. Owns the per-player session table (nonce announced by
    /// each player's HandshakeMsg) and produces signed <see cref="PushEnvelope"/>s with the project key
    /// (<c>Library/Pipeline/signing.key</c>, created at build time — never here: a key no build has
    /// baked would sign pushes nothing accepts, so its absence should say "build first").
    ///
    /// The session table is editor-memory only, so every domain reload empties it. Recovery is the
    /// handshake protocol: on load we broadcast RequestHandshake; a push whose target has no session yet is
    /// deferred a couple of seconds while a directed RequestHandshake round-trips (see
    /// <see cref="PushOrDefer"/>) — watch-mode saves right after a script recompile keep working.
    ///
    /// Counters only need to be strictly increasing per player session and are bound to the signed
    /// nonce, so they are seeded from UTC ticks: a fresh editor session (or another machine holding a
    /// copy of the key) continues above whatever a previous editor session used, without the player
    /// having to report where the count stands.
    /// </summary>
    [InitializeOnLoad]
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    internal static class PushSigner
    {
        private sealed class Session
        {
            public byte[] Nonce;
            public long Counter;
            public string Fingerprint;   // key fingerprint the player expects ("" = no key baked)
            public int ProtocolVersion;
        }

        // How long a push waits for the player's handshake before it is dropped. An idle IL2CPP
        // player on the same machine took longer than 2 s to answer the first request after the
        // editor's domain reload (verified live), which silently lost the first push.
        private const double DeferSeconds = 10.0;

        private static readonly Dictionary<int, Session> s_Sessions = new Dictionary<int, Session>();
        private static readonly List<(double deadline, int playerId, byte[] payload, string desc)> s_Deferred =
            new List<(double, int, byte[], string)>();
        private static bool s_DeferPumpHooked;
        private static bool s_Registered;

        static PushSigner()
        {
            // EditorConnection is not reliably initializable during InitializeOnLoad itself.
            EditorApplication.delayCall += RegisterAndRequestHandshakes;
            AssemblyReloadEvents.beforeAssemblyReload += Unregister;
        }

        private static void RegisterAndRequestHandshakes()
        {
            try
            {
                EditorConnection.instance.Initialize();
                EditorConnection.instance.Register(PipelineMethodReloadConnect.HandshakeMsg, OnHandshake);
                s_Registered = true;

                // A domain reload just wiped the session table; ask every connected player to
                // re-announce so it is warm again before anyone pushes.
                var players = EditorConnection.instance.ConnectedPlayers;
                if (players != null && players.Count > 0)
                    EditorConnection.instance.Send(PipelineMethodReloadConnect.RequestHandshakeMsg, RequestPayload());
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PushReload] Could not register the push-signing handshake handler: {ex.Message}");
            }
        }

        private static void Unregister()
        {
            s_Deferred.Clear();
            if (s_DeferPumpHooked)
            {
                EditorApplication.update -= PumpDeferred;
                s_DeferPumpHooked = false;
            }
            if (!s_Registered) return;
            try { EditorConnection.instance.Unregister(PipelineMethodReloadConnect.HandshakeMsg, OnHandshake); }
            catch { /* connection may already be gone during teardown */ }
            s_Registered = false;
        }

        // Non-empty payload: some transport paths drop zero-length messages.
        private static byte[] RequestPayload() => new byte[] { PushEnvelope.ProtocolVersion };

        // Main thread (EditorConnection dispatches there, same as the ack handler in PushReloadCommand).
        private static void OnHandshake(MessageEventArgs args)
        {
            if (!PipelineMethodReloadConnect.TryDecodeHandshake(args.data, out var version, out var fingerprint, out var nonce))
            {
                Debug.LogWarning($"[PushReload] Undecodable handshake from player {args.playerId} — its pushes will fail.");
                return;
            }
            if (nonce == null || nonce.Length != PushEnvelope.NonceSize)
            {
                Debug.LogWarning($"[PushReload] Handshake from player {args.playerId} carries a bad session nonce — its pushes will fail.");
                return;
            }

            bool isNew = !s_Sessions.TryGetValue(args.playerId, out var existing)
                         || !NoncesEqual(existing.Nonce, nonce);
            s_Sessions[args.playerId] = new Session
            {
                Nonce = nonce,
                Counter = s_Sessions.TryGetValue(args.playerId, out var prev) ? prev.Counter : 0,
                Fingerprint = fingerprint ?? "",
                ProtocolVersion = version,
            };

            if (isNew)
            {
                var local = LocalFingerprint();
                var expected = string.IsNullOrEmpty(fingerprint) ? "none (no key baked!)" : fingerprint;
                var session = $"player {args.playerId} session: protocol v{version}, expects key {expected}";
                if (local != null && !string.IsNullOrEmpty(fingerprint) && local != fingerprint)
                    // Actionable right now — every push to this player would be rejected — so it stays
                    // a warning of its own rather than waiting for the first reload.
                    Debug.LogWarning($"[PushReload] {session} — MISMATCH with this machine's key {local}: rebuild the player, " +
                                     $"or copy Library/Pipeline/{PushSigningKey.KeyFileName} from the machine that built it.");
                else
                    // A player connecting is only interesting once something is pushed to it; a
                    // connected dev player with the watcher off used to log this on every connect.
                    MethodReloadRegistry.NoteForFirstReload(session);
            }

            PumpDeferred(); // a deferred push may have been waiting exactly for this handshake
        }

        private static bool NoncesEqual(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        internal static bool HasSession(int playerId) => s_Sessions.ContainsKey(playerId);

        internal static string LocalFingerprint() =>
            PushSigningKey.TryLoad(PushSigningKey.DefaultDirectory, out var key)
                ? PushEnvelope.Fingerprint(key)
                : null;

        /// <summary>
        /// Sign <paramref name="payload"/> for one player and send it, or — if that player's handshake
        /// has not arrived (typical right after an editor domain reload) — request one and defer the
        /// send for up to a couple of seconds. Returns a short status for the caller's summary line;
        /// deferred/failed details are also logged here.
        /// </summary>
        internal static PushSendStatus PushOrDefer(int playerId, byte[] payload, string desc)
        {
            if (s_Sessions.ContainsKey(playerId))
                return SignAndSend(playerId, payload, desc) ? PushSendStatus.Sent : PushSendStatus.Failed;

            try
            {
                EditorConnection.instance.Send(PipelineMethodReloadConnect.RequestHandshakeMsg, RequestPayload(), playerId);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PushReload] Could not request a handshake from player {playerId}: {ex.Message}");
            }

            s_Deferred.Add((EditorApplication.timeSinceStartup + DeferSeconds, playerId, payload, desc));
            if (!s_DeferPumpHooked)
            {
                EditorApplication.update += PumpDeferred;
                s_DeferPumpHooked = true;
            }
            return PushSendStatus.Deferred;
        }

        private static bool SignAndSend(int playerId, byte[] payload, string desc)
        {
            var session = s_Sessions[playerId];

            if (!PushSigningKey.TryLoad(PushSigningKey.DefaultDirectory, out var privateKey))
            {
                Debug.LogError($"[PushReload] {desc}: no push-signing key at Library/Pipeline/{PushSigningKey.KeyFileName}. " +
                    "It is created at build time — build the player once on this machine, or copy the key file from the machine that built it.");
                return false;
            }

            var local = PushEnvelope.Fingerprint(privateKey);
            if (string.IsNullOrEmpty(session.Fingerprint))
            {
                Debug.LogError($"[PushReload] {desc}: player {playerId} has no baked push key and rejects all pushes. " +
                    "Rebuild with the Pipeline build processor enabled (it bakes the key into RuntimePipelineBuildInfo).");
                return false;
            }
            if (session.Fingerprint != local)
            {
                Debug.LogError($"[PushReload] {desc}: player {playerId} expects key {session.Fingerprint}, this machine has {local}. " +
                    $"Rebuild the player, or copy Library/Pipeline/{PushSigningKey.KeyFileName} from the machine that built it.");
                return false;
            }

            // Strictly increasing per session; UTC-tick seeding keeps a fresh editor domain (counter
            // state lost) above anything a previous editor session already used.
            session.Counter = Math.Max(session.Counter + 1, DateTime.UtcNow.Ticks);

            try
            {
                var envelope = PushEnvelope.Sign(privateKey, session.Nonce, session.Counter, payload);
                EditorConnection.instance.Send(PipelineMethodReloadConnect.ApplyReloadMsg, envelope, playerId);
                Commands.PushReloadCommand.TrackAck(desc + $" → player {playerId}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PushReload] {desc}: signing/sending to player {playerId} failed: {ex.Message}");
                return false;
            }
        }

        private static void PumpDeferred()
        {
            if (s_Deferred.Count == 0)
            {
                if (s_DeferPumpHooked)
                {
                    EditorApplication.update -= PumpDeferred;
                    s_DeferPumpHooked = false;
                }
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            // Drain in insertion (FIFO) order. The counter is assigned at send time, so sending the
            // oldest ready push first keeps the newest push on the highest counter; draining
            // newest-first would sign a later push with a lower counter and the player would then
            // apply the stale earlier override last. Rebuild the queue with the entries that were
            // neither sent nor expired, preserving their order.
            var remaining = new List<(double deadline, int playerId, byte[] payload, string desc)>(s_Deferred.Count);
            foreach (var entry in s_Deferred)
            {
                var (deadline, playerId, payload, desc) = entry;

                if (s_Sessions.ContainsKey(playerId))
                {
                    if (SignAndSend(playerId, payload, desc))
                        Debug.Log($"[PushReload] {desc}: deferred push sent to player {playerId} (handshake arrived).");
                    continue;
                }

                if (now >= deadline)
                {
                    Debug.LogWarning($"[PushReload] {desc}: no handshake from player {playerId} within {DeferSeconds:0}s — push dropped. " +
                        "Is the player build made with a signing-aware com.unity.pipeline package? Reconnect or restart the player and retry.");
                    continue;
                }

                remaining.Add(entry);
            }
            s_Deferred.Clear();
            s_Deferred.AddRange(remaining);
        }
    }

    internal enum PushSendStatus
    {
        Sent,
        Deferred,
        Failed,
    }
}
