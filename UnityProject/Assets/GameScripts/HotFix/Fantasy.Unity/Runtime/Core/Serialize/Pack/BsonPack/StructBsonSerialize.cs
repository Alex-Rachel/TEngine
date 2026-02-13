#if FANTASY_NET
using System.Reflection;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace Fantasy.Serialize;

/// <summary>
/// 提供对结构体类型进行 BSON 序列化和反序列化的辅助类。
/// </summary>
/// <typeparam name="TValue">要序列化和反序列化的结构体类型。</typeparam>
public class StructBsonSerialize<TValue> : StructSerializerBase<TValue> where TValue : struct
{
    /// <summary>
    /// 将结构体对象序列化为 BSON 数据。
    /// </summary>
    /// <param name="context">序列化上下文。</param>
    /// <param name="args">序列化参数。</param>
    /// <param name="value">要序列化的结构体对象。</param>
    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, TValue value)
    {
        var nominalType = args.NominalType;
        var bsonWriter = context.Writer;
        bsonWriter.WriteStartDocument();
        var fields = nominalType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        foreach (var field in fields)
        {
            bsonWriter.WriteName(field.Name);
            BsonSerializer.Serialize(bsonWriter, field.FieldType, field.GetValue(value));
        }
        bsonWriter.WriteEndDocument();
    }

    /// <summary>
    /// 将 BSON 数据反序列化为结构体对象。
    /// </summary>
    /// <param name="context">反序列化上下文。</param>
    /// <param name="args">反序列化参数。</param>
    /// <returns>反序列化得到的结构体对象。</returns>
    public override TValue Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        //boxing is required for SetValue to work
        object obj = new TValue();
        var actualType = args.NominalType;
        var bsonReader = context.Reader;
        bsonReader.ReadStartDocument();
        while (bsonReader.ReadBsonType() != BsonType.EndOfDocument)
        {
            var name = bsonReader.ReadName(Utf8NameDecoder.Instance);

            var field = actualType.GetField(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                var value = BsonSerializer.Deserialize(bsonReader, field.FieldType);
                field.SetValue(obj, value);
            }
        }
        bsonReader.ReadEndDocument();
        return (TValue) obj;
    }
}
#endif
