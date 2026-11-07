using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline
{
    /// <summary>
    /// The Pipeline protocol: how the unity CLI runs a command in an Editor. The engine's native
    /// endpoint and this package speak it alike, so the CLI has one client for both.
    /// </summary>
    /// <remarks>
    /// <para>A connection runs one command:</para>
    /// <list type="number">
    /// <item>Signature, each way: "PPLN" and a version byte. The client sends first. The server
    /// answers with its own, and closes when it doesn't speak the client's version; it closes at once
    /// on anything else, such as HTTP.</item>
    /// <item>Frames, each way: a little-endian uint32 byte count, then that many bytes: a
    /// <see cref="FrameType"/> byte and the body. The client's first frame is Auth, then an optional
    /// Metadata, then Request. Then the client sends Stdin and Signal frames and the server Stdout,
    /// Stderr and Progress frames, until the server sends Exit and closes. The client closes early to abandon the
    /// command.</item>
    /// </list>
    /// <para>Specified in the repository's <c>docs/pipeline-protocol.md</c>, and mirrored by the engine's
    /// <c>Modules/PipelineEditor/Protocol/PipelineProtocol.h</c>; change all three together.</para>
    /// </remarks>
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    static class PipelineProtocol
    {
        public const byte Version = 1;
        public const int SignatureSize = 5;

        /// <summary>The byte count and the type.</summary>
        public const int FrameHeaderSize = 5;

        /// <summary>
        /// Frames carry command lines and console text; anything larger is a peer that doesn't speak
        /// the protocol.
        /// </summary>
        public const int MaxFrameSize = 1024 * 1024;

        public const int MaxBodySize = MaxFrameSize - 1;

        /// <summary>
        /// Checked from the Auth frame's header, so a peer without the token can't make the server
        /// read a large body.
        /// </summary>
        public const int MaxTokenSize = 256;

        /// <summary>Checked from the Metadata frame's header, like <see cref="MaxTokenSize"/>.</summary>
        public const int MaxMetadataSize = 4096;

        public enum FrameType : byte
        {
            // Client to server.
            /// <summary>The token, at most <see cref="MaxTokenSize"/> bytes.</summary>
            Auth = 0x01,
            /// <summary>The client and the request, as key/value pairs; at most <see cref="MaxMetadataSize"/> bytes.</summary>
            Metadata = 0x02,
            /// <summary>The command line: each argument, UTF-8, followed by NUL.</summary>
            Request = 0x03,
            /// <summary>Bytes for the command's standard input; an empty body ends it.</summary>
            Stdin = 0x04,
            /// <summary>One <see cref="Signal"/> byte.</summary>
            Signal = 0x05,

            // Server to client.
            Stdout = 0x11,
            Stderr = 0x12,
            /// <summary>The command's exit code, a little-endian int32.</summary>
            Exit = 0x13,
            /// <summary>The command's whole progress state, as key/value pairs; an empty body clears it.</summary>
            Progress = 0x14
        }

        public enum Signal : byte
        {
            /// <summary>Ctrl+C.</summary>
            Interrupt = 1,
            Terminate = 2
        }

        /// <summary>One frame as read: its type, which may be one this version doesn't know, and its body.</summary>
        public readonly struct Frame
        {
            public Frame(FrameType type, byte[] body)
            {
                Type = type;
                Body = body;
            }

            public FrameType Type { get; }
            public byte[] Body { get; }
        }

        static readonly byte[] s_Magic = { (byte)'P', (byte)'P', (byte)'L', (byte)'N' };

        static readonly Encoding s_StrictUtf8 = new UTF8Encoding(false, true);

        /// <summary>This side's signature: the magic and <see cref="Version"/>.</summary>
        public static byte[] CreateSignature()
        {
            var signature = new byte[SignatureSize];
            Buffer.BlockCopy(s_Magic, 0, signature, 0, s_Magic.Length);
            signature[s_Magic.Length] = Version;
            return signature;
        }

        /// <param name="version">The version the peer speaks, which may not be <see cref="Version"/>.</param>
        /// <returns>False when the bytes are not a signature.</returns>
        public static bool TryParseSignature(byte[] signature, out byte version)
        {
            version = 0;
            if (signature == null || signature.Length != SignatureSize)
                return false;
            for (var i = 0; i < s_Magic.Length; i++)
            {
                if (signature[i] != s_Magic[i])
                    return false;
            }

            version = signature[s_Magic.Length];
            return true;
        }

        /// <summary>Writes the header of a frame with a body of <paramref name="bodySize"/> bytes.</summary>
        public static void WriteFrameHeader(byte[] header, FrameType type, int bodySize)
        {
            if (bodySize < 0 || bodySize > MaxBodySize)
                throw new ArgumentOutOfRangeException(nameof(bodySize));
            WriteUInt32(header, 0, (uint)bodySize + 1);
            header[4] = (byte)type;
        }

        /// <summary>
        /// Reads a frame's first <see cref="FrameHeaderSize"/> bytes: its type and the size of the
        /// body that follows.
        /// </summary>
        /// <returns>False when the frame is empty or larger than <see cref="MaxFrameSize"/>.</returns>
        public static bool TryParseFrameHeader(byte[] header, out FrameType type, out int bodySize)
        {
            type = default;
            bodySize = 0;
            if (header == null || header.Length != FrameHeaderSize)
                return false;
            var frameSize = ReadUInt32(header, 0);
            if (frameSize == 0 || frameSize > MaxFrameSize)
                return false;
            type = (FrameType)header[4];
            bodySize = (int)frameSize - 1;
            return true;
        }

        public static byte[] EncodeRequest(IReadOnlyList<string> arguments)
        {
            using var body = new MemoryStream();
            foreach (var argument in arguments)
            {
                var bytes = s_StrictUtf8.GetBytes(argument);
                body.Write(bytes, 0, bytes.Length);
                body.WriteByte(0);
            }
            return body.ToArray();
        }

        /// <returns>False when the last argument has no NUL after it, or one isn't UTF-8.</returns>
        public static bool TryDecodeRequest(byte[] body, out List<string> arguments)
        {
            arguments = new List<string>();
            var start = 0;
            try
            {
                for (var i = 0; i < body.Length; i++)
                {
                    if (body[i] != 0)
                        continue;
                    arguments.Add(s_StrictUtf8.GetString(body, start, i - start));
                    start = i + 1;
                }
            }
            catch (DecoderFallbackException)
            {
                return false;
            }

            return start == body.Length;
        }

        public static byte[] EncodeExitCode(int exitCode)
        {
            var body = new byte[4];
            WriteUInt32(body, 0, unchecked((uint)exitCode));
            return body;
        }

        public static bool TryDecodeExitCode(byte[] body, out int exitCode)
        {
            exitCode = 0;
            if (body == null || body.Length != 4)
                return false;
            exitCode = unchecked((int)ReadUInt32(body, 0));
            return true;
        }

        /// <summary>
        /// Encodes a Metadata or Progress body: each key, NUL, its value, NUL.
        /// </summary>
        /// <exception cref="ArgumentException">A key isn't valid, or a value holds NUL.</exception>
        public static byte[] EncodeKeyValues(IEnumerable<KeyValuePair<string, string>> pairs)
        {
            using var body = new MemoryStream();
            foreach (var pair in pairs)
            {
                if (!IsValidKey(pair.Key))
                    throw new ArgumentException($"'{pair.Key}' is not a valid key.", nameof(pairs));
                var value = pair.Value ?? string.Empty;
                if (value.IndexOf('\0') >= 0)
                    throw new ArgumentException($"The value of '{pair.Key}' holds NUL.", nameof(pairs));

                var key = Encoding.ASCII.GetBytes(pair.Key);
                body.Write(key, 0, key.Length);
                body.WriteByte(0);
                var bytes = s_StrictUtf8.GetBytes(value);
                body.Write(bytes, 0, bytes.Length);
                body.WriteByte(0);
            }
            return body.ToArray();
        }

        /// <summary>Decodes a Metadata or Progress body.</summary>
        /// <returns>
        /// False when the body doesn't end with NUL, holds an odd number of strings, has an invalid or
        /// repeated key, or a value that isn't UTF-8.
        /// </returns>
        public static bool TryDecodeKeyValues(byte[] body, out Dictionary<string, string> pairs)
        {
            pairs = new Dictionary<string, string>(StringComparer.Ordinal);
            string key = null;
            var start = 0;
            try
            {
                for (var i = 0; i < body.Length; i++)
                {
                    if (body[i] != 0)
                        continue;
                    var text = s_StrictUtf8.GetString(body, start, i - start);
                    start = i + 1;
                    if (key == null)
                    {
                        if (!IsValidKey(text) || pairs.ContainsKey(text))
                            return false;
                        key = text;
                    }
                    else
                    {
                        pairs.Add(key, text);
                        key = null;
                    }
                }
            }
            catch (DecoderFallbackException)
            {
                return false;
            }

            return start == body.Length && key == null;
        }

        /// <summary>One or more ASCII lowercase letters, digits, <c>.</c> or <c>-</c>.</summary>
        static bool IsValidKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;
            foreach (var c in key)
            {
                if (!(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '.' || c == '-'))
                    return false;
            }
            return true;
        }

        /// <summary>The Auth frame's token.</summary>
        /// <returns>False when it isn't UTF-8.</returns>
        public static bool TryDecodeToken(byte[] body, out string token)
        {
            try
            {
                token = s_StrictUtf8.GetString(body);
                return true;
            }
            catch (DecoderFallbackException)
            {
                token = null;
                return false;
            }
        }

        /// <summary>Reads the peer's signature.</summary>
        /// <returns>Null when the peer closed before sending all of it.</returns>
        public static async Task<byte[]> ReadSignatureAsync(Stream stream, CancellationToken cancellation)
        {
            var signature = new byte[SignatureSize];
            return await ReadExactlyAsync(stream, signature, cancellation) ? signature : null;
        }

        /// <summary>Reads one frame whose body is at most <paramref name="maxBodySize"/> bytes.</summary>
        /// <returns>The frame, or null when the peer closed before its first byte.</returns>
        /// <exception cref="InvalidDataException">
        /// The peer closed mid-frame, or sent a frame that is empty or has a larger body.
        /// </exception>
        public static Task<Frame?> ReadFrameAsync(Stream stream, int maxBodySize, CancellationToken cancellation)
        {
            return ReadFrameAsync(stream, _ => maxBodySize, cancellation);
        }

        /// <summary>
        /// Reads one frame whose body is at most <paramref name="maxBodySize"/> bytes for its type,
        /// checked from the header before the body is read.
        /// </summary>
        /// <returns>The frame, or null when the peer closed before its first byte.</returns>
        /// <exception cref="InvalidDataException">
        /// The peer closed mid-frame, or sent a frame that is empty or has a larger body.
        /// </exception>
        public static async Task<Frame?> ReadFrameAsync(Stream stream, Func<FrameType, int> maxBodySize,
            CancellationToken cancellation)
        {
            var header = await ReadFrameHeaderAsync(stream, cancellation);
            if (header == null)
                return null;
            var (type, bodySize) = header.Value;
            if (bodySize > MaxBodySize || bodySize > maxBodySize(type))
                throw new InvalidDataException("The peer sent a frame that is too large.");
            return new Frame(type, await ReadFrameBodyAsync(stream, (int)bodySize, cancellation));
        }

        /// <summary>
        /// Reads a frame's header only: its type and the size of the body that follows, which may be
        /// larger than <see cref="MaxBodySize"/>. The caller reads the body, or skips it.
        /// </summary>
        /// <returns>Null when the peer closed before its first byte.</returns>
        /// <exception cref="InvalidDataException">The peer closed mid-header, or sent an empty frame.</exception>
        public static async Task<(FrameType Type, long BodySize)?> ReadFrameHeaderAsync(Stream stream,
            CancellationToken cancellation)
        {
            var header = new byte[FrameHeaderSize];
            var read = await ReadAtMostAsync(stream, header, cancellation);
            if (read == 0)
                return null;
            if (read < header.Length)
                throw new InvalidDataException("The peer closed mid-frame.");
            var frameSize = ReadUInt32(header, 0);
            if (frameSize == 0)
                throw new InvalidDataException("The peer sent an empty frame.");
            return ((FrameType)header[4], (long)frameSize - 1);
        }

        /// <summary>Reads the body of a frame whose header was read.</summary>
        /// <exception cref="InvalidDataException">The peer closed mid-frame.</exception>
        public static async Task<byte[]> ReadFrameBodyAsync(Stream stream, int bodySize, CancellationToken cancellation)
        {
            var body = new byte[bodySize];
            if (!await ReadExactlyAsync(stream, body, cancellation))
                throw new InvalidDataException("The peer closed mid-frame.");
            return body;
        }

        /// <summary>Reads and discards the body of a frame whose header was read.</summary>
        /// <exception cref="InvalidDataException">The peer closed mid-frame.</exception>
        public static async Task SkipFrameBodyAsync(Stream stream, long bodySize, CancellationToken cancellation)
        {
            var buffer = new byte[(int)Math.Min(bodySize, 64 * 1024)];
            while (bodySize > 0)
            {
                var read = await stream.ReadAsync(buffer, 0, (int)Math.Min(bodySize, buffer.Length), cancellation);
                if (read == 0)
                    throw new InvalidDataException("The peer closed mid-frame.");
                bodySize -= read;
            }
        }

        /// <summary>Writes one frame. Not thread-safe: one writer per stream at a time.</summary>
        public static async Task WriteFrameAsync(Stream stream, FrameType type, byte[] body, int offset, int count,
            CancellationToken cancellation)
        {
            var header = new byte[FrameHeaderSize];
            WriteFrameHeader(header, type, count);
            await stream.WriteAsync(header, 0, header.Length, cancellation);
            if (count > 0)
                await stream.WriteAsync(body, offset, count, cancellation);
        }

        public static Task WriteFrameAsync(Stream stream, FrameType type, byte[] body, CancellationToken cancellation)
        {
            return WriteFrameAsync(stream, type, body, 0, body.Length, cancellation);
        }

        static async Task<bool> ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellation)
        {
            return await ReadAtMostAsync(stream, buffer, cancellation) == buffer.Length;
        }

        /// <returns>How many bytes were read: all of them unless the peer closed first.</returns>
        static async Task<int> ReadAtMostAsync(Stream stream, byte[] buffer, CancellationToken cancellation)
        {
            var offset = 0;
            while (offset < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer, offset, buffer.Length - offset, cancellation);
                if (read == 0)
                    break;
                offset += read;
            }
            return offset;
        }

        static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        static uint ReadUInt32(byte[] buffer, int offset)
        {
            return buffer[offset]
                | ((uint)buffer[offset + 1] << 8)
                | ((uint)buffer[offset + 2] << 16)
                | ((uint)buffer[offset + 3] << 24);
        }
    }
}
