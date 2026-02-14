#if FANTASY_UNITY
using System;
namespace MongoDB.Bson.Serialization.Attributes
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class BsonElementAttribute : Attribute
    {
        public BsonElementAttribute() { }
        
        public BsonElementAttribute(string elementName) { }
    }
}
#endif