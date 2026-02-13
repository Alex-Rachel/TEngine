using System;
using System.Buffers;
using System.IO;
using LightProto.Parser;

namespace LightProto
{
    public static partial class Serializer
    {
        /// <summary>
        /// Writes a protocol-buffer representation of the given instance to the supplied writer.
        /// </summary>
        public static void Serialize<T>(
            IBufferWriter<byte> destination,
            T instance,
            IProtoWriter<T> writer
        )
        {
            if (writer.IsMessage == false && writer is not ICollectionWriter)
            {
                writer = MessageWrapper<T>.ProtoWriter.From(writer);
            }

            WriterContext.Initialize(destination, out var ctx);
            writer.WriteTo(ref ctx, instance);
            ctx.Flush();
        }

        public static void Serialize<T>(Stream destination, T instance, IProtoWriter<T> writer)
        {
            if (writer.IsMessage == false && writer is not ICollectionWriter)
            {
                writer = MessageWrapper<T>.ProtoWriter.From(writer);
            }

            using var codedOutputStream = new CodedOutputStream(destination, leaveOpen: true);
            WriterContext.Initialize(codedOutputStream, out var ctx);
            writer.WriteTo(ref ctx, instance);
            ctx.Flush();
        }

        /// <summary>
        /// Creates a deep clone of the given message.
        /// </summary>
        /// <param name="message">The instance to deep-clone.</param>
        /// <param name="reader">The proto reader of T.</param>
        /// <param name="writer">The proto writer of T.</param>
        /// <typeparam name="T">The type of the message being cloned.</typeparam>
        /// <returns>A new instance that is a deep clone of <paramref name="message"/>.</returns>
        public static T DeepClone<T>(T message, IProtoReader<T> reader, IProtoWriter<T> writer)
        {
            var size = writer.CalculateSize(message);
            var array = ArrayPool<byte>.Shared.Rent(size);
            try
            {
                var buffer = array.AsSpan(0, size);
                WriterContext.Initialize(ref buffer, out var ctx);
                writer.WriteTo(ref ctx, message);
                ctx.Flush();
                return Deserialize(buffer, reader);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(array);
            }
        }

#if NET7_0_OR_GREATER
    /// <summary>
    /// Creates a deep clone of the given message.
    /// </summary>
    /// <param name="message">The instance to deep-clone.</param>
    /// <typeparam name="T">The type of the message being cloned.</typeparam>
    /// <returns>A new instance that is a deep clone of <paramref name="message"/>.</returns>
    public static T DeepClone<T>(T message)
        where T : IProtoParser<T>
    {
        return DeepClone(message, T.ProtoReader, T.ProtoWriter);
    }

    /// <summary>
    /// Writes a protocol-buffer representation of the given instance to the supplied stream.
    /// </summary>
    /// <param name="instance">The existing instance to be serialized (cannot be null).</param>
    /// <param name="destination">The destination to write to.</param>
    public static void Serialize<T>(Stream destination, T instance)
        where T : IProtoParser<T> => Serialize(destination, instance, T.ProtoWriter);

    /// <summary>
    /// Writes a protocol-buffer representation of the given instance to the supplied writer.
    /// </summary>
    /// <param name="instance">The existing instance to be serialized (cannot be null).</param>
    /// <param name="destination">The destination to write to.</param>
    public static void Serialize<T>(IBufferWriter<byte> destination, T instance)
        where T : IProtoParser<T> => Serialize(destination, instance, T.ProtoWriter);
#endif
    }
}
