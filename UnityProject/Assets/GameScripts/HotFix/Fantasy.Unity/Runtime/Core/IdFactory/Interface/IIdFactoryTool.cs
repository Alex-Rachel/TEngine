namespace Fantasy.IdFactory
{
    /// <summary>
    /// Id扩展工具接口
    /// </summary>
    public interface IIdFactoryTool
    {
        /// <summary>
        /// 获取 RuntimeId 中的 IsPool 标志
        /// </summary>
        /// <param name="runtimeId"></param>
        /// <returns></returns>
        public bool GetIsPool(ref long runtimeId);
        /// <summary>
        /// 获取 RuntimeId 中的 IsPool 标志
        /// </summary>
        /// <param name="runtimeId"></param>
        /// <returns></returns>
        public bool GetIsPool(long runtimeId);
        /// <summary>
        /// 获得创建时间
        /// </summary>
        /// <param name="entityId"></param>
        /// <returns></returns>
        public uint GetTime(ref long entityId);
        /// <summary>
        /// 获得创建时间
        /// </summary>
        /// <param name="entityId"></param>
        /// <returns></returns>
        public uint GetTime(long entityId);
        /// <summary>
        /// 获得SceneId
        /// </summary>
        /// <param name="entityId"></param>
        /// <returns></returns>
        public uint GetSceneId(ref long entityId);
        /// <summary>
        /// 获得SceneId
        /// </summary>
        /// <param name="entityId"></param>
        /// <returns></returns>
        public uint GetSceneId(long entityId);
        /// <summary>
        /// 获得WorldId
        /// </summary>
        /// <param name="entityId"></param>
        /// <returns></returns>
        public byte GetWorldId(ref long entityId);
        /// <summary>
        /// 获得WorldId
        /// </summary>
        /// <param name="entityId"></param>
        /// <returns></returns>
        public byte GetWorldId(long entityId);
    }
}