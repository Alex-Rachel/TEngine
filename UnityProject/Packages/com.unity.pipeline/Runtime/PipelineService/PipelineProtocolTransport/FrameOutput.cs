using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FrameType = Unity.Pipeline.PipelineProtocol.FrameType;

namespace Unity.Pipeline
{
    /// <summary>
    /// What a command writes to its Out and Error, sent to the client as Stdout and Stderr frames
    /// in the order it was written, and its progress, sent as Progress frames. Writing only
    /// buffers, so a command never waits on its client; <see cref="SendAsync"/> sends what was
    /// buffered, from a loop of its own, the one writer of the stream.
    /// </summary>
    /// <remarks>
    /// The buffer is bounded, so a client that stays connected without reading can't grow it
    /// without limit: once it holds <c>maxBufferedChars</c>, further writes are dropped, and a line
    /// on Stderr marks where, until the sender takes what was buffered. A write is taken whole
    /// while the buffer is under the bound, so a command's result, written at once, always is.
    /// Thread-safe.
    /// </remarks>
    sealed class FrameOutput
    {
        sealed class Chunk
        {
            public FrameType Type;
            public readonly StringBuilder Text = new StringBuilder();
        }

        /// <summary>How many characters it buffers before dropping writes, by default: 16 MB of text.</summary>
        public const int DefaultMaxBufferedChars = 8 * 1024 * 1024;

        /// <summary>Written on Stderr where writes start being dropped.</summary>
        internal const string DroppedMarker = "Pipeline: Output dropped here: the client isn't reading it.\n";

        readonly object m_Lock = new object();
        readonly int m_MaxBufferedChars;
        readonly Queue<Chunk> m_Pending = new Queue<Chunk>();
        readonly SemaphoreSlim m_Available = new SemaphoreSlim(0);
        Chunk m_Last;

        /// <summary>The characters in <see cref="m_Pending"/>.</summary>
        int m_BufferedChars;

        /// <summary>Whether writes are dropped until the sender takes what was buffered.</summary>
        bool m_Dropping;

        /// <summary>The Progress body not sent yet; null when the client has the latest.</summary>
        byte[] m_Progress;

        bool m_Completed;

        /// <param name="maxBufferedChars">How many characters it buffers before dropping writes.</param>
        public FrameOutput(int maxBufferedChars = DefaultMaxBufferedChars)
        {
            if (maxBufferedChars <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxBufferedChars), maxBufferedChars, "Must be positive.");
            m_MaxBufferedChars = maxBufferedChars;
            Out = new FrameWriter(this, FrameType.Stdout);
            Error = new FrameWriter(this, FrameType.Stderr);
        }

        /// <summary>Takes what the command produces, sent as Stdout frames.</summary>
        public TextWriter Out { get; }

        /// <summary>Takes why the command failed, sent as Stderr frames.</summary>
        public TextWriter Error { get; }

        /// <summary>
        /// Sends <paramref name="body"/> as a Progress frame, after what was written before it. Only
        /// the latest is kept: one not sent yet is replaced.
        /// </summary>
        public void ReportProgress(byte[] body)
        {
            lock (m_Lock)
            {
                if (m_Completed)
                    return;

                var wasEmpty = IsEmpty;
                m_Progress = body;
                if (!wasEmpty)
                    return;
            }
            m_Available.Release();
        }

        /// <summary>Whether nothing waits to be sent. Read under the lock.</summary>
        bool IsEmpty => m_Pending.Count == 0 && m_Progress == null;

        /// <summary>
        /// Ends the output: <see cref="SendAsync"/> sends what is buffered and returns, and anything
        /// written after is dropped.
        /// </summary>
        public void Complete()
        {
            lock (m_Lock)
            {
                if (m_Completed)
                    return;
                m_Completed = true;
            }
            m_Available.Release();
        }

        /// <summary>
        /// Sends what is written, as it is written, until <see cref="Complete"/> and everything
        /// before it was sent. Call once.
        /// </summary>
        /// <exception cref="IOException">The client went away.</exception>
        public async Task SendAsync(Stream stream, CancellationToken cancellation)
        {
            // Stateful, so a surrogate pair split across two writes still encodes as one character.
            var stdout = new UTF8Encoding(false).GetEncoder();
            var stderr = new UTF8Encoding(false).GetEncoder();
            var chunks = new List<Chunk>();
            var bytes = Array.Empty<byte>();

            while (true)
            {
                await m_Available.WaitAsync(cancellation);

                bool completed;
                byte[] progress;
                lock (m_Lock)
                {
                    chunks.AddRange(m_Pending);
                    m_Pending.Clear();
                    m_Last = null;
                    m_BufferedChars = 0;
                    m_Dropping = false;
                    progress = m_Progress;
                    m_Progress = null;
                    completed = m_Completed;
                }

                foreach (var chunk in chunks)
                {
                    var encoder = chunk.Type == FrameType.Stdout ? stdout : stderr;
                    var chars = chunk.Text.ToString().ToCharArray();
                    var size = encoder.GetByteCount(chars, 0, chars.Length, completed);
                    if (bytes.Length < size)
                        bytes = new byte[size];
                    encoder.GetBytes(chars, 0, chars.Length, bytes, 0, completed);

                    for (var offset = 0; offset < size; offset += PipelineProtocol.MaxBodySize)
                    {
                        var count = Math.Min(PipelineProtocol.MaxBodySize, size - offset);
                        await PipelineProtocol.WriteFrameAsync(stream, chunk.Type, bytes, offset, count, cancellation);
                    }
                }
                chunks.Clear();

                // Ends with Exit, so a client clears it then; one buffered past the end is stale.
                if (progress != null && !completed)
                    await PipelineProtocol.WriteFrameAsync(stream, FrameType.Progress, progress, cancellation);

                if (completed)
                    return;
            }
        }

        void Append(FrameType type, string text, char[] buffer, int index, int count)
        {
            lock (m_Lock)
            {
                if (m_Completed || m_Dropping)
                    return;

                var wasEmpty = IsEmpty;
                if (m_BufferedChars >= m_MaxBufferedChars)
                {
                    // Marked once, on Stderr, past the bound: it has to reach the client to mean anything.
                    m_Dropping = true;
                    Buffer(FrameType.Stderr).Append(DroppedMarker);
                    return;
                }

                var chunk = Buffer(type);
                if (text != null)
                    chunk.Append(text);
                else
                    chunk.Append(buffer, index, count);
                m_BufferedChars += text?.Length ?? count;

                if (!wasEmpty)
                    return;
            }
            m_Available.Release();
        }

        /// <summary>The chunk to append <paramref name="type"/>'s text to. Called under the lock.</summary>
        StringBuilder Buffer(FrameType type)
        {
            if (m_Last == null || m_Last.Type != type)
            {
                m_Last = new Chunk { Type = type };
                m_Pending.Enqueue(m_Last);
            }
            return m_Last.Text;
        }

        sealed class FrameWriter : TextWriter
        {
            readonly FrameOutput m_Output;
            readonly FrameType m_Type;

            public FrameWriter(FrameOutput output, FrameType type)
            {
                m_Output = output;
                m_Type = type;
            }

            public override Encoding Encoding => Encoding.UTF8;

            public override void Write(char value) => m_Output.Append(m_Type, null, new[] { value }, 0, 1);

            public override void Write(char[] buffer, int index, int count)
            {
                if (count > 0)
                    m_Output.Append(m_Type, null, buffer, index, count);
            }

            public override void Write(string value)
            {
                if (!string.IsNullOrEmpty(value))
                    m_Output.Append(m_Type, value, null, 0, 0);
            }
        }
    }
}
