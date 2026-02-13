#if FANTASY_UNITY
using System;
using System.Buffers;
using LightProto;

namespace Fantasy.Serialize
{
    public sealed class BsonPackHelper : ISerialize
    {
        /// <inheritdoc/>
        public string SerializeName { get; } = "Bson";
        
        /// <inheritdoc/>
        public T Deserialize<T>(byte[] bytes) where T : IProtoParser<T>
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public T Deserialize<T>(MemoryStreamBuffer buffer) where T : IProtoParser<T>
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public object Deserialize(Type type, byte[] bytes)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public object Deserialize(Type type, MemoryStreamBuffer buffer)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public T Deserialize<T>(byte[] bytes, int index, int count) where T : IProtoParser<T>
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public object Deserialize(Type type, byte[] bytes, int index, int count)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public byte[] Serialize(Type type, object @object)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public byte[] Serialize<T>(T @object) where T : IProtoParser<T>
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public void Serialize<T>(T @object, IBufferWriter<byte> buffer) where T : IProtoParser<T>
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public void Serialize(Type type, object @object, IBufferWriter<byte> buffer)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public T Clone<T>(T t) where T : IProtoParser<T>
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public object Clone(Type type, object @object)
        {
            throw new NotImplementedException();
        }
    }
}
#endif