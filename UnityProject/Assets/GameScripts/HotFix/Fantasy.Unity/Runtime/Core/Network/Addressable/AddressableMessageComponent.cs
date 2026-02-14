#if FANTASY_NET
using System;
using Fantasy.Async;
using Fantasy.Entitas;
using Fantasy.Entitas.Interface;
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace Fantasy.Network.Route
{
    public class AddressableMessageComponentDestroySystem : DestroySystem<AddressableMessageComponent>
    {
        protected override void Destroy(AddressableMessageComponent self)
        {
            if (self.AddressableId != 0)
            {
                AddressableHelper.RemoveAddressable(self.Scene, self.AddressableId).Coroutine();
                self.AddressableId = 0;
            }
        }
    }

    /// <summary>
    /// 可寻址消息组件、挂载了这个组件可以接收Addressable消息
    /// </summary>
    public sealed class AddressableMessageComponent : Entity
    {
        /// <summary>
        /// 可寻址消息组件的唯一标识。
        /// </summary>
        public long AddressableId;

        /// <summary>
        /// 注册可寻址消息组件。
        /// </summary>
        /// <param name="isLock">是否进行锁定。</param>
        public FTask Register(bool isLock = true)
        {
            if (Parent == null)
            {
                throw new Exception("AddressableRouteComponent must be mounted under a component");
            }

            AddressableId = Parent.Id;
            
            if (AddressableId == 0)
            {
                throw new Exception("AddressableRouteComponent.Parent.Id is null");
            }

#if FANTASY_DEVELOP
            Log.Debug($"AddressableMessageComponent Register addressableId:{AddressableId} Address:{Parent.Address}");
#endif
            return AddressableHelper.AddAddressable(Scene, AddressableId, Parent.Address, isLock);
        }

        /// <summary>
        /// 锁定可寻址消息组件。
        /// </summary>
        public FTask Lock()
        {
#if FANTASY_DEVELOP
            Log.Debug($"AddressableMessageComponent Lock {Parent.Id}");
#endif
            return AddressableHelper.LockAddressable(Scene, Parent.Id);
        }

        /// <summary>
        /// 解锁可寻址消息组件。
        /// </summary>
        /// <param name="source">解锁来源。</param>
        public FTask UnLock(string source)
        {
#if FANTASY_DEVELOP
            Log.Debug($"AddressableMessageComponent UnLock {Parent.Id} {Parent.Address}");
#endif
            return AddressableHelper.UnLockAddressable(Scene, Parent.Id, Parent.Address, source);
        }

        /// <summary>
        /// 锁定可寻址消息并且释放掉AddressableMessageComponent组件。
        /// 该方法不会自动取Addressable中心删除自己的信息。
        /// 用于传送或转移到其他服务器时使用
        /// </summary>
        public async FTask LockAndRelease()
        {
            await Lock();
            AddressableId = 0;
            Dispose();
        }
    }
}
#endif