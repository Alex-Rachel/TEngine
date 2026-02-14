using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace TEngine
{
    internal partial class ResourceExtComponent
    {
        private static IResourceModule _resourceModule;
        private LoadAssetCallbacks _loadAssetCallbacks;

        public static IResourceModule ResourceModule => _resourceModule;

        private class LoadingState : IMemory
        {
            public CancellationTokenSource Cts { get; set; }
            public string Location { get; set; }

            public void Clear()
            {
                if (Cts != null)
                {
                    Cts.Cancel();
                    Cts.Dispose();
                    Cts = null;
                }

                Location = String.Empty;
            }
        }

        private static readonly Dictionary<UnityEngine.Object, LoadingState> _loadingStates = new Dictionary<UnityEngine.Object, LoadingState>();

        private void InitializedResources()
        {
            _resourceModule = ModuleSystem.GetModule<IResourceModule>();
            _loadAssetCallbacks = new LoadAssetCallbacks(OnLoadAssetSuccess, OnLoadAssetFailure);
        }

        private void OnLoadAssetFailure(string assetName, LoadResourceStatus status, string errormessage, object userdata)
        {
            _assetLoadingList.Remove(assetName);
            ISetAssetObject setAssetObject = (ISetAssetObject)userdata;
            if (setAssetObject != null)
            {
                ClearLoadingState(setAssetObject.TargetObject);
            }

            Log.Error("Can not load asset from '{0}' with error message '{1}'.", assetName, errormessage);
        }

        private void OnLoadAssetSuccess(string assetName, object asset, float duration, object userdata)
        {
            _assetLoadingList.Remove(assetName);
            ISetAssetObject setAssetObject = (ISetAssetObject)userdata;
            UnityEngine.Object assetObject = asset as UnityEngine.Object;

            if (assetObject != null)
            {
                // 检查资源是否仍然是当前需要的。
                if (IsCurrentLocation(setAssetObject.TargetObject, setAssetObject.Location))
                {
                    ClearLoadingState(setAssetObject.TargetObject);

                    _assetItemPool.Register(AssetItemObject.Create(setAssetObject.Location, assetObject), true);
                    SetAsset(setAssetObject, assetObject);
                }
                else
                {
                    // 资源已经过期，卸载。
                    _resourceModule.UnloadAsset(assetObject);
                }
            }
            else
            {
                Log.Error($"Load failure asset type is {asset.GetType()}.");
            }
        }

        /// <summary>
        /// 通过Unity对象加载资源。
        /// </summary>
        /// <param name="setAssetObject">ISetAssetObject。</param>
        /// <param name="cancellationToken">CancellationToken。</param>
        /// <typeparam name="T">Unity对象类型。</typeparam>
        public async UniTaskVoid SetAssetByResources<T>(ISetAssetObject setAssetObject, CancellationToken cancellationToken) where T : UnityEngine.Object
        {
            var target = setAssetObject.TargetObject;
            var location = setAssetObject.Location;

            if (target == null)
            {
                return;
            }
            
            // 取消并清理旧的加载请求。
            CancelAndCleanupOldRequest(target);

            // 创建新的加载状态
            var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var loadingState = MemoryPool.Acquire<LoadingState>();
            loadingState.Cts = linkedTokenSource;
            loadingState.Location = location;
            _loadingStates[target] = loadingState;

            try
            {
                // 等待其他可能正在进行的加载。
                await TryWaitingLoading(location).AttachExternalCancellation(linkedTokenSource.Token);

                // 再次检查是否被新请求替换。
                if (!IsCurrentLocation(target, location))
                {
                    return;
                }

                // 检查缓存。
                if (_assetItemPool.CanSpawn(location))
                {
                    ClearLoadingState(target);

                    var assetObject = (T)_assetItemPool.Spawn(location).Target;
                    SetAsset(setAssetObject, assetObject);
                }
                else
                {
                    // 最后一次检查是否被替换。
                    if (!IsCurrentLocation(target, location))
                    {
                        return;
                    }

                    // 防止重复加载同一资源。
                    if (!_assetLoadingList.Add(location))
                    {
                        // 已经在加载中，等待回调处理。
                        return;
                    }

                    T resource = await _resourceModule.LoadAssetAsync<T>(location, linkedTokenSource.Token);
                    if (resource != null)
                    {
                        _loadAssetCallbacks?.LoadAssetSuccessCallback.Invoke(location,resource, 0f, setAssetObject);
                        
                    }
                    _assetLoadingList.Remove(location);
                }
            }
            catch (OperationCanceledException)
            {
                // 请求被取消，正常情况，无需处理。
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to load asset '{location}': {ex}");
                ClearLoadingState(target);
            }
        }

        /// <summary>
        /// 取消并清理旧的加载请求。
        /// <param name="target">Unity对象。</param>
        /// </summary>
        private void CancelAndCleanupOldRequest(UnityEngine.Object target)
        {
            if (_loadingStates.TryGetValue(target, out var oldState))
            {
                MemoryPool.Release(oldState);
                _loadingStates.Remove(target);
            }
        }

        /// <summary>
        /// 清理加载状态。
        /// <param name="target">Unity对象。</param>
        /// </summary>
        private void ClearLoadingState(UnityEngine.Object target)
        {
            if (_loadingStates.TryGetValue(target, out var state))
            {
                MemoryPool.Release(state);
                _loadingStates.Remove(target);
            }
        }

        /// <summary>
        /// 检查指定位置是否仍是该目标的当前加载位置。
        /// </summary>
        private bool IsCurrentLocation(UnityEngine.Object target, string location)
        {
            if (target == null)
            {
                return false;
            }
            return _loadingStates.TryGetValue(target, out var state) && state.Location == location;
        }

        /// <summary>
        /// 组件销毁时清理所有资源。
        /// </summary>
        private void OnDestroy()
        {
            foreach (var state in _loadingStates.Values)
            {
                MemoryPool.Release(state);
            }

            _loadingStates.Clear();
        }
    }
}
