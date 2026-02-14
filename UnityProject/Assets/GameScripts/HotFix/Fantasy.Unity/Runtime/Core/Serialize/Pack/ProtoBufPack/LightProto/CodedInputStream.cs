#region Copyright notice and license
// Protocol Buffers - Google's data interchange format
// Copyright 2008 Google Inc.  All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file or at
// https://developers.google.com/open-source/licenses/bsd
#endregion

using System;
using System.IO;
using System.Security;

namespace LightProto
{
    /// <summary>
    /// Reads and decodes protocol message fields.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class is generally used by generated code to read appropriate
    /// primitives from the stream. It effectively encapsulates the lowest
    /// levels of protocol buffer format.
    /// </para>
    /// <para>
    /// Repeated fields and map fields are not handled by this class; use <see cref="RepeatedField{T}"/>
    /// and <see cref="MapField{TKey, TValue}"/> to serialize such fields.
    /// </para>
    /// </remarks>
    [SecuritySafeCritical]
    internal sealed class CodedInputStream : IDisposable
    {
        /// <summary>
        /// Whether to leave the underlying stream open when disposing of this stream.
        /// This is always true when there's no stream.
        /// </summary>
        private readonly bool leaveOpen;

        internal long leftSize;

        /// <summary>
        /// Buffer of data read from the stream or provided at construction time.
        /// </summary>
        private readonly byte[] buffer;

        /// <summary>
        /// The stream to read further input from, or null if the byte array buffer was provided
        /// directly on construction, with no further data available.
        /// </summary>
        private readonly Stream input;

        /// <summary>
        /// The parser state is kept separately so that other parse implementations can reuse the same
        /// parsing primitives.
        /// </summary>
        private ParserInternalState state;

        internal const int DefaultRecursionLimit = 100;
        internal const long DefaultSizeLimit = long.MaxValue;
        internal const int BufferSize = 4096;

        #region Construction

        /// <summary>
        /// Creates a new <see cref="CodedInputStream"/> reading data from the given stream.
        /// </summary>
        /// <param name="input">The stream to read from.</param>
        /// <param name="leaveOpen"><c>true</c> to leave <paramref name="input"/> open when the returned
        /// <c cref="CodedInputStream"/> is disposed; <c>false</c> to dispose of the given stream when the
        /// returned object is disposed.</param>
        /// <param name="maxSize"></param>
        internal CodedInputStream(Stream input, bool leaveOpen, long maxSize = long.MaxValue)
            : this(
                ProtoPreconditions.CheckNotNull(input, "input"),
                new byte[BufferSize],
                0,
                0,
                leaveOpen,
                maxSize
            ) { }

        /// <summary>
        /// Creates a new CodedInputStream reading data from the given
        /// stream and buffer, using the default limits.
        /// </summary>
        internal CodedInputStream(
            Stream input,
            byte[] buffer,
            int bufferPos,
            int bufferSize,
            bool leaveOpen,
            long maxSize
        )
        {
            this.input = input;
            this.buffer = buffer;
            this.state.bufferPos = bufferPos;
            this.state.bufferSize = bufferSize;
            this.state.sizeLimit = DefaultSizeLimit;
            this.state.recursionLimit = DefaultRecursionLimit;
            SegmentedBufferHelper.Initialize(this, out this.state.segmentedBufferHelper);
            this.leaveOpen = leaveOpen;
            this.state.currentLimit = DefaultSizeLimit;
            this.leftSize = maxSize;
        }

        #endregion

        internal byte[] InternalBuffer => buffer;

        internal Stream InternalInputStream => input;

        internal ref ParserInternalState InternalState => ref state;

        /// <summary>
        /// Disposes of this instance, potentially closing any underlying stream.
        /// </summary>
        /// <remarks>
        /// As there is no flushing to perform here, disposing of a <see cref="CodedInputStream"/> which
        /// was constructed with the <c>leaveOpen</c> option parameter set to <c>true</c> (or one which
        /// was constructed to read from a byte array) has no effect.
        /// </remarks>
        public void Dispose()
        {
            if (!leaveOpen)
            {
                input.Dispose();
            }
        }
    }
}
