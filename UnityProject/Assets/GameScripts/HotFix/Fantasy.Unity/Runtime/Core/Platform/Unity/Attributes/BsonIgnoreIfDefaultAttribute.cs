#if FANTASY_UNITY
using System;
namespace MongoDB.Bson.Serialization.Attributes
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class BsonIgnoreIfDefaultAttribute : Attribute
    {
        public BsonIgnoreIfDefaultAttribute() { }

        public BsonIgnoreIfDefaultAttribute(bool value) { }
    }
}
#endif