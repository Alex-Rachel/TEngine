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
#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.

namespace LightProto
{
    /// <summary>
    /// Encodes and writes protocol message fields.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class is generally used by generated code to write appropriate
    /// primitives to the stream. It effectively encapsulates the lowest
    /// levels of protocol buffer format. Unlike some other implementations,
    /// this does not include combined "write tag and value" methods. Generated
    /// code knows the exact byte representations of the tags they're going to write,
    /// so there's no need to re-encode them each time. Manually-written code calling
    /// this class should just call one of the <c>WriteTag</c> overloads before each value.
    /// </para>
    /// <para>
    /// Repeated fields and map fields are not handled by this class; use <c>RepeatedField&lt;T&gt;</c>
    /// and <c>MapField&lt;TKey, TValue&gt;</c> to serialize such fields.
    /// </para>
    /// </remarks>
    [SecuritySafeCritical]
    public sealed partial class CodedOutputStream : IDisposable
    {
        /// <summary>
        /// The buffer size used by CreateInstance(Stream).
        /// </summary>
        static readonly int DefaultBufferSize = 4096;

        private readonly bool leaveOpen;
        private readonly byte[] buffer;
        private WriterInternalState state;

        private readonly Stream? output;

        #region Construction
        /// <summary>
        /// Creates a new CodedOutputStream that writes directly to the given
        /// byte array. If more bytes are written than fit in the array,
        /// OutOfSpaceException will be thrown.
        /// </summary>
        public CodedOutputStream(byte[] flatArray)
            : this(flatArray, 0, flatArray.Length) { }

        /// <summary>
        /// Creates a new CodedOutputStream that writes directly to the given
        /// byte array slice. If more bytes are written than fit in the array,
        /// OutOfSpaceException will be thrown.
        /// </summary>
        private CodedOutputStream(byte[] buffer, int offset, int length)
        {
            this.output = null;
            this.buffer = ProtoPreconditions.CheckNotNull(buffer, nameof(buffer));
            this.state.position = offset;
            this.state.limit = offset + length;
            WriteBufferHelper.Initialize(this, out this.state.writeBufferHelper);
            leaveOpen = true; // Simple way of avoiding trying to dispose of a null reference
        }

        private CodedOutputStream(Stream output, byte[] buffer, bool leaveOpen)
        {
            this.output = ProtoPreconditions.CheckNotNull(output, nameof(output));
            this.buffer = buffer;
            this.state.position = 0;
            this.state.limit = buffer.Length;
            WriteBufferHelper.Initialize(this, out this.state.writeBufferHelper);
            this.leaveOpen = leaveOpen;
        }

        /// <summary>
        /// Creates a new CodedOutputStream which write to the given stream.
        /// </summary>
        /// <param name="output">The stream to write to.</param>
        /// <param name="leaveOpen">If <c>true</c>, <paramref name="output"/> is left open when the returned <c>CodedOutputStream</c> is disposed;
        /// if <c>false</c>, the provided stream is disposed as well.</param>
        public CodedOutputStream(Stream output, bool leaveOpen)
            : this(output, DefaultBufferSize, leaveOpen) { }

        /// <summary>
        /// Creates a new CodedOutputStream which write to the given stream and uses
        /// the specified buffer size.
        /// </summary>
        /// <param name="output">The stream to write to.</param>
        /// <param name="bufferSize">The size of buffer to use internally.</param>
        /// <param name="leaveOpen">If <c>true</c>, <paramref name="output"/> is left open when the returned <c>CodedOutputStream</c> is disposed;
        /// if <c>false</c>, the provided stream is disposed as well.</param>
        public CodedOutputStream(Stream output, int bufferSize, bool leaveOpen)
            : this(output, new byte[bufferSize], leaveOpen) { }
        #endregion

        /// <summary>
        /// Indicates that a CodedOutputStream wrapping a flat byte array
        /// ran out of space.
        /// </summary>
        public sealed class OutOfSpaceException : IOException
        {
            internal OutOfSpaceException()
                : base("CodedOutputStream was writing to a flat byte array and ran out of space.")
            { }
        }

        /// <summary>
        /// Flushes any buffered data and optionally closes the underlying stream, if any.
        /// </summary>
        /// <remarks>
        /// <para>
        /// By default, any underlying stream is closed by this method. To configure this behaviour,
        /// use a constructor overload with a <c>leaveOpen</c> parameter. If this instance does not
        /// have an underlying stream, this method does nothing.
        /// </para>
        /// <para>
        /// For the sake of efficiency, calling this method does not prevent future write calls - but
        /// if a later write ends up writing to a stream which has been disposed, that is likely to
        /// fail. It is recommend that you not call any other methods after this.
        /// </para>
        /// </remarks>
        public void Dispose()
        {
            Flush();
            if (!leaveOpen)
            {
                output?.Dispose();
            }
        }

        /// <summary>
        /// Flushes any buffered data to the underlying stream (if there is one).
        /// </summary>
        internal void Flush()
        {
            var span = new Span<byte>(buffer);
            WriteBufferHelper.Flush(ref span, ref state);
        }

        internal byte[] InternalBuffer => buffer;

        internal Stream? InternalOutputStream => output;

        internal ref WriterInternalState InternalState => ref state;
    }
}
