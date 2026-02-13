#if FANTASY_UNITY
using System;
namespace MongoDB.Bson.Serialization.Attributes
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class BsonDefaultValueAttribute : Attribute
    {
        public BsonDefaultValueAttribute(object defaultValue) { }
    }
}
#endif