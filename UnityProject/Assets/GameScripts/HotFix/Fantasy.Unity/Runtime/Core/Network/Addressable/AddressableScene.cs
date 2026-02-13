#if FANTASY_NET
using Fantasy.IdFactory;
using Fantasy.Platform.Net;

namespace Fantasy.Network.Route
{
    /// <summary>
    /// AddressableScene
    /// </summary>
    public sealed class AddressableScene
    {
        /// <summary>
        /// Id
        /// </summary>
        public readonly long Id;
        /// <summary>
        /// RunTimeId
        /// </summary>
        public readonly long RunTimeId;
        /// <summary>
        /// 构造方法
        /// </summary>
        /// <param name="sceneConfig">sceneConfig</param>
        public AddressableScene(SceneConfig sceneConfig)
        {
            Id = IdFactoryHelper.EntityId(0, sceneConfig.Id, (byte)sceneConfig.WorldConfigId, 0);
            RunTimeId = IdFactoryHelper.RuntimeId(false,0, sceneConfig.Id, (byte)sceneConfig.WorldConfigId, 0);
        }
    }
}
#endif