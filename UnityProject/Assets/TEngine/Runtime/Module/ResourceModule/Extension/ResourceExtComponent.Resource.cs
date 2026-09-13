using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace TEngine
{
    internal partial class ResourceExtComponent
    {
        private static IResourceModule _resourceModule;

        public static IResourceModule ResourceModule => _resourceModule;

        private class LoadingState : IMemory
        {
            public CancellationTokenSource Cts { get; set; }
            public string Location { get; set; }

            public void Cancel()
            {
                if (Cts != null && !Cts.IsCancellationRequested)
                {
                    Cts.Cancel();
                }
            }

            public void Clear()
            {
                var cts = Cts;
                Cts = null;
                if (cts != null)
                {
                    cts.Dispose();
                }

                Location = String.Empty;
            }
        }

        private static readonly Dictionary<UnityEngine.Object, LoadingState> _loadingStates = new Dictionary<UnityEngine.Object, LoadingState>();

        private void InitializedResources()
        {
            _resourceModule = ModuleSystem.GetModule<IResourceModule>();
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
                MemoryPool.Release(setAssetObject);
                return;
            }

            // 创建新的加载状态
            var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var loadingState = MemoryPool.Acquire<LoadingState>();
            loadingState.Cts = linkedTokenSource;
            loadingState.Location = location;
            ReplaceLoadingState(target, loadingState);

            var hasLoadingMarker = false;
            var setAssetObjectTransferred = false;
            T loadedResource = null;
            var resourceRegistered = false;

            try
            {
                // 等待其他可能正在进行的加载。
                await TryWaitingLoading(location).AttachExternalCancellation(linkedTokenSource.Token);

                // 再次检查是否被新请求替换。
                if (!IsCurrentRequest(target, loadingState))
                {
                    return;
                }

                // 检查缓存。
                if (_assetItemPool.CanSpawn(location))
                {
                    var assetObject = (T)_assetItemPool.Spawn(location).Target;
                    DetachCurrentRequest(target, loadingState);
                    setAssetObjectTransferred = true;
                    SetAsset(setAssetObject, assetObject);
                }
                else
                {
                    // 防止重复加载同一资源。
                    if (!_assetLoadingList.Add(location))
                    {
                        // 已经在加载中，等待回调处理。
                        Log.Warning("资源仍在加载中，跳过重复请求 '{0}'", location);
                        return;
                    }

                    hasLoadingMarker = true;

                    loadedResource = await _resourceModule.LoadAssetAsync<T>(location, linkedTokenSource.Token);
                    if (loadedResource == null)
                    {
                        if (linkedTokenSource.IsCancellationRequested)
                        {
                            return;
                        }
                        Log.Error("加载资源失败，资源为空: '{0}'", location);
                        return;
                    }

                    if (!IsCurrentRequest(target, loadingState))
                    {
                        return;
                    }

                    _assetItemPool.Register(AssetItemObject.Create(location, loadedResource), true);
                    resourceRegistered = true;
                    DetachCurrentRequest(target, loadingState);
                    setAssetObjectTransferred = true;
                    SetAsset(setAssetObject, loadedResource);
                }
            }
            catch (OperationCanceledException) when (linkedTokenSource.IsCancellationRequested)
            {
                // 请求被替换或目标销毁属于正常取消流程。
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to load asset '{location}': {ex}");
            }
            finally
            {
                DetachCurrentRequest(target, loadingState);

                if (hasLoadingMarker)
                {
                    _assetLoadingList.Remove(location);
                }

                if (loadedResource != null && !resourceRegistered)
                {
                    _resourceModule.UnloadAsset(loadedResource);
                }

                if (!setAssetObjectTransferred)
                {
                    MemoryPool.Release(setAssetObject);
                }
                MemoryPool.Release(loadingState);
            }
        }

        private void DetachCurrentRequest(UnityEngine.Object target, LoadingState expectedState)
        {
            if (_loadingStates.TryGetValue(target, out var curState)
                && ReferenceEquals(curState, expectedState))
            {
                _loadingStates.Remove(target);
            }
        }

        private void ReplaceLoadingState(UnityEngine.Object target, LoadingState newState)
        {
            if (_loadingStates.Remove(target, out var oldState))
            {
                oldState.Cancel();
            }
            _loadingStates[target] = newState;
        }

        private bool IsCurrentRequest(UnityEngine.Object target, LoadingState expectedState)
        {
            if (target == null)
            {
                return false;
            }
            return _loadingStates.TryGetValue(target, out var curState)
                   && ReferenceEquals(curState, expectedState);
        }

        /// <summary>
        /// 组件销毁时清理所有资源。
        /// </summary>
        private void OnDestroy()
        {
            var loadingStates = new LoadingState[_loadingStates.Count];
            _loadingStates.Values.CopyTo(loadingStates, 0);
            _loadingStates.Clear();
            foreach (var state in loadingStates)
            {
                state.Cancel();
            }
        }
    }
}