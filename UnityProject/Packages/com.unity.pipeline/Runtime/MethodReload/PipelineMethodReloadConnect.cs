using System;
using System.Collections.Generic;
using System.Text;

namespace Unity.Pipeline.MethodReload
{
    /// <summary>
    /// Shared contract for pushing a compiled in-place method-reload override from the editor to a
    /// running player over Unity's PlayerConnection — the same editor↔player message bus the Profiler
    /// and Console use (tunnels over USB for mobile, no IP or open port required). Lives in Runtime so
    /// both the player (<c>PlayerConnection.Register</c>) and the editor push command
    /// (<c>EditorConnection.Send</c>) reference the same GUIDs and wire format.
    ///
    /// The editor compiles the edited method(s) to override-assembly IL bytes (Roslyn, editor-only)
    /// and ships {typeName, methodNames, il}; the player feeds them to
    /// <c>InterpreterMethodReloadExecutor.Register</c> and runs them through the IlInterpreter interpreter —
    /// no <c>Assembly.Load</c>, so it works under IL2CPP.
    ///
    /// This type touches no Unity API, so it compiles unconditionally on both editor and player.
    /// </summary>
    static class PipelineMethodReloadConnect
    {
        /// <summary>editor → player: apply a compiled override (typeName + method names + IL).</summary>
        public static readonly Guid ApplyReloadMsg = new Guid("b6f1e0d24c7a4e13a9f2c8d5017e3b44");

        /// <summary>player → editor: an ack (ok + human-readable summary) for a received push.</summary>
        public static readonly Guid ResultMsg = new Guid("c2a7d4e916b84f0db3e5a1c6072f8d99");

        /// <summary>editor → player: a save is being compiled for a push (payload = utf8 file name).
        /// Compilation happens editor-side, so this notice is the only way an on-device overlay can
        /// show "compiling…" progress.</summary>
        public static readonly Guid ReloadPendingMsg = new Guid("d4c9f2a1385e46b7a0d1e8c4529b6f77");

        /// <summary>editor → player: the compile for a pending reload failed (payload = utf8 summary).
        /// Without it the on-device overlay would sit on "compiling…" forever after a syntax error.</summary>
        public static readonly Guid ReloadFailedMsg = new Guid("e7b3a8d05c214f9e8b6d2f0a1c435588");

        /// <summary>player → editor: session announcement for push signing — protocol version, the
        /// key fingerprint the player's baked public key expects, and the random session nonce every
        /// signed push must carry. Sent on connect and in answer to <see cref="RequestHandshakeMsg"/>.</summary>
        public static readonly Guid HandshakeMsg = new Guid("f19c5b8a2d7e40c6b8a3f0d612c94e55");

        /// <summary>editor → player: re-announce your handshake (empty payload). Sent when the editor's
        /// nonce table is empty for a target — typically right after an editor domain reload.</summary>
        public static readonly Guid RequestHandshakeMsg = new Guid("a8d2c7f4691e4b0f9c5d3a80e17b6c22");

        /// <summary>
        /// Wire format: [typeNameLen:int32-LE][utf8 typeName][methodCount:int32-LE]
        /// (per method: [len:int32-LE][utf8 name]) [il bytes…].
        /// </summary>
        public static byte[] Encode(string typeName, IReadOnlyList<string> methodNames, byte[] il)
        {
            var methods = methodNames ?? Array.Empty<string>();

            var nameBytes = Encoding.UTF8.GetBytes(typeName ?? "");
            var methodBytes = new byte[methods.Count][];
            int methodsSize = 4; // count
            for (int i = 0; i < methods.Count; i++)
            {
                methodBytes[i] = Encoding.UTF8.GetBytes(methods[i] ?? "");
                methodsSize += 4 + methodBytes[i].Length;
            }

            int ilLen = il?.Length ?? 0;
            var buf = new byte[4 + nameBytes.Length + methodsSize + ilLen];
            int o = 0;
            o = WriteInt32(buf, o, nameBytes.Length);
            o = WriteBytes(buf, o, nameBytes);
            o = WriteInt32(buf, o, methods.Count);
            for (int i = 0; i < methods.Count; i++)
            {
                o = WriteInt32(buf, o, methodBytes[i].Length);
                o = WriteBytes(buf, o, methodBytes[i]);
            }
            if (ilLen > 0) Buffer.BlockCopy(il, 0, buf, o, ilLen);
            return buf;
        }

