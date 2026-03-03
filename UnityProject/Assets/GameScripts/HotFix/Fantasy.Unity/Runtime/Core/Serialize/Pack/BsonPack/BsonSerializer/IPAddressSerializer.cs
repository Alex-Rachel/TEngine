#if FANTASY_NET
using System.Net;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace Fantasy.Serialize
{
    public class IPAddressSerializer : SerializerBase<IPAddress>
    {
        public override void Serialize(
            BsonSerializationContext context,
            BsonSerializationArgs args,
            IPAddress value)
        {
            context.Writer.WriteString(value.ToString());
        }

        public override IPAddress Deserialize(
            BsonDeserializationContext context,
            BsonDeserializationArgs args)
        {
            return IPAddress.Parse(context.Reader.ReadString());
        }
    }
}
#endif