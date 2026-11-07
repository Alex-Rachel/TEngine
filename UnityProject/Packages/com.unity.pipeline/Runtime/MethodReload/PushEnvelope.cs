using System;
using System.Security.Cryptography;
using System.Text;

namespace Unity.Pipeline.MethodReload
{
    /// <summary>Outcome of opening a signed push envelope. Everything except <see cref="Ok"/> is a
    /// rejection; the receiver reports them generically on the wire and specifically in its own log.</summary>
    enum PushVerifyStatus
    {
        Ok,
        /// <summary>The receiver has no baked public key — every push is rejected (strict policy).</summary>
        NoKey,
        /// <summary>Structurally not a v1 envelope (bad magic, truncated, absurd lengths).</summary>
        Malformed,
        /// <summary>Envelope parses but the RSA signature does not verify under the baked key.</summary>
        BadSignature,
        /// <summary>Signature is valid but was made for a different session nonce (replay across
        /// sessions/devices, or the editor's nonce table is stale).</summary>
        NonceMismatch,
        /// <summary>Signature and nonce are valid but the counter did not increase (replay within
        /// the session, or out-of-order delivery).</summary>
        CounterNotIncreasing,
        /// <summary>The platform's RSA implementation threw — crypto is unusable here. Surfaced by
        /// the boot self-test; should never be the steady state.</summary>
        CryptoUnavailable,
    }

    /// <summary>
    /// Authenticated envelope for editor → player method-reload pushes, plus the project signing-key
    /// primitives. Pure BCL (System.Security.Cryptography, no Unity API) so it compiles on both
    /// sides and is unit-testable without a connection.
    ///
    /// Wire format (little-endian):
    ///   [magic "UPP1" 4B][session nonce 16B][counter int64][payloadLen int32][payload][RSA signature]
    /// The signature is RSA-2048 / SHA-256 / PKCS#1 v1.5 over UTF8(<see cref="DomainTag"/>) followed
    /// by every envelope byte before the signature, so a signature can never be repurposed by a
    /// future feature signing other content with the same project key.
    ///
    /// Keys are serialized in a deliberately trivial private format (base64 RSAParameters fields)
    /// rather than XML/PKCS#8: RSA.To/FromXmlString and ImportPkcs8PrivateKey both have per-platform
    /// availability gaps across Unity scripting backends; base64ing the raw parameters has none.
    /// </summary>
    static class PushEnvelope
    {
        public const string DomainTag = "unity-pipeline/push-reload/v1";
        public const int ProtocolVersion = 1;
        public const int NonceSize = 16;
        public const int RsaKeySizeBits = 2048;

        private const string PrivatePrefix = "upp1-rsa:";
        private const string PublicPrefix = "upp1-rsa-pub:";
        private const int HeaderSize = 4 + NonceSize + 8 + 4; // magic + nonce + counter + payloadLen
        private const int MaxPayload = 64 * 1024 * 1024;      // sanity bound, far above any real push

        private static readonly byte[] Magic = { (byte)'U', (byte)'P', (byte)'P', (byte)'1' };

        // ---- keys ----

        /// <summary>Generate a fresh RSA-2048 private key (contains the public half).</summary>
        public static string CreatePrivateKey()
        {
            using (var rsa = RSA.Create())
            {
                rsa.KeySize = RsaKeySizeBits;
                var p = rsa.ExportParameters(true);
                return PrivatePrefix + string.Join(".",
                    B64(p.Modulus), B64(p.Exponent), B64(p.D), B64(p.P), B64(p.Q),
                    B64(p.DP), B64(p.DQ), B64(p.InverseQ));
            }
        }

        /// <summary>Extract the shippable public half of a private key.</summary>
        public static string DerivePublicKey(string privateKey)
        {
            var p = ParsePrivate(privateKey);
            return PublicPrefix + B64(p.Modulus) + "." + B64(p.Exponent);
        }

        public static bool IsPublicKey(string key) =>
            !string.IsNullOrEmpty(key) && key.StartsWith(PublicPrefix, StringComparison.Ordinal);

        public static bool IsPrivateKey(string key) =>
            !string.IsNullOrEmpty(key) && key.StartsWith(PrivatePrefix, StringComparison.Ordinal);

