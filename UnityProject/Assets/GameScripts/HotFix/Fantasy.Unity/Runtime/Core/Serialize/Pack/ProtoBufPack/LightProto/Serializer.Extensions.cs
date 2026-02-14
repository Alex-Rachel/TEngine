using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using LightProto.Parser;

namespace LightProto
{
    public static partial class Serializer
    {
#if NET7_0_OR_GREATER
    public static int CalculateSize<T>(T message)
        where T : IProtoParser<T>
    {
        return T.ProtoWriter.CalculateSize(message);
    }

    public static byte[] ToByteArray<T>(this T message)
        where T : IProtoParser<T> => ToByteArray(message, T.ProtoWriter);
#endif

        public static int CalculateMessageSize<T>(this IProtoWriter<T> writer, T value)
        {
            var size = writer.CalculateSize(value);
            if (writer.IsMessage)
            {
                return CodedOutputStream.ComputeLengthSize(size) + size;
            }
            else
            {
                return size;
            }
        }

        public static void WriteMessageTo<T>(
            this IProtoWriter<T> writer,
            ref WriterContext output,
            T value
        )
        {
            if (writer.IsMessage)
            {
                output.WriteLength(writer.CalculateSize(value));
            }

            writer.WriteTo(ref output, value);
        }

        public static T ParseMessageFrom<T>(this IProtoReader<T> reader, ref ReaderContext input)
        {
            if (reader.IsMessage)
            {
                var length = input.ReadInt64();
                if (input.state.recursionDepth >= input.state.recursionLimit)
                {
                    throw InvalidProtocolBufferException.RecursionLimitExceeded();
                }

                var oldLimit = SegmentedBufferHelper.PushLimit(ref input.state, length);
                ++input.state.recursionDepth;
                var message = reader.ParseFrom(ref input);

                if (input.state.lastTag != 0)
                {
                    throw InvalidProtocolBufferException.MoreDataAvailable();
                }

                // Check that we've read exactly as much data as expected.
                if (!SegmentedBufferHelper.IsReachedLimit(ref input.state))
                {
                    throw InvalidProtocolBufferException.TruncatedMessage();
                }

                --input.state.recursionDepth;
                SegmentedBufferHelper.PopLimit(ref input.state, oldLimit);
                return message;
            }
            else
            {
                return reader.ParseFrom(ref input);
            }
        }

        public static byte[] ToByteArray<T>(this T message, IProtoWriter<T> writer)
        {
            if (writer.IsMessage == false && writer is not ICollectionWriter)
            {
                writer = MessageWrapper<T>.ProtoWriter.From(writer);
            }

            var buffer = new byte[writer.CalculateSize(message)];
            using CodedOutputStream output = new CodedOutputStream(buffer);
            WriterContext.Initialize(output, out var ctx);
            writer.WriteTo(ref ctx, message);
            ctx.Flush();
            return buffer;
        }

        public static void SerializeTo<T>(
            this T instance,
            Stream destination,
            IProtoWriter<T> writer
        ) => Serialize(destination, instance, writer);

        public static void SerializeTo<T>(
            this T instance,
            IBufferWriter<byte> destination,
            IProtoWriter<T> writer
        ) => Serialize(destination, instance, writer);
    }
}
