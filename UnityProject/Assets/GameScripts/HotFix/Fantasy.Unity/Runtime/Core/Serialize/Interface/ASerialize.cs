using System;
using System.ComponentModel;
using System.Runtime.Serialization;
using Fantasy.Pool;
#if FANTASY_NET || FANTASY_UNITY || FANTASY_CONSOLE
using MongoDB.Bson.Serialization.Attributes;
#endif
using Newtonsoft.Json;
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace Fantasy.Serialize
{
    public abstract class ASerialize : ISupportInitialize, IDisposable
    {
        public virtual void Dispose() { }
        public virtual void BeginInit() { }
        public virtual void EndInit() { }
        public virtual void AfterDeserialization() => EndInit();
    }
}