namespace GameLogic
{
    /// <summary>
    /// UI 控制器接口，用于定义 UI 消息注册行为。
    /// </summary>
    public interface IUIController
    {
        /// <summary>
        /// 注册 UI 消息处理器。
        /// </summary>
        void RegUIMessage();
    }
}