        /// <summary>Short stable id of a keypair (SHA-256 over modulus+exponent, first 8 bytes hex).
        /// Accepts the public or the private serialization and gives the same answer.</summary>
        public static string Fingerprint(string key)
        {
            RSAParameters p = IsPublicKey(key) ? ParsePublic(key) : ParsePrivate(key);
            using (var sha = SHA256.Create())
            {
                var data = new byte[p.Modulus.Length + p.Exponent.Length];
                Buffer.BlockCopy(p.Modulus, 0, data, 0, p.Modulus.Length);
                Buffer.BlockCopy(p.Exponent, 0, data, p.Modulus.Length, p.Exponent.Length);
                var hash = sha.ComputeHash(data);
                var sb = new StringBuilder(16);
                for (int i = 0; i < 8; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>Cryptographically random session nonce.</summary>
        public static byte[] CreateNonce()
        {
            var nonce = new byte[NonceSize];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(nonce);
            return nonce;
        }

        // ---- signing (editor side) ----

        public static byte[] Sign(string privateKey, byte[] sessionNonce, long counter, byte[] payload) =>
            Sign(privateKey, sessionNonce, counter, payload, DomainTag);

        /// <summary>Domain-tag overload: exists so tests can prove a signature made under a different
        /// tag is rejected. Production code always signs with <see cref="DomainTag"/>.</summary>
        public static byte[] Sign(string privateKey, byte[] sessionNonce, long counter, byte[] payload, string domainTag)
        {
            if (sessionNonce == null || sessionNonce.Length != NonceSize)
                throw new ArgumentException($"session nonce must be {NonceSize} bytes", nameof(sessionNonce));
            payload = payload ?? Array.Empty<byte>();

            var preamble = new byte[HeaderSize + payload.Length];
            Buffer.BlockCopy(Magic, 0, preamble, 0, 4);
            Buffer.BlockCopy(sessionNonce, 0, preamble, 4, NonceSize);
            WriteInt64(preamble, 4 + NonceSize, counter);
            WriteInt32(preamble, 4 + NonceSize + 8, payload.Length);
            Buffer.BlockCopy(payload, 0, preamble, HeaderSize, payload.Length);

            byte[] sig;
            using (var rsa = RSA.Create())
            {
                rsa.ImportParameters(ParsePrivate(privateKey));
                sig = rsa.SignData(Tagged(domainTag, preamble), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }

            var envelope = new byte[preamble.Length + sig.Length];
            Buffer.BlockCopy(preamble, 0, envelope, 0, preamble.Length);
            Buffer.BlockCopy(sig, 0, envelope, preamble.Length, sig.Length);
            return envelope;
        }

        // ---- opening (player side) ----

        /// <summary>Cheap structural check: does this look like a v1 envelope (as opposed to a
        /// legacy raw payload from a pre-signing editor)?</summary>
        public static bool LooksLikeEnvelope(byte[] data) =>
            data != null && data.Length >= HeaderSize &&
            data[0] == Magic[0] && data[1] == Magic[1] && data[2] == Magic[2] && data[3] == Magic[3];

        /// <summary>Parse without any crypto. Editor-play-mode strip only — never a security gate.</summary>
        public static bool TryParse(byte[] envelope, out byte[] nonce, out long counter, out byte[] payload)
        {
            nonce = null; counter = 0; payload = null;
            if (!LooksLikeEnvelope(envelope)) return false;

            nonce = new byte[NonceSize];
            Buffer.BlockCopy(envelope, 4, nonce, 0, NonceSize);
            counter = ReadInt64(envelope, 4 + NonceSize);
            int payloadLen = ReadInt32(envelope, 4 + NonceSize + 8);
            if (payloadLen < 0 || payloadLen > MaxPayload || HeaderSize + payloadLen > envelope.Length)
                return false;

            payload = new byte[payloadLen];
            Buffer.BlockCopy(envelope, HeaderSize, payload, 0, payloadLen);
            return true;
        }

        /// <summary>
        /// Full verification: structure, signature under <paramref name="publicKey"/>, session nonce
        /// equality, strictly increasing counter. On <see cref="PushVerifyStatus.Ok"/>,
        /// <paramref name="payload"/> and <paramref name="counter"/> are set. Stateless — callers own
        /// the last-counter bookkeeping (see <see cref="PushVerifier"/>).
        /// </summary>
        public static PushVerifyStatus TryOpen(string publicKey, byte[] envelope, byte[] expectedNonce,
            long lastCounter, out long counter, out byte[] payload)
        {
            counter = 0; payload = null;
            if (string.IsNullOrEmpty(publicKey)) return PushVerifyStatus.NoKey;
            if (!LooksLikeEnvelope(envelope)) return PushVerifyStatus.Malformed;

            int payloadLen = ReadInt32(envelope, 4 + NonceSize + 8);
            if (payloadLen < 0 || payloadLen > MaxPayload) return PushVerifyStatus.Malformed;
            int preambleLen = HeaderSize + payloadLen;
            if (preambleLen >= envelope.Length) return PushVerifyStatus.Malformed; // signature must be non-empty

            // Signature first: nothing later is meaningful on unauthenticated bytes.
            try
            {
                var preamble = new byte[preambleLen];
                Buffer.BlockCopy(envelope, 0, preamble, 0, preambleLen);
                var sig = new byte[envelope.Length - preambleLen];
                Buffer.BlockCopy(envelope, preambleLen, sig, 0, sig.Length);

                using (var rsa = RSA.Create())
                {
                    rsa.ImportParameters(ParsePublic(publicKey));
                    if (!rsa.VerifyData(Tagged(DomainTag, preamble), sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                        return PushVerifyStatus.BadSignature;
                }
            }
            catch (FormatException) { return PushVerifyStatus.Malformed; }        // corrupt key string
            catch (Exception) { return PushVerifyStatus.CryptoUnavailable; }

            if (expectedNonce == null || expectedNonce.Length != NonceSize)
                return PushVerifyStatus.NonceMismatch;
            // Constant-time not required (the nonce is not secret; it is a liveness binding), but
            // the full compare is trivial anyway.
            int diff = 0;
            for (int i = 0; i < NonceSize; i++) diff |= envelope[4 + i] ^ expectedNonce[i];
            if (diff != 0) return PushVerifyStatus.NonceMismatch;

            long c = ReadInt64(envelope, 4 + NonceSize);
            if (c <= lastCounter) return PushVerifyStatus.CounterNotIncreasing;

            counter = c;
            payload = new byte[payloadLen];
            Buffer.BlockCopy(envelope, HeaderSize, payload, 0, payloadLen);
            return PushVerifyStatus.Ok;
        }

        /// <summary>
        /// Boot-time probe that the platform can run the verify path at all (RSA import + VerifyData
        /// on a garbage signature must return false, not throw). Catches an IL2CPP/platform crypto
        /// gap on day one instead of as mysterious per-push rejects.
        /// </summary>
        public static bool VerifySelfTest(string publicKey, out string error)
        {
            error = null;
            try
            {
                using (var rsa = RSA.Create())
                {
                    rsa.ImportParameters(ParsePublic(publicKey));
                    var garbageSig = new byte[RsaKeySizeBits / 8];
                    if (rsa.VerifyData(new byte[] { 1, 2, 3 }, garbageSig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                    {
                        error = "garbage signature verified — RSA implementation is broken here";
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        // ---- internals ----

        private static byte[] Tagged(string domainTag, byte[] preamble)
        {
            var tag = Encoding.UTF8.GetBytes(domainTag ?? "");
            var signed = new byte[tag.Length + preamble.Length];
            Buffer.BlockCopy(tag, 0, signed, 0, tag.Length);
            Buffer.BlockCopy(preamble, 0, signed, tag.Length, preamble.Length);
            return signed;
        }

        private static RSAParameters ParsePrivate(string key)
        {
            if (!IsPrivateKey(key)) throw new FormatException("not a " + PrivatePrefix + " private key");
            var f = key.Substring(PrivatePrefix.Length).Split('.');
            if (f.Length != 8) throw new FormatException("private key must have 8 fields");
            return new RSAParameters
            {
                Modulus = D64(f[0]), Exponent = D64(f[1]), D = D64(f[2]), P = D64(f[3]),
                Q = D64(f[4]), DP = D64(f[5]), DQ = D64(f[6]), InverseQ = D64(f[7]),
            };
        }

        private static RSAParameters ParsePublic(string key)
        {
            if (!IsPublicKey(key)) throw new FormatException("not a " + PublicPrefix + " public key");
            var f = key.Substring(PublicPrefix.Length).Split('.');
            if (f.Length != 2) throw new FormatException("public key must have 2 fields");
            return new RSAParameters { Modulus = D64(f[0]), Exponent = D64(f[1]) };
        }

        private static string B64(byte[] b) => Convert.ToBase64String(b ?? Array.Empty<byte>());
        private static byte[] D64(string s) => Convert.FromBase64String(s);

        private static void WriteInt32(byte[] buf, int off, int v)
        {
            buf[off] = (byte)v; buf[off + 1] = (byte)(v >> 8);
            buf[off + 2] = (byte)(v >> 16); buf[off + 3] = (byte)(v >> 24);
        }

        private static void WriteInt64(byte[] buf, int off, long v)
        {
            for (int i = 0; i < 8; i++) buf[off + i] = (byte)(v >> (8 * i));
        }

        private static int ReadInt32(byte[] buf, int off) =>
            buf[off] | (buf[off + 1] << 8) | (buf[off + 2] << 16) | (buf[off + 3] << 24);

        private static long ReadInt64(byte[] buf, int off)
        {
            long v = 0;
            for (int i = 0; i < 8; i++) v |= (long)buf[off + i] << (8 * i);
            return v;
        }
    }

    /// <summary>
    /// The player-side gate: one per receiver session. Owns the session nonce and the last-accepted
    /// counter, so <see cref="Verify"/> is the single call the receiver makes per push.
    /// </summary>
    sealed class PushVerifier
    {
        private readonly string m_PublicKey;
        private readonly byte[] m_SessionNonce;
        private long m_LastCounter;

        public PushVerifier(string publicKey, byte[] sessionNonce)
        {
            m_PublicKey = publicKey;
            m_SessionNonce = sessionNonce;
        }

        public byte[] SessionNonce => m_SessionNonce;

        public PushVerifyStatus Verify(byte[] envelope, out byte[] payload)
        {
            var status = PushEnvelope.TryOpen(m_PublicKey, envelope, m_SessionNonce, m_LastCounter,
                out var counter, out payload);
            if (status == PushVerifyStatus.Ok)
                m_LastCounter = counter;
            return status;
        }
    }
}
