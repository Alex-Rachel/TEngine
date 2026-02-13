#if FANTASY_NET
using Fantasy.Entitas;
using Fantasy.Entitas.Interface;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
// ReSharper disable UnassignedGetOnlyAutoProperty
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

namespace Fantasy.Serialize;

public sealed class EntityTreeCollectionSerializer: IBsonSerializer
{
    public object Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var entityTreeCollection = EntityTreeCollection.Create(true);
        var bsonSerializer = BsonSerializer.LookupSerializer<Entity>();
        
        var bsonReader = context.Reader;
        bsonReader.ReadStartArray();
        
        while (bsonReader.ReadBsonType() != BsonType.EndOfDocument)
        {
            var entity = bsonSerializer.Deserialize(context);
            entityTreeCollection.Add(entity.TypeHashCode, entity);
        }
        bsonReader.ReadEndArray();

        return entityTreeCollection;
    }

    public void Serialize(BsonSerializationContext context, BsonSerializationArgs args, object value)
    {
        var bsonWriter = context.Writer;
        bsonWriter.WriteStartArray();
        var childrenCollection = (EntityTreeCollection)value;

        var bsonSerializer = BsonSerializer.LookupSerializer<Entity>();
        
        foreach (var (_, entity) in childrenCollection)
        {
            if (entity is ISupportedSerialize)
            {
                bsonSerializer.Serialize(context, entity);
            }
        }
        bsonWriter.WriteEndArray();
    }

    public Type ValueType { get; }
}
#endif
