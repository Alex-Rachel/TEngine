#region Copyright notice and license
// Protocol Buffers - Google's data interchange format
// Copyright 2008 Google Inc.  All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file or at
// https://developers.google.com/open-source/licenses/bsd
#endregion

using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Security;

namespace LightProto
{
    /// <summary>
    /// An opaque struct that represents the current parsing state and is passed along
    /// as the parsing proceeds.
    /// All the public methods are intended to be invoked only by the generated code,
    /// users should never invoke them directly.
    /// </summary>
    [SecuritySafeCritical]
    public ref struct ReaderContext
    {
        internal const int DefaultRecursionLimit = 100;
        internal const int DefaultSizeLimit = int.MaxValue;

        internal ReadOnlySpan<byte> buffer;
        internal ParserInternalState state;

        /// <summary>
        /// Initialize a <see cref="ReaderContext"/>, building all <see cref="ParserInternalState"/> from defaults and
        /// the given <paramref name="buffer"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Initialize(ReadOnlySpan<byte> buffer, out ReaderContext ctx)
        {
            ParserInternalState state = default;
            state.sizeLimit = DefaultSizeLimit;
            state.recursionLimit = DefaultRecursionLimit;
            state.currentLimit = int.MaxValue;
            state.bufferSize = buffer.Length;
            // Equivalent to Initialize(buffer, ref state, out ctx);
            ctx.buffer = buffer;
            ctx.state = state;
        }

        /// <summary>
        /// Creates a ParseContext instance from CodedInputStream.
        /// WARNING: internally this copies the CodedInputStream's state, so after done with the ParseContext,
        /// the CodedInputStream's state needs to be updated.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Initialize(CodedInputStream input, out ReaderContext ctx)
        {
            ctx.buffer = new ReadOnlySpan<byte>(input.InternalBuffer);
            // ideally we would use a reference to the original state, but that doesn't seem possible
            // so we just copy the struct that holds the state. We will need to later store the state back
            // into CodedInputStream if we want to keep it usable.
            ctx.state = input.InternalState;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Initialize(ReadOnlySequence<byte> input, out ReaderContext ctx)
        {
            Initialize(input, DefaultRecursionLimit, out ctx);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Initialize(
            ReadOnlySequence<byte> input,
            int recursionLimit,
            out ReaderContext ctx
        )
        {
            ctx.buffer = default;
            ctx.state = default;
            ctx.state.lastTag = 0;
            ctx.state.recursionDepth = 0;
            ctx.state.sizeLimit = DefaultSizeLimit;
            ctx.state.recursionLimit = recursionLimit;
            ctx.state.currentLimit = int.MaxValue;
            SegmentedBufferHelper.Initialize(
                input,
                out ctx.state.segmentedBufferHelper,
                out ctx.buffer
            );
            ctx.state.bufferPos = 0;
            ctx.state.bufferSize = ctx.buffer.Length;

            ctx.state.DiscardUnknownFields = false;
        }

        /// <summary>
        /// Reads a field tag, returning the tag of 0 for "end of input".
        /// </summary>
        /// <remarks>
        /// If this method returns 0, it doesn't necessarily mean the end of all
        /// the data in this CodedInputReader; it may be the end of the logical input
        /// for an embedded message, for example.
        /// </remarks>
        /// <returns>The next field tag, or 0 for end of input. (0 is never a valid tag.)</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadTag() => ParsingPrimitives.ParseTag(ref buffer, ref state);

        /// <summary>
        /// Reads a double field from the input.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double ReadDouble() => ParsingPrimitives.ParseDouble(ref buffer, ref state);

        /// <summary>
        /// Reads a float field from the input.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ReadFloat() => ParsingPrimitives.ParseFloat(ref buffer, ref state);

        /// <summary>
        /// Reads a uint64 field from the input.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong ReadUInt64() => ParsingPrimitives.ParseRawVarint64(ref buffer, ref state);

        /// <summary>
        /// Reads an int64 field from the input.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ReadInt64() => (long)ParsingPrimitives.ParseRawVarint64(ref buffer, ref state);

        /// <summary>
        /// Reads an int32 field from the input.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadInt32() => (int)ParsingPrimitives.ParseRawVarint32(ref buffer, ref state);

        /// <summary>
        /// Reads a fixed64 field from the input.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong ReadFixed64() =>
            ParsingPrimitives.ParseRawLittleEndian64(ref buffer, ref state);

        /// <summary>
        /// Reads a fixed32 field from the input.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadFixed32() =>
            ParsingPrimitives.ParseRawLittleEndian32(ref buffer, ref state);

        /// <summary>
        /// Reads a bool field from the input.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ReadBool() => ParsingPrimitives.ParseRawVarint64(ref buffer, ref state) != 0;

        /// <summary>
        /// Reads a string field from the input.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string ReadString() => ParsingPrimitives.ReadString(ref buffer, ref state);

        /// <summary>
        /// Reads a uint32 field value from the input.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadUInt32() => ParsingPrimitives.ParseRawVarint32(ref buffer, ref state);

        /// <summary>
        /// Reads a byte field value from the input.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte ReadByte() => (byte)ParsingPrimitives.ParseRawVarint32(ref buffer, ref state);

        /// <summary>
        /// Reads an enum field value from the input.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadEnum()
        {
            // Currently just a pass-through, but it's nice to separate it logically from WriteInt32.
            return (int)ParsingPrimitives.ParseRawVarint32(ref buffer, ref state);
        }

        /// <summary>
        /// Reads an sfixed32 field value from the input.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadSFixed32() =>
            (int)ParsingPrimitives.ParseRawLittleEndian32(ref buffer, ref state);

        /// <summary>
        /// Reads an sfixed64 field value from the input.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ReadSFixed64() =>
            (long)ParsingPrimitives.ParseRawLittleEndian64(ref buffer, ref state);

        /// <summary>
        /// Reads an sint32 field value from the input.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadSInt32() =>
            ParsingPrimitives.DecodeZigZag32(
                ParsingPrimitives.ParseRawVarint32(ref buffer, ref state)
            );

        /// <summary>
        /// Reads an sint64 field value from the input.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ReadSInt64() =>
            ParsingPrimitives.DecodeZigZag64(
                ParsingPrimitives.ParseRawVarint64(ref buffer, ref state)
            );

        /// <summary>
        /// Reads a length for length-delimited data.
        /// </summary>
        /// <remarks>
        /// This is internally just reading a varint, but this method exists
        /// to make the calling code clearer.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadLength() => (int)ParsingPrimitives.ParseRawVarint32(ref buffer, ref state);

        /// <summary>
        /// Skips a field with the last tag.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SkipLastField() => ParsingPrimitives.SkipLastField(ref buffer, ref state);
    }
}
