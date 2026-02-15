namespace GameLogic
{
    /// <summary>
    /// 数据中心模块接口，定义模块生命周期回调。
    /// </summary>
    public interface IDataCenterModule
    {
        /// <summary>
        /// 模块初始化时调用。
        /// </summary>
        void OnInit();

        /// <summary>
        /// 角色登录成功后调用。
        /// </summary>
        void OnRoleLogin();

        /// <summary>
        /// 角色登出时调用。
        /// </summary>
        void OnRoleLogout();

        /// <summary>
        /// 每帧更新时调用。
        /// </summary>
        void OnUpdate();

        /// <summary>
        /// 主玩家地图切换时调用。
        /// </summary>
        void OnMainPlayerMapChange();
    }

    /// <summary>
    /// 数据中心模块基类，提供单例模式和生命周期虚方法。
    /// </summary>
    /// <typeparam name="T">模块类型</typeparam>
    public class DataCenterModule<T> : IDataCenterModule where T : new()
    {
        private static T m_instance;

        /// <summary>
        /// 获取模块单例实例。
        /// </summary>
        public static T Instance => m_instance != null ? m_instance : m_instance = new T();

        /// <summary>
        /// 模块初始化时调用。
        /// </summary>
        public virtual void OnInit() { }

        /// <summary>
        /// 角色登录成功后调用。
        /// </summary>
        public virtual void OnRoleLogin() { }

        /// <summary>
        /// 角色登出时调用。
        /// </summary>
        public virtual void OnRoleLogout() { }

        /// <summary>
        /// 每帧更新时调用。
        /// </summary>
        public virtual void OnUpdate() { }

        /// <summary>
        /// 主玩家地图切换时调用。
        /// </summary>
        public virtual void OnMainPlayerMapChange() { }
    }
}