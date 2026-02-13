#if FANTASY_NET
using System;
using System.Collections.Generic;
using Fantasy.Async;
using Fantasy.Entitas;
using Fantasy.Entitas.Interface;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
namespace Fantasy.Network.Route
{
    public class AddressableManageComponentAwakeSystem : AwakeSystem<AddressableManageComponent>
    {
        protected override void Awake(AddressableManageComponent self)
        {
            self.AddressableLock = self.Scene.CoroutineLockComponent.Create(self.GetType().TypeHandle.Value.ToInt64());
        }
    }
    
    public class AddressableManageComponentDestroySystem : DestroySystem<AddressableManageComponent>
    {
        protected override void Destroy(AddressableManageComponent self)
        {
            foreach (var (_, waitCoroutineLock) in self.Locks)
            {
                waitCoroutineLock.Dispose();
            }
            
            self.Locks.Clear();
            self.Addressable.Clear();
            self.AddressableLock.Dispose();
            self.AddressableLock = null;
        }
    }

    public sealed class AddressableManageComponent : Entity
    {
        public CoroutineLock AddressableLock;
        public readonly Dictionary<long, long> Addressable = new();
        public readonly Dictionary<long, WaitCoroutineLock> Locks = new();
        
        /// <summary>
        /// 添加地址映射。
        /// </summary>
        /// <param name="addressableId">地址映射的唯一标识。</param>
        /// <param name="address">Address。</param>
        /// <param name="isLock">是否进行锁定。</param>
        public async FTask Add(long addressableId, long address, bool isLock)
        {
            WaitCoroutineLock waitCoroutineLock = null;
            
            try
            {
                if (isLock)
                {
                    waitCoroutineLock = await AddressableLock.Wait(addressableId);
                }
                
                Addressable[addressableId] = address;
#if FANTASY_DEVELOP
                Log.Debug($"AddressableManageComponent Add addressableId:{addressableId} Address:{address}");
#endif
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
            finally
            {
                waitCoroutineLock?.Dispose();
            }
        }

        /// <summary>
        /// 获取地址映射的Address。
        /// </summary>
        /// <param name="addressableId">地址映射的唯一标识。</param>
        /// <returns>地址映射的Address。</returns>
        public async FTask<long> Get(long addressableId)
        {
            using (await AddressableLock.Wait(addressableId))
            {
                Addressable.TryGetValue(addressableId, out var address);
                return address;
            }
        }

        /// <summary>
        /// 移除地址映射。
        /// </summary>
        /// <param name="addressableId">地址映射的唯一标识。</param>
        public async FTask Remove(long addressableId)
        {
            using (await AddressableLock.Wait(addressableId))
            {
                Addressable.Remove(addressableId);
#if FANTASY_DEVELOP
                Log.Debug($"Addressable Remove addressableId: {addressableId} _addressable:{Addressable.Count}");
#endif
            }
        }

        /// <summary>
        /// 锁定地址映射。
        /// </summary>
        /// <param name="addressableId">地址映射的唯一标识。</param>
        public async FTask Lock(long addressableId)
        {
            var waitCoroutineLock = await AddressableLock.Wait(addressableId);
            Locks.Add(addressableId, waitCoroutineLock);
        }

        /// <summary>
        /// 解锁地址映射。
        /// </summary>
        /// <param name="addressableId">地址映射的唯一标识。</param>
        /// <param name="address">Address。</param>
        /// <param name="source">解锁来源。</param>
        public void UnLock(long addressableId, long address, string source)
        {
            if (!Locks.Remove(addressableId, out var coroutineLock))
            {
                Log.Error($"Addressable unlock not found addressableId: {addressableId} Source:{source}");
                return;
            }

            if (address != 0)
            {
                Addressable[addressableId] = address;
            }

            coroutineLock.Dispose();
        }
    }
}
#endif