#if FANTASY_NET
using Fantasy.Sphere;
using MemoryPack;

namespace Fantasy.Entitas;

public sealed class SphereEventArgsFormatter : MemoryPackFormatter<SphereEventArgs>
{
    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref SphereEventArgs? value)
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
            throw new MemoryPackSerializationException($"SphereEventArgs type not found for TypeHashCode: {tag}");
        }
        // 写入TypeHashCode用于Deserialize使用
        writer.WriteValue<byte>(MemoryPackCode.WideTag);
        writer.WriteValue<long>(tag);
        // 获取该类型的 formatter 并序列化
        var formatter = writer.GetFormatter(entityType);
        object? valueObj = value;
        formatter.Serialize(ref writer, ref valueObj);
    }

    public override void Deserialize(ref MemoryPackReader reader, scoped ref SphereEventArgs? value)
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
            throw new MemoryPackSerializationException($"SphereEventArgs type not found for TypeHashCode: {tag}");
        }
            
        var formatter = reader.GetFormatter(entityType);
        object? valueObj = value;
        formatter.Deserialize(ref reader, ref valueObj);
        value = valueObj as SphereEventArgs;
    }
}
#endif
