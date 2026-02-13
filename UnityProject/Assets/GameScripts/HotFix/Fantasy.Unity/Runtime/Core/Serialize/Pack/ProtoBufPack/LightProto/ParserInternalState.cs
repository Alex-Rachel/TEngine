#region Copyright notice and license
// Protocol Buffers - Google's data interchange format
// Copyright 2008 Google Inc.  All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file or at
// https://developers.google.com/open-source/licenses/bsd
#endregion

namespace LightProto
{
    // warning: this is a mutable struct, so it needs to be only passed as a ref!
    internal struct ParserInternalState
    {
        // NOTE: the Span representing the current buffer is kept separate so that this doesn't have to be a ref struct and so it can
        // be included in CodedInputStream's internal state

        /// <summary>
        /// The position within the current buffer (i.e. the next byte to read)
        /// </summary>
        internal int bufferPos;

        /// <summary>
        /// Size of the current buffer
        /// </summary>
        internal int bufferSize;

        /// <summary>
        /// If we are currently inside a length-delimited block, this is the number of
        /// bytes in the buffer that are still available once we leave the delimited block.
        /// </summary>
        internal int bufferSizeAfterLimit;

        /// <summary>
        /// The absolute position of the end of the current length-delimited block (including totalBytesRetired)
        /// </summary>
        internal long currentLimit;

        /// <summary>
        /// The total number of consumed before the start of the current buffer. The
        /// total bytes read up to the current position can be computed as
        /// totalBytesRetired + bufferPos.
        /// </summary>
        internal long totalBytesRetired;

        internal int recursionDepth; // current recursion depth

        internal SegmentedBufferHelper segmentedBufferHelper;

        /// <summary>
        /// The last tag we read. 0 indicates we've read to the end of the stream
        /// (or haven't read anything yet).
        /// </summary>
        internal uint lastTag;

        /// <summary>
        /// The next tag, used to store the value read by PeekTag.
        /// </summary>
        internal uint nextTag;
        internal bool hasNextTag;

        // these fields are configuration, they should be readonly
        internal long sizeLimit;
        internal int recursionLimit;

        /// <summary>
        /// Internal-only property; when set to true, unknown fields will be discarded while parsing.
        /// </summary>
        internal bool DiscardUnknownFields { get; set; }
    }
}
