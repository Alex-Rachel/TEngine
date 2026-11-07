using System;
using System.Security.Cryptography;
using System.Threading;
#if UNITY_EDITOR
using UnityEditor;
#endif
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Security
{
    /// <summary>
    /// Manages the security token used to authorize Pipeline server requests. In the Editor the token
    /// is persisted in SessionState so it survives domain reloads (but dies with the editor process),
    /// keeping long-lived clients (MCP/IDE) authenticated instead of hitting 401 after every reload.
    /// Player builds use a per-process in-memory token. Published to the instance descriptor (port
    /// file) for CLI discovery; never written to a separate file.
    /// </summary>
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    static class SecurityTokenManager
    {
        // In-editor persistence key. SessionState is session-scoped editor state (wiped on editor exit).
        private const string SessionStateKey = "Unity.Pipeline.SecurityToken";

        // Fast in-memory cache, warmed on the main thread at server start (see BasePipelineServer.Start)
        // so background auth reads a populated static instead of touching SessionState off-thread.
        private static string s_CachedToken;

        // Guards the check-and-set so two threads can't race to generate a token on first access.
        private static readonly object s_Lock = new object();

        // Thread that first touches this class (the main thread, since the server warms on it at start).
        // SessionState is main-thread-only, so it is only touched when running on this thread.
        private static readonly int s_MainThreadId = Thread.CurrentThread.ManagedThreadId;

        /// <summary>
        /// Get or create the session's security token. In the Editor it is persisted in SessionState so
        /// it survives domain reloads (regenerated only on editor restart or via <see cref="ClearCache"/>
        /// / <see cref="RotateToken"/>); player builds generate it once per process.
        /// </summary>
        /// <returns>The current session token.</returns>
        public static string GetOrCreateToken()
        {
            // Fast path: plain read of the warmed cache — no lock, no SessionState.
            var cached = s_CachedToken;
            if (!string.IsNullOrEmpty(cached))
                return cached;

            lock (s_Lock)
            {
                // Re-check: another thread may have generated the token while we waited on the lock;
                // regenerating here would overwrite (and invalidate) the token it just handed out.
                if (!string.IsNullOrEmpty(s_CachedToken))
                    return s_CachedToken;

#if UNITY_EDITOR
                // Main thread only: safe to touch SessionState (the warm path).
                if (Thread.CurrentThread.ManagedThreadId == s_MainThreadId)
                {
                    // Rehydrate a token from earlier this session (survives domain reloads).
                    var persisted = SessionState.GetString(SessionStateKey, string.Empty);
                    var mainToken = string.IsNullOrEmpty(persisted) ? GenerateSecureToken() : persisted;
                    if (string.IsNullOrEmpty(persisted))
                        SessionState.SetString(SessionStateKey, mainToken);
                    // Publish last so a fast-path reader sees either null (and locks) or the persisted token.
                    s_CachedToken = mainToken;
                    return s_CachedToken;
                }
#endif

                // Player builds; or Editor off the main thread with a cold cache, where we generate
                // in-memory rather than touch SessionState off-thread (which would throw/500).
                s_CachedToken = GenerateSecureToken();
                return s_CachedToken;
            }
        }

        /// <summary>
        /// Compare two tokens in length-independent constant time to avoid leaking the expected
        /// token through comparison timing.
        /// </summary>
        /// <param name="a">First token.</param>
        /// <param name="b">Second token.</param>
        /// <returns>True if the tokens are equal.</returns>
        public static bool ConstantTimeEquals(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                return false;

            var diff = a.Length ^ b.Length;
            for (int i = 0; i < a.Length && i < b.Length; i++)
                diff |= a[i] ^ b[i];

            return diff == 0;
        }

        /// <summary>
        /// Generate a cryptographically secure token.
        /// </summary>
        private static string GenerateSecureToken()
        {
            using (var rng = new RNGCryptoServiceProvider())
            {
                var bytes = new byte[32]; // 256 bits
                rng.GetBytes(bytes);
                return Convert.ToBase64String(bytes);
            }
        }

        /// <summary>
        /// Clear the cached token (and the persisted SessionState copy) so the next call generates a
        /// fresh one. Used by tests and by <see cref="RotateToken"/>. Call on the main thread (touches
        /// SessionState).
        /// </summary>
        public static void ClearCache()
        {
            lock (s_Lock)
            {
                s_CachedToken = null;
#if UNITY_EDITOR
                SessionState.EraseString(SessionStateKey);
#endif
            }
        }

        /// <summary>
        /// Force a new token and return it. Old-token revocation is immediate for request auth
        /// (GetToken validates live); the port file refreshes on the next heartbeat. Call on the main
        /// thread; a command exposing this must be MainThreadRequired and republish the descriptor.
        /// </summary>
        /// <returns>The new token.</returns>
        public static string RotateToken()
        {
            ClearCache();
            return GetOrCreateToken();
        }

        /// <summary>
        /// Test-only: drop the in-memory cache but keep SessionState, to simulate a domain reload
        /// (statics cleared, SessionState survives).
        /// </summary>
        internal static void ResetInMemoryCacheForTests()
        {
            s_CachedToken = null;
        }

        /// <summary>
        /// Test-only: make the given token current again, in memory and in SessionState, so a test
        /// that replaced the token can hand the live server back the one its clients hold. Call on
        /// the main thread (touches SessionState).
        /// </summary>
        /// <param name="token">The token to make current.</param>
        internal static void RestoreTokenForTests(string token)
        {
            lock (s_Lock)
            {
#if UNITY_EDITOR
                SessionState.SetString(SessionStateKey, token);
#endif
                s_CachedToken = token;
            }
        }
    }
}
