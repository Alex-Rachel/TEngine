using YooAsset;

namespace TEngine
{
    /// <summary>
    /// 音频数据。
    /// </summary>
    public class AudioData : MemoryObject
    {
        /// <summary>
        /// 资源句柄。
        /// </summary>
        public AssetHandle AssetHandle { private set; get; }

        /// <summary>
        /// 是否使用对象池。
        /// </summary>
        public bool InPool { private set; get; } = false;

        public override void InitFromPool()
        {
        }

        /// <summary>
        /// 回收到对象池。
        /// </summary>
        public override void RecycleToPool()
        {
            if (!InPool)
            {
                AssetHandle.Dispose();
            }

            InPool = false;
            AssetHandle = null;
        }

        /// <summary>
        /// 生成音频数据。
        /// </summary>
        /// <param name="assetHandle">资源操作句柄。</param>
        /// <param name="inPool">是否使用对象池。</param>
        /// <returns>音频数据。</returns>
        internal static AudioData Alloc(AssetHandle assetHandle, bool inPool)
        {
            AudioData ret = MemoryPool.Acquire<AudioData>();
            ret.AssetHandle = assetHandle;
            ret.InPool = inPool;
            ret.InitFromPool();
            return ret;
        }

        /// <summary>
        /// 回收音频数据。
        /// </summary>
        /// <param name="audioData"></param>
        internal static void DeAlloc(AudioData audioData)
        {
            if (audioData != null)
            {
                MemoryPool.Release(audioData);
                audioData.RecycleToPool();
            }
        }
    }
}