using System.Buffers;
using System.Runtime.CompilerServices;
using Fantasy.Entitas.Interface;
using MemoryPack;
using MemoryPack.Formatters;
// ReSharper disable SuspiciousTypeConversion.Global
#pragma warning disable CS9074 // The 'scoped' modifier of parameter doesn't match overridden or implemented member.
#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
#pragma warning disable CS8602 // Dereference of a possibly null reference.
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.

namespace Fantasy.Entitas
{
    /// <summary>
    /// EntityTreeCollection的MemoryPack格式化器
    /// 用于序列化Entity集合时的特殊处理
    /// </summary>
    public sealed class EntityTreeCollectionFormatter : MemoryPackFormatter<EntityTreeCollection>
    {
#if FANTASY_UNITY
        public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, ref EntityTreeCollection? value)
#endif
#if FANTASY_NET
        public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref EntityTreeCollection? value)
#endif
        {
            if (value == null)
            {
                writer.WriteNullCollectionHeader();
                return;
            }

            var count = 0;
            var formatter = writer.GetFormatter<Entity>();
            ref var spanReference = ref writer.GetSpanReference(4);
            writer.Advance(4);

            foreach (var kv in value)
            {
                var entity = kv.Value;

                if (entity is not ISupportedSerialize)
                {
                    continue;
                }

                ++count;
                formatter.Serialize(ref writer, ref entity!);
            }

            Unsafe.WriteUnaligned(ref spanReference, count);
        }
#if FANTASY_UNITY
        public override void Deserialize(ref MemoryPackReader reader, ref EntityTreeCollection? value)
#endif
#if FANTASY_NET
        public override void Deserialize(ref MemoryPackReader reader, scoped ref EntityTreeCollection? value)
#endif
        {
            if (!reader.TryReadCollectionHeader(out var length))
            {
                value = null;
                return;
            }

            if (value == null)
            {
                value = EntityTreeCollection.Create(true);
            }
            else
            {
                value.Clear();
            }

            var formatter = reader.GetFormatter<Entity>();

            for (var i = 0; i < length; i++)
            {
                Entity entity = null;
                formatter.Deserialize(ref reader, ref entity);
                value.Add(entity.TypeHashCode, entity);
            }
        }
    }
}
