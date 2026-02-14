#if FANTASY_NET
using System.Collections.Generic;
using Fantasy.Async;
using Fantasy.InnerMessage;
using Fantasy.Platform.Net;
namespace Fantasy.Network.Route
{
    /// <summary>
    /// 提供操作地址映射的辅助方法。
    /// </summary>
    public static class AddressableHelper
    {
        // 声明一个私有静态只读列表 AddressableScenes，用于存储地址映射的场景配置信息
        private static readonly List<AddressableScene> AddressableScenes = new List<AddressableScene>();

        static AddressableHelper()
        {
            // 遍历场景配置信息，筛选出地址映射类型的场景，并添加到 AddressableScenes 列表中
            foreach (var sceneConfig in SceneConfigData.Instance.List)
            {
                if (sceneConfig.SceneTypeString == "Addressable")
                {
                    AddressableScenes.Add(new AddressableScene(sceneConfig));
                }
            }
        }

        /// <summary>
        /// 添加地址映射并返回操作结果。
        /// </summary>
        /// <param name="scene">场景实例。</param>
        /// <param name="addressableId">地址映射的唯一标识。</param>
        /// <param name="address">Address。</param>
        /// <param name="isLock">是否锁定。</param>
        public static async FTask AddAddressable(Scene scene, long addressableId, long address, bool isLock = true)
        {
            // 获取指定索引的地址映射场景配置信息
            var addressableScene = AddressableScenes[(int)addressableId % AddressableScenes.Count];
            // 调用内部路由方法，发送添加地址映射的请求并等待响应
            var response = await scene.NetworkMessagingComponent.Call(addressableScene.RunTimeId,
                new I_AddressableAdd_Request
                {
                    AddressableId = addressableId, Address = address, IsLock = isLock
                });
            if (response.ErrorCode != 0)
            {
                Log.Error($"AddAddressable error is {response.ErrorCode}");
            }
        }

        /// <summary>
        /// 获取地址映射的路由 ID。
        /// </summary>
        /// <param name="scene">场景实例。</param>
        /// <param name="addressableId">地址映射的唯一标识。</param>
        /// <returns>地址映射的路由 ID。</returns>
        public static async FTask<long> GetAddressableAddress(Scene scene, long addressableId)
        {
            // 获取指定索引的地址映射场景配置信息
            var addressableScene = AddressableScenes[(int)addressableId % AddressableScenes.Count];
            // 调用内部路由方法，发送获取地址映射路由 ID 的请求并等待响应
            var response = (I_AddressableGet_Response) await scene.NetworkMessagingComponent.Call(addressableScene.RunTimeId,
                new I_AddressableGet_Request
                {
                    AddressableId = addressableId
                });
            // 检查响应错误码，如果为零，返回路由 ID；否则，输出错误信息并返回 0
            if (response.ErrorCode == 0)
            {
                return response.Address;
            }

            Log.Error($"GetAddressable error is {response.ErrorCode} addressableId:{addressableId}");
            return 0;
        }

        /// <summary>
        /// 移除指定地址映射。
        /// </summary>
        /// <param name="scene">场景实例。</param>
        /// <param name="addressableId">地址映射的唯一标识。</param>
        public static async FTask RemoveAddressable(Scene scene, long addressableId)
        {
            var addressableScene = AddressableScenes[(int)addressableId % AddressableScenes.Count];
            var response = await scene.NetworkMessagingComponent.Call(addressableScene.RunTimeId,
                new I_AddressableRemove_Request
                {
                    AddressableId = addressableId
                });
            
            if (response.ErrorCode != 0)
            {
                Log.Error($"RemoveAddressable error is {response.ErrorCode}");
            }
        }

        /// <summary>
        /// 锁定指定地址映射。
        /// </summary>
        /// <param name="scene">场景实例。</param>
        /// <param name="addressableId">地址映射的唯一标识。</param>
        public static async FTask LockAddressable(Scene scene, long addressableId)
        {
            var addressableScene = AddressableScenes[(int)addressableId % AddressableScenes.Count];
            var response = await scene.NetworkMessagingComponent.Call(addressableScene.RunTimeId,
                new I_AddressableLock_Request
                {
                    AddressableId = addressableId
                });
            
            if (response.ErrorCode != 0)
            {
                Log.Error($"LockAddressable error is {response.ErrorCode}");
            }
        }

        /// <summary>
        /// 解锁指定地址映射。
        /// </summary>
        /// <param name="scene">场景实例。</param>
        /// <param name="addressableId">地址映射的唯一标识。</param>
        /// <param name="address">Address。</param>
        /// <param name="source">解锁来源。</param>
        public static async FTask UnLockAddressable(Scene scene, long addressableId, long address, string source)
        {
            var addressableScene = AddressableScenes[(int)addressableId % AddressableScenes.Count];
            var response = await scene.NetworkMessagingComponent.Call(addressableScene.RunTimeId,
                new I_AddressableUnLock_Request
                {
                    AddressableId = addressableId,
                    Address = address,
                    Source = source
                });

            if (response.ErrorCode != 0)
            {
                Log.Error($"UnLockAddressable error is {response.ErrorCode}");
            }
        }
    }
}
#endif