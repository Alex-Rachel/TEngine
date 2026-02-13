using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using LightProto.Parser;
#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.

namespace LightProto
{
    public static partial class Serializer
    {
        public static IEnumerable<T> DeserializeItems<T>(
            Stream source,
            PrefixStyle style,
            IProtoReader<T> reader
        )
        {
            return DeserializeItems(source, style, 0, reader);
        }

        public static IEnumerable<T> DeserializeItems<T>(
            Stream source,
            PrefixStyle style,
            int fieldNumber,
            IProtoReader<T> reader
        )
        {
            while (true)
            {
                var result = DeserializeWithLengthPrefixInternal(
                    source,
                    style,
                    fieldNumber,
                    reader,
                    out var instance
                );
                switch (result)
                {
                    case DeserializeWithLengthPrefixResult.NoMoreData:
                    case DeserializeWithLengthPrefixResult.PrefixStyleIsNone:
                        yield break;
                    case DeserializeWithLengthPrefixResult.Success:
                        yield return instance;
                        break;
                    case DeserializeWithLengthPrefixResult.FieldNumberIsMismatched:
                        //skip
                        break;
                    default:
                        throw new InvalidOperationException("Unreachable code");
                }
            }
        }

        public static T DeserializeWithLengthPrefix<T>(
            Stream source,
            PrefixStyle style,
            IProtoReader<T> reader
        )
        {
            return DeserializeWithLengthPrefix(source, style, 0, reader);
        }

        internal enum DeserializeWithLengthPrefixResult
        {
            Success,
            PrefixStyleIsNone,
            FieldNumberIsMismatched,
            NoMoreData,
        }

        static DeserializeWithLengthPrefixResult DeserializeWithLengthPrefixInternal<T>(
            Stream source,
            PrefixStyle style,
            int fieldNumber,
            IProtoReader<T> reader,
            out T result
        )
        {
            if (style is not PrefixStyle.None)
            {
                if (reader.IsMessage == false)
                {
                    reader = MessageWrapper<T>.ProtoReader.From(reader);
                }

                int length;
                if (style is PrefixStyle.Base128)
                {
                    bool fieldNumberIsMatched = true;
                    if (fieldNumber > 0)
                    {
                        //write tag
                        var tag = ReadVarintFromStream(source);
                        if (tag < 0)
                        {
                            //at end;
                            result = default!;
                            return DeserializeWithLengthPrefixResult.NoMoreData;
                        }

                        if (WireFormat.GetTagFieldNumber((uint)tag) != fieldNumber)
                        {
                            fieldNumberIsMatched = false;
                        }
                    }

                    length = ReadVarintFromStream(source);
                    if (length < 0)
                    {
                        // at end
                        result = default!;
                        return DeserializeWithLengthPrefixResult.NoMoreData;
                    }

                    if (fieldNumberIsMatched == false)
                    {
                        //skip the message
                        int left = length;
                        byte[]? tempBuffer = null;
                        try
                        {
                            // Use a pooled buffer to avoid large allocations and improve performance.
                            tempBuffer = ArrayPool<byte>.Shared.Rent(Math.Min(left, 8192));
                            while (left > 0)
                            {
                                int toRead = Math.Min(left, tempBuffer.Length);
                                var read = source.Read(tempBuffer, 0, toRead);
                                if (read <= 0)
                                {
                                    // End of stream reached prematurely.
                                    throw InvalidProtocolBufferException.TruncatedMessage();
                                }

                                left -= read;
                            }
                        }
                        finally
                        {
                            if (tempBuffer != null)
                            {
                                ArrayPool<byte>.Shared.Return(tempBuffer);
                            }
                        }

                        result = default!;
                        return DeserializeWithLengthPrefixResult.FieldNumberIsMismatched;
                    }
                }
                else if (style is PrefixStyle.Fixed32)
                {
                    if (TryReadFixed32FromStream(source, out var UIntLength) == false)
                    {
                        // at end
                        result = default!;
                        return DeserializeWithLengthPrefixResult.NoMoreData;
                    }

                    length = (int)UIntLength;
                }
                else if (style is PrefixStyle.Fixed32BigEndian)
                {
                    if (TryReadFixed32BigEndianFromStream(source, out var UIntLength) == false)
                    {
                        // at end
                        result = default!;
                        return DeserializeWithLengthPrefixResult.NoMoreData;
                    }

                    length = (int)UIntLength;
                }
                else
                {
                    throw new ArgumentOutOfRangeException(nameof(style));
                }

                using var codedStream = new CodedInputStream(source, leaveOpen: true, maxSize: length);
                ReaderContext.Initialize(codedStream, out var ctx);
                result = reader.ParseFrom(ref ctx);
                return DeserializeWithLengthPrefixResult.Success;
            }

            result = Deserialize(source, reader);
            return DeserializeWithLengthPrefixResult.PrefixStyleIsNone;
        }

        static int ReadVarintFromStream(Stream source)
        {
            int result = 0;
            int shift = 0;
            for (int i = 0; i < 5; i++)
            {
                int b = source.ReadByte();
                if (b == -1)
                {
                    if (i == 0)
                    {
                        return -1; // Clean end-of-stream at the start of a new item.
                    }

                    throw InvalidProtocolBufferException.TruncatedMessage();
                }

                result |= (b & 0x7f) << shift;
                if ((b & 0x80) == 0)
                {
                    return result;
                }

                shift += 7;
            }

            // If we get here, the 5th byte had the MSB set, which is a malformed varint.
            throw InvalidProtocolBufferException.MalformedVarint();
        }

