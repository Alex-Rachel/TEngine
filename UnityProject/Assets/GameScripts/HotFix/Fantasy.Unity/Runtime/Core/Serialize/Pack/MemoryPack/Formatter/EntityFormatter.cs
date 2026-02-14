using Fantasy.Entitas.Interface;
using Fantasy.Helper;
using MemoryPack;
// ReSharper disable SuspiciousTypeConversion.Global
#pragma warning disable CS9074 // The 'scoped' modifier of parameter doesn't match overridden or implemented member.
#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
#pragma warning disable CS8602 // Dereference of a possibly null reference.
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.

namespace Fantasy.Entitas
{
    /// <summary>
    /// Entity 的 MemoryPack 序列化器，通过 TypeDictionary 动态查找具体类型的 formatter
    /// </summary>
    public sealed class EntityFormatter : MemoryPackFormatter<Entity>
    {
#if FANTASY_UNITY
        public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, ref Entity? value)
#endif
#if FANTASY_NET
        public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref Entity? value)
#endif
        {
            if (value == null)
            {
                writer.WriteNullUnionHeader();
                return;
            }

            var tag = value.TypeHashCode;
            // 从 TypeDictionary 查找具体类型
            if (!Fantasy.Serialize.MemoryPackHelper.TypeDictionary.TryGetValue(tag, out var entityType))
            {
                throw new MemoryPackSerializationException($"Entity type not found for TypeHashCode: {tag}");
            }
            // 写入TypeHashCode用于Deserialize使用
            writer.WriteValue<byte>(MemoryPackCode.WideTag);
            writer.WriteValue<long>(tag);
            // 获取该类型的 formatter 并序列化
            var formatter = writer.GetFormatter(entityType);
            object? valueObj = value;
            formatter.Serialize(ref writer, ref valueObj);
        }
#if FANTASY_UNITY
        public override void Deserialize(ref MemoryPackReader reader, ref Entity? value)
#endif
#if FANTASY_NET
        public override void Deserialize(ref MemoryPackReader reader, scoped ref Entity? value)
#endif
        {
            var isNull = reader.ReadValue<byte>() == MemoryPackCode.NullObject;

            if (isNull)
            {
                value = null;
                return;
            }

            var tag = reader.ReadValue<long>();
            
            if (!Fantasy.Serialize.MemoryPackHelper.TypeDictionary.TryGetValue(tag, out var entityType))
            {
                throw new MemoryPackSerializationException($"Entity type not found for TypeHashCode: {tag}");
            }
            
            var formatter = reader.GetFormatter(entityType);
            object? valueObj = value;
            formatter.Deserialize(ref reader, ref valueObj);
            value = valueObj as Entity;
        }
    }
}
