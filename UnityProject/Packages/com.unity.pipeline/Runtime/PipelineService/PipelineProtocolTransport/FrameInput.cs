using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Unity.Pipeline
{
    /// <summary>
    /// A command's In, fed from the client's Stdin frames: a read waits for the client's next bytes,
    /// and ends once the client ends its input or goes away.
    /// </summary>
    /// <remarks>
    /// Thread-safe. A read blocks its thread, so a command on the main thread that reads holds the
    /// Editor until the client sends; the session ends the input once the command is cancelled or
    /// ends, so a read never outlasts either.
    /// </remarks>
    sealed class FrameInput : TextReader
    {
        /// <summary>
        /// How much input it holds unread, in characters: 8M (16 MB), as much as
        /// <see cref="FrameOutput"/> holds for the client.
        /// </summary>
        public const int DefaultMaxBufferedChars = 8 * 1024 * 1024;

        readonly object m_Lock = new object();
        readonly Decoder m_Decoder = new UTF8Encoding(false).GetDecoder();
        readonly StringBuilder m_Buffer = new StringBuilder();
        readonly int m_MaxBufferedChars;
        bool m_Ended;

        public FrameInput(int maxBufferedChars = DefaultMaxBufferedChars)
        {
            m_MaxBufferedChars = maxBufferedChars;
        }

        /// <summary>Adds the body of a Stdin frame, which may split a character.</summary>
        /// <returns>
        /// False when the input left unread grew past the bound: the client sends more than the
        /// command reads, and the input ended.
        /// </returns>
        public bool Append(byte[] bytes)
        {
            lock (m_Lock)
            {
                if (m_Ended)
                    return true;
                var chars = new char[m_Decoder.GetCharCount(bytes, 0, bytes.Length, false)];
                m_Decoder.GetChars(bytes, 0, bytes.Length, chars, 0, false);
                if (m_Buffer.Length + chars.Length > m_MaxBufferedChars)
                {
                    m_Ended = true;
                    Monitor.PulseAll(m_Lock);
                    return false;
                }
                m_Buffer.Append(chars);
                Monitor.PulseAll(m_Lock);
                return true;
            }
        }

        /// <summary>Ends the input: reads take what is left, then end.</summary>
        public void End()
        {
            lock (m_Lock)
            {
                if (m_Ended)
                    return;
                var chars = new char[m_Decoder.GetCharCount(Array.Empty<byte>(), 0, 0, true)];
                m_Decoder.GetChars(Array.Empty<byte>(), 0, 0, chars, 0, true);
                m_Buffer.Append(chars);
                m_Ended = true;
                Monitor.PulseAll(m_Lock);
            }
        }

        public override int Peek()
        {
            lock (m_Lock)
                return WaitForInput() ? m_Buffer[0] : -1;
        }

        public override int Read()
        {
            lock (m_Lock)
            {
                if (!WaitForInput())
                    return -1;
                var value = m_Buffer[0];
                m_Buffer.Remove(0, 1);
                return value;
            }
        }

        public override int Read(char[] buffer, int index, int count)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (index < 0 || count < 0 || buffer.Length - index < count)
                throw new ArgumentOutOfRangeException(nameof(count));

            lock (m_Lock)
            {
                if (count == 0 || !WaitForInput())
                    return 0;
                var read = Math.Min(count, m_Buffer.Length);
                m_Buffer.CopyTo(0, buffer, index, read);
                m_Buffer.Remove(0, read);
                return read;
            }
        }

        /// <returns>False once the input ended and everything in it was read.</returns>
        bool WaitForInput()
        {
            while (m_Buffer.Length == 0 && !m_Ended)
                Monitor.Wait(m_Lock);
            return m_Buffer.Length > 0;
        }
    }
}
