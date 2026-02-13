#if FANTASY_UNITY
using System;
namespace MongoDB.Bson.Serialization.Attributes
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class BsonIgnoreIfNullAttribute : Attribute
    {
    
    }
}
#endif