        public static bool TryDecode(byte[] data, out string typeName, out string[] methodNames, out byte[] il)
        {
            typeName = null; methodNames = null; il = null;
            if (data == null || data.Length < 4) return false;
            try
            {
                int o = 0;
                int nameLen = ReadInt32(data, ref o);
                if (nameLen < 0 || o + nameLen > data.Length) return false;
                typeName = Encoding.UTF8.GetString(data, o, nameLen); o += nameLen;

                if (o + 4 > data.Length) return false;
                int methodCount = ReadInt32(data, ref o);
                if (methodCount < 0) return false;
                methodNames = new string[methodCount];
                for (int i = 0; i < methodCount; i++)
                {
                    if (o + 4 > data.Length) return false;
                    int len = ReadInt32(data, ref o);
                    if (len < 0 || o + len > data.Length) return false;
                    methodNames[i] = Encoding.UTF8.GetString(data, o, len); o += len;
                }

                int ilLen = data.Length - o;
                il = new byte[ilLen];
                if (ilLen > 0) Buffer.BlockCopy(data, o, il, 0, ilLen);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Handshake wire format: [protocolVersion:int32-LE][fpLen:int32-LE][utf8 fingerprint][nonce…].</summary>
        public static byte[] EncodeHandshake(int protocolVersion, string keyFingerprint, byte[] sessionNonce)
        {
            var fp = Encoding.UTF8.GetBytes(keyFingerprint ?? "");
            var nonce = sessionNonce ?? Array.Empty<byte>();
            var buf = new byte[4 + 4 + fp.Length + nonce.Length];
            int o = 0;
            o = WriteInt32(buf, o, protocolVersion);
            o = WriteInt32(buf, o, fp.Length);
            o = WriteBytes(buf, o, fp);
            if (nonce.Length > 0) Buffer.BlockCopy(nonce, 0, buf, o, nonce.Length);
            return buf;
        }

        public static bool TryDecodeHandshake(byte[] data, out int protocolVersion, out string keyFingerprint, out byte[] sessionNonce)
        {
            protocolVersion = 0; keyFingerprint = null; sessionNonce = null;
            if (data == null || data.Length < 8) return false;
            try
            {
                int o = 0;
                protocolVersion = ReadInt32(data, ref o);
                int fpLen = ReadInt32(data, ref o);
                if (fpLen < 0 || fpLen > 256 || o + fpLen > data.Length) return false;
                keyFingerprint = Encoding.UTF8.GetString(data, o, fpLen); o += fpLen;
                sessionNonce = new byte[data.Length - o];
                if (sessionNonce.Length > 0) Buffer.BlockCopy(data, o, sessionNonce, 0, sessionNonce.Length);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Payload for the plain-text notices (<see cref="ReloadPendingMsg"/>, <see cref="ReloadFailedMsg"/>).</summary>
        public static byte[] EncodeText(string text) => Encoding.UTF8.GetBytes(text ?? "");

        public static string DecodeText(byte[] data) =>
            data == null || data.Length == 0 ? "" : Encoding.UTF8.GetString(data);

        /// <summary>Result wire format: [ok:1 byte][utf8 message…].</summary>
        public static byte[] EncodeResult(bool ok, string message)
        {
            var msgBytes = Encoding.UTF8.GetBytes(message ?? "");
            var buf = new byte[1 + msgBytes.Length];
            buf[0] = (byte)(ok ? 1 : 0);
            Buffer.BlockCopy(msgBytes, 0, buf, 1, msgBytes.Length);
            return buf;
        }

        public static bool TryDecodeResult(byte[] data, out bool ok, out string message)
        {
            ok = false; message = null;
            if (data == null || data.Length < 1) return false;
            ok = data[0] != 0;
            message = data.Length > 1 ? Encoding.UTF8.GetString(data, 1, data.Length - 1) : "";
            return true;
        }

        private static int WriteInt32(byte[] buf, int off, int v)
        {
            buf[off]     = (byte)v;
            buf[off + 1] = (byte)(v >> 8);
            buf[off + 2] = (byte)(v >> 16);
            buf[off + 3] = (byte)(v >> 24);
            return off + 4;
        }

        private static int WriteBytes(byte[] buf, int off, byte[] src)
        {
            if (src.Length > 0) Buffer.BlockCopy(src, 0, buf, off, src.Length);
            return off + src.Length;
        }

        private static int ReadInt32(byte[] buf, ref int off)
        {
            int v = buf[off] | (buf[off + 1] << 8) | (buf[off + 2] << 16) | (buf[off + 3] << 24);
            off += 4;
            return v;
        }
    }
}
