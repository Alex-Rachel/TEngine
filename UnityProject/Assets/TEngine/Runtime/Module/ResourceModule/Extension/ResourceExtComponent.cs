using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Serialization;
using Object = UnityEngine.Object;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif

namespace TEngine
{
    /// <summary>
    /// 资源组件拓展。
    /// </summary>
    [DisallowMultipleComponent]
    internal partial class ResourceExtComponent : MonoBehaviour
    {
        public static ResourceExtComponent Instance { private set; get; }

        private readonly TimeoutController _timeoutController = new TimeoutController();

        /// <summary>
        /// 正在加载的资源列表。
        /// </summary>
        private readonly HashSet<string> _assetLoadingList = new HashSet<string>();

        /// <summary>
        /// 检查是否可以释放间隔。
        /// </summary>
        [SerializeField]
        private float checkCanReleaseInterval = 30f;

        private float _checkCanReleaseTime = 0.0f;

        /// <summary>
        /// 对象池自动释放时间间隔。
        /// </summary>
        [SerializeField]
        private float autoReleaseInterval = 60f;

        /// <summary>
        /// 每帧最大处理资源数量，用于分帧处理避免卡顿。
        /// </summary>
        [SerializeField]
        private int maxProcessPerFrame = 50;

        /// <summary>
        /// 当前正在处理的节点，用于分帧处理。
        /// </summary>
#if ODIN_INSPECTOR
        [ShowInInspector]
#endif
        private LinkedListNode<LoadAssetObject> _currentProcessNode;

        /// <summary>
        /// 保存加载的图片对象。
        /// </summary>
#if ODIN_INSPECTOR
        [ShowInInspector]
#endif
        private LinkedList<LoadAssetObject> _loadAssetObjectsLinkedList;

        /// <summary>
        /// 散图集合对象池。
        /// </summary>
        private IObjectPool<AssetItemObject> _assetItemPool;


#if UNITY_EDITOR
        public LinkedList<LoadAssetObject> LoadAssetObjectsLinkedList
        {
            get => _loadAssetObjectsLinkedList;
            set => _loadAssetObjectsLinkedList = value;
        }
#endif
        private IEnumerator Start()
        {
            Instance = this;
            yield return new WaitForEndOfFrame();
            IObjectPoolModule objectPoolComponent = ModuleSystem.GetModule<IObjectPoolModule>();
            _assetItemPool = objectPoolComponent.CreateMultiSpawnObjectPool<AssetItemObject>(
                "SetAssetPool",
                autoReleaseInterval, 16, 60, 0);
            _loadAssetObjectsLinkedList = new LinkedList<LoadAssetObject>();

            InitializedResources();
        }

        private void Update()
        {
            _checkCanReleaseTime += Time.unscaledDeltaTime;
            if (_checkCanReleaseTime < (double)checkCanReleaseInterval)
            {
                return;
            }

            ReleaseUnused();
        }

        /// <summary>
        /// 回收无引用的缓存资产。
        /// 使用分帧处理优化性能，避免一次性处理大量资源导致卡顿。
        /// </summary>
#if ODIN_INSPECTOR
        [Button("Release Unused")]
#endif
        public void ReleaseUnused()
        {
            if (_loadAssetObjectsLinkedList == null || _loadAssetObjectsLinkedList.Count == 0)
            {
                _currentProcessNode = null;
                _checkCanReleaseTime = 0f;
                return;
            }

            // 如果当前没有正在处理的节点，从头开始
            if (_currentProcessNode == null)
            {
                _currentProcessNode = _loadAssetObjectsLinkedList.First;
            }

            int processedCount = 0;
            LinkedListNode<LoadAssetObject> current = _currentProcessNode;

            // 分帧处理：每帧最多处理 maxProcessPerFrame 个资源
            while (current != null && processedCount < maxProcessPerFrame)
            {
                var next = current.Next;
                
                if (current.Value.AssetObject.IsCanRelease())
                {
                    _assetItemPool.Unspawn(current.Value.AssetTarget);
                    MemoryPool.Release(current.Value.AssetObject);
                    _loadAssetObjectsLinkedList.Remove(current);
                }

                current = next;
                processedCount++;
            }

            // 更新当前处理节点
            _currentProcessNode = current;

            // 如果已经处理完所有节点，重置状态
            if (_currentProcessNode == null)
            {
                _checkCanReleaseTime = 0f;
            }
        }

        private void SetAsset(ISetAssetObject setAssetObject, Object assetObject)
        {
            _loadAssetObjectsLinkedList.AddLast(new LoadAssetObject(setAssetObject, assetObject));
            setAssetObject.SetAsset(assetObject);
        }

        private async UniTask TryWaitingLoading(string assetObjectKey)
        {
            if (_assetLoadingList.Contains(assetObjectKey))
            {
                try
                {
                    await UniTask.WaitUntil(
                            () => !_assetLoadingList.Contains(assetObjectKey))
#if UNITY_EDITOR
                        .AttachExternalCancellation(_timeoutController.Timeout(TimeSpan.FromSeconds(60)));
                    _timeoutController.Reset();
#else
                    ;
#endif
                }
                catch (OperationCanceledException ex)
                {
                    if (_timeoutController.IsTimeout())
                    {
                        Log.Error($"LoadAssetAsync Waiting {assetObjectKey} timeout. reason:{ex.Message}");
                    }
                }
            }
        }
    }
}