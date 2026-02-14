using System.Collections.Generic;
using Fantasy.Pool;

#pragma warning disable CS8714 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match 'notnull' constraint.

namespace Fantasy.Entitas
{
    internal sealed class EntityPool : PoolCore
    {
        public EntityPool() : base(4096) { }
    }
}