        static bool TryReadFixed32FromStream(Stream source, out uint value)
        {
            Span<byte> bytes = stackalloc byte[4];
            if (!TryRead4BytesFromStream(source, bytes))
            {
                value = 0;
                return false;
            }

            value = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
            return true;
        }

        static bool TryReadFixed32BigEndianFromStream(Stream source, out uint value)
        {
            Span<byte> bytes = stackalloc byte[4];
            if (!TryRead4BytesFromStream(source, bytes))
            {
                value = 0;
                return false;
            }

            value = BinaryPrimitives.ReadUInt32BigEndian(bytes);
            return true;
        }

        private static bool TryRead4BytesFromStream(Stream source, Span<byte> bytes)
        {
#if NET7_0_OR_GREATER
        try
        {
            source.ReadExactly(bytes);
            return true;
        }
        catch (EndOfStreamException)
        {
            return false;
        }
#else
            byte[] buffer = new byte[4];
            int total = 0;
            while (total < 4)
            {
                int read = source.Read(buffer, total, 4 - total);
                if (read <= 0)
                {
                    if (total == 0)
                    {
                        return false;
                    }

                    throw InvalidProtocolBufferException.TruncatedMessage();
                }

                total += read;
            }

            buffer.CopyTo(bytes);
            return true;
#endif
        }

        public static T DeserializeWithLengthPrefix<T>(
            Stream source,
            PrefixStyle style,
            int fieldNumber,
            IProtoReader<T> reader
        )
        {
            _ = DeserializeWithLengthPrefixInternal(
                source,
                style,
                fieldNumber,
                reader,
                out var instance
            );
            return instance;
        }

        public static void SerializeWithLengthPrefix<T>(
            Stream destination,
            T instance,
            PrefixStyle style,
            int fieldNumber,
            IProtoWriter<T> writer
        )
        {
            using var codedOutputStream = new CodedOutputStream(destination, leaveOpen: true);
            WriterContext.Initialize(codedOutputStream, out var ctx);
            if (style != PrefixStyle.None)
            {
                if (writer.IsMessage == false && writer is not ICollectionWriter)
                {
                    writer = MessageWrapper<T>.ProtoWriter.From(writer);
                }

                var length = writer.CalculateSize(instance);
                if (style is PrefixStyle.Base128)
                {
                    if (fieldNumber > 0)
                    {
                        //write tag
                        ctx.WriteTag(
                            WireFormat.MakeTag(fieldNumber, WireFormat.WireType.LengthDelimited)
                        );
                    }

                    ctx.WriteInt32(length);
                }
                else if (style is PrefixStyle.Fixed32)
                {
                    ctx.WriteFixed32((uint)length);
                }
                else if (style is PrefixStyle.Fixed32BigEndian)
                {
                    ctx.WriteFixedBigEndian32((uint)length);
                }
                else
                {
                    throw new ArgumentOutOfRangeException(nameof(style));
                }

                writer.WriteTo(ref ctx, instance);
                ctx.Flush();
                return;
            }

            Serialize(destination, instance, writer);
        }

        public static void SerializeWithLengthPrefix<T>(
            Stream destination,
            T instance,
            PrefixStyle style,
            IProtoWriter<T> writer
        )
        {
            SerializeWithLengthPrefix(destination, instance, style, 0, writer);
        }

#if NET7_0_OR_GREATER
    public static IEnumerable<T> DeserializeItems<T>(
        Stream source,
        PrefixStyle style,
        int fieldNumber
    )
        where T : IProtoParser<T>
    {
        return DeserializeItems(source, style, fieldNumber, T.ProtoReader);
    }

    public static IEnumerable<T> DeserializeItems<T>(Stream source, PrefixStyle style)
        where T : IProtoParser<T>
    {
        return DeserializeItems<T>(source, style, 0);
    }

    public static T DeserializeWithLengthPrefix<T>(Stream source, PrefixStyle style)
        where T : IProtoParser<T>
    {
        return DeserializeWithLengthPrefix(source, style, T.ProtoReader);
    }

    public static T DeserializeWithLengthPrefix<T>(
        Stream source,
        PrefixStyle style,
        int fieldNumber
    )
        where T : IProtoParser<T>
    {
        return DeserializeWithLengthPrefix(source, style, fieldNumber, T.ProtoReader);
    }

    public static void SerializeWithLengthPrefix<T>(
        Stream destination,
        T instance,
        PrefixStyle style,
        int fieldNumber
    )
        where T : IProtoParser<T>
    {
        SerializeWithLengthPrefix(destination, instance, style, fieldNumber, T.ProtoWriter);
    }

    public static void SerializeWithLengthPrefix<T>(
        Stream destination,
        T instance,
        PrefixStyle style
    )
        where T : IProtoParser<T>
    {
        SerializeWithLengthPrefix(destination, instance, style, T.ProtoWriter);
    }
#endif
    }

    /// <summary>
    /// Specifies the type of prefix that should be applied to messages.
    /// </summary>
    public enum PrefixStyle
    {
        /// <summary>
        /// No length prefix is applied to the data; the data is terminated only by the end of the stream.
        /// </summary>
        None = 0,

        /// <summary>
        /// A base-128 ("varint", the default prefix format in protobuf) length prefix is applied to the data (efficient for short messages).
        /// </summary>
        Base128 = 1,

        /// <summary>
        /// A fixed-length (little-endian) length prefix is applied to the data (useful for compatibility).
        /// </summary>
        Fixed32 = 2,

        /// <summary>
        /// A fixed-length (big-endian) length prefix is applied to the data (useful for compatibility).
        /// </summary>
        Fixed32BigEndian = 3,
    }

}