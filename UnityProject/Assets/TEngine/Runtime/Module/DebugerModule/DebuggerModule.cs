using UnityEngine;

namespace TEngine
{
    /// <summary>
    /// 调试器管理器。
    /// </summary>
    internal sealed partial class DebuggerModule : Module, IDebuggerModule, IUpdateModule
    {
        private DebuggerWindowGroup _debuggerWindowRoot;
        private bool _activeWindow;

        /// <summary>
        /// 初始化调试器管理器的新实例。
        /// </summary>
        public override void OnInit()
        {
            _debuggerWindowRoot = new DebuggerWindowGroup();
            _activeWindow = false;
        }

        /// <summary>
        /// 获取游戏框架模块优先级。
        /// </summary>
        /// <remarks>优先级较高的模块会优先轮询，并且关闭操作会后进行。</remarks>
        public override int Priority => -1;

        /// <summary>
        /// 获取或设置调试器窗口是否激活。
        /// </summary>
        public bool ActiveWindow
        {
            get => _activeWindow;
            set => _activeWindow = value;
        }

        /// <summary>
        /// 调试器窗口根结点。
        /// </summary>
        public IDebuggerWindowGroup DebuggerWindowRoot => _debuggerWindowRoot;

        /// <summary>
        /// 调试器管理器轮询。
        /// </summary>
        public void Update(float elapseSeconds, float realElapseSeconds)
        {
            if (!_activeWindow)
            {
                return;
            }

            _debuggerWindowRoot.OnUpdate(elapseSeconds, realElapseSeconds);
        }

        /// <summary>
        /// 关闭并清理调试器管理器。
        /// </summary>
        public override void Shutdown()
        {
            _activeWindow = false;
            _debuggerWindowRoot.Shutdown();
        }

        /// <summary>
        /// 注册调试器窗口。
        /// </summary>
        /// <param name="path">调试器窗口路径。</param>
        /// <param name="debuggerWindow">要注册的调试器窗口。</param>
        /// <param name="args">初始化调试器窗口参数。</param>
        public void RegisterDebuggerWindow(string path, IDebuggerWindow debuggerWindow, params object[] args)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new GameFrameworkException("Path is invalid.");
            }

            if (debuggerWindow == null)
            {
                throw new GameFrameworkException("Debugger window is invalid.");
            }

            _debuggerWindowRoot.RegisterDebuggerWindow(path, debuggerWindow);
            debuggerWindow.Initialize(args);
        }

        /// <summary>
        /// 解除注册调试器窗口。
        /// </summary>
        /// <param name="path">调试器窗口路径。</param>
        /// <returns>是否解除注册调试器窗口成功。</returns>
        public bool UnregisterDebuggerWindow(string path)
        {
            return _debuggerWindowRoot.UnregisterDebuggerWindow(path);
        }

        /// <summary>
        /// 获取调试器窗口。
        /// </summary>
        /// <param name="path">调试器窗口路径。</param>
        /// <returns>要获取的调试器窗口。</returns>
        public IDebuggerWindow GetDebuggerWindow(string path)
        {
            return _debuggerWindowRoot.GetDebuggerWindow(path);
        }

        /// <summary>
        /// 选中调试器窗口。
        /// </summary>
        /// <param name="path">调试器窗口路径。</param>
        /// <returns>是否成功选中调试器窗口。</returns>
        public bool SelectDebuggerWindow(string path)
        {
            return _debuggerWindowRoot.SelectDebuggerWindow(path);
        }
    }
}