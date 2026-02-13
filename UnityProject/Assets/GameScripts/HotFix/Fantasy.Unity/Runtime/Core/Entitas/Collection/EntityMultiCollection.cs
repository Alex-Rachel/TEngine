using System;
using System.Collections.Generic;
using Fantasy.Pool;
using MemoryPack;

namespace Fantasy.Entitas
{
    [MemoryPackable(GenerateType.NoGenerate)]
    public sealed partial class EntityMultiCollection : SortedDictionary<long, Entity>, IPool, IDisposable
    {
        private bool _isPool;
        
        public static EntityMultiCollection Create(bool isPool)
        {
            return isPool ? Pool<EntityMultiCollection>.Rent() : new EntityMultiCollection();
        }

        public bool IsPool()
        {
            return _isPool;
        }

        public void SetIsPool(bool isPool)
        {
            _isPool = isPool;
        }

        public void Dispose()
        {
            if (!_isPool)
            {
                return;
            }

            _isPool = false;
            this.Clear();
            Pool<EntityMultiCollection>.Return(this);
        }
    }
}