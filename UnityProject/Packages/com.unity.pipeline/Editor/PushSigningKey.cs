using System;
using System.IO;
using Unity.Pipeline.MethodReload;
using UnityEngine;

namespace Unity.Pipeline.Editor
{
    /// <summary>
    /// The per-project method-reload push-signing keypair: <c>Library/Pipeline/signing.key</c>.
    ///
    /// Created lazily at build time (see <c>PipelineRuntimeBuildProcessor</c>) and loaded at push
    /// time (see <c>PushSigner</c>). Living under <c>Library/</c> makes committing it structurally
    /// impossible and scopes trust to "the machine that built the player can push to it" — a
    /// teammate either copies the file or rebuilds. Deleting <c>Library/</c> orphans existing
    /// device builds until the next rebuild; the key fingerprint in build and push logs is how
    /// that mismatch names itself.
    /// </summary>
    internal static class PushSigningKey
    {
        internal const string KeyFileName = "signing.key";
        internal const string PublicInfoFileName = "signing.key.pub";

        /// <summary>The project's Library/Pipeline directory (also home of the port file).</summary>
        internal static string DefaultDirectory =>
            Path.Combine(Path.GetDirectoryName(Path.GetFullPath(Application.dataPath)), "Library", "Pipeline");

        /// <summary>Load the private key if the file exists and fully parses.</summary>
        internal static bool TryLoad(string directory, out string privateKey)
        {
            privateKey = null;
            var path = Path.Combine(directory, KeyFileName);
            try
            {
                if (!File.Exists(path)) return false;
                var text = File.ReadAllText(path).Trim();
                if (!PushEnvelope.IsPrivateKey(text)) return false;
                // The prefix alone doesn't prove the body parses. Fully parse the RSA parameters now:
                // a truncated or corrupt "upp1-rsa:..." passes the prefix check, then throws later in
                // DerivePublicKey and fails the build. A parse failure here falls to the catch and is
                // treated as unreadable, so LoadOrCreate regenerates the key — the documented recovery.
                PushEnvelope.DerivePublicKey(text);
                privateKey = text;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Pipeline: could not read push-signing key at {path} ({ex.Message}) — a fresh key will be generated on the next build.");
                return false;
            }
        }

        /// <summary>
        /// Load the project key, generating and persisting a fresh one if missing or unreadable.
        /// Also writes a human-readable <c>signing.key.pub</c> (public half + fingerprint) beside it
        /// for diagnostics and for handing to a teammate's editor.
        /// </summary>
        internal static string LoadOrCreate(string directory, out bool created)
        {
            created = false;
            if (TryLoad(directory, out var existing))
                return existing;

            var privateKey = PushEnvelope.CreatePrivateKey();
            var publicKey = PushEnvelope.DerivePublicKey(privateKey);
            var fingerprint = PushEnvelope.Fingerprint(publicKey);

            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, KeyFileName), privateKey);
            File.WriteAllText(Path.Combine(directory, PublicInfoFileName),
                $"# com.unity.pipeline push-signing public key (fingerprint {fingerprint})\n" +
                "# The private half is signing.key next to this file — never commit or share it.\n" +
                publicKey + "\n");

            created = true;
            return privateKey;
        }
    }
}
