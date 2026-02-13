using System;
using System.Buffers;
using System.IO;
using LightProto.Parser;

namespace LightProto
{
    public static partial class Serializer
    {
#if NET7_0_OR_GREATER
    /// <summary>
    /// Creates a new instance from a protocol-buffer stream
    /// </summary>
    /// <typeparam name="T">The type to be created.</typeparam>
    /// <param name="source">The binary stream to apply to the new instance (cannot be null).</param>
    /// <returns>A new, initialized instance.</returns>
    public static T Deserialize<T>(Stream source)
        where T : IProtoParser<T> => Deserialize(source, T.ProtoReader);

    /// <summary>
    /// Creates a new instance from a protocol-buffer stream
    /// </summary>
    public static T Deserialize<T>(ReadOnlySequence<byte> source)
        where T : IProtoParser<T> => Deserialize(source, T.ProtoReader);

    /// <summary>
    /// Creates a new instance from a protocol-buffer stream
    /// </summary>
    public static T Deserialize<T>(ReadOnlySpan<byte> source)
        where T : IProtoParser<T> => Deserialize(source, T.ProtoReader);
#endif

        /// <summary>
        /// Creates a new instance from a protocol-buffer stream
        /// </summary>
        public static T Deserialize<T>(ReadOnlySequence<byte> source, IProtoReader<T> reader)
        {
            if (reader.IsMessage == false)
            {
                reader = MessageWrapper<T>.ProtoReader.From(reader);
            }

            ReaderContext.Initialize(source, out var ctx);
            return reader.ParseFrom(ref ctx);
        }

        /// <summary>
        /// Creates a new instance from a protocol-buffer stream
        /// </summary>
        public static T Deserialize<T>(ReadOnlySpan<byte> source, IProtoReader<T> reader)
        {
            if (reader.IsMessage == false)
            {
                reader = MessageWrapper<T>.ProtoReader.From(reader);
            }

            ReaderContext.Initialize(source, out var ctx);
            return reader.ParseFrom(ref ctx);
        }

        /// <summary>
        /// Creates a new instance from a protocol-buffer stream
        /// </summary>
        public static T Deserialize<T>(Stream source, IProtoReader<T> reader)
        {
            if (reader.IsMessage == false)
            {
                reader = MessageWrapper<T>.ProtoReader.From(reader);
            }

            using var codedStream = new CodedInputStream(source, leaveOpen: true);
            ReaderContext.Initialize(codedStream, out var ctx);
            return reader.ParseFrom(ref ctx);
        }
    }
}
