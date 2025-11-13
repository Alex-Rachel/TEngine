using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace TEngine
{
    /// <summary>
    /// UI 模块接口：负责 UI 的创建、显示、关闭与查询。
    /// 支持异步与同步（预加载）两种打开方式。
    /// </summary>
    public interface IUIModule : IUpdateModule
    {
        /// <summary>
        /// 初始化 UI 模块。
        /// </summary>
        /// <param name="root">UI 根节点（通常为 Canvas 根）</param>
        /// <param name="isOrthographic">摄像机是否正交模式</param>
        void Initialize(Transform root, bool isOrthographic);

        /// <summary>
        /// UI 摄像机
        /// </summary>
        Camera UICamera { get; set; }

        /// <summary>
        /// UI 根节点
        /// </summary>
        Transform UICanvasRoot { get; set; }

        // ───────────────────────────────────────────────
        //  Show 系列：异步为主，同步为辅
        // ───────────────────────────────────────────────

        /// <summary>
        /// 异步显示 UI（推荐方式）
        /// </summary>
        UniTask<T> ShowUI<T>(params object[] userDatas) where T : UIBase;

        /// <summary>
        /// 异步显示 UI（使用字符串类型名）
        /// </summary>
        UniTask<UIBase>? ShowUI(string type, params object[] userDatas);

        /// <summary>
        /// 同步显示 UI（仅限资源已预加载时使用，避免死锁）
        /// </summary>
        T ShowUISync<T>(params object[] userDatas) where T : UIBase;

        // ───────────────────────────────────────────────
        //  Close / Get 系列
        // ───────────────────────────────────────────────

        /// <summary>
        /// 关闭指定类型 UI。
        /// </summary>
        void CloseUI<T>(bool force = false) where T : UIBase;

        /// <summary>
        /// 关闭指定类型 UI。
        /// </summary>
        void CloseUI(RuntimeTypeHandle handle, bool force = false);

        /// <summary>
        /// 获取当前已打开的指定类型 UI。
        /// </summary>
        T GetUI<T>() where T : UIBase;

        // ───────────────────────────────────────────────
        //  内部注入
        // ───────────────────────────────────────────────

        /// <summary>
        /// 由框架内部注入计时模块。
        /// </summary>
        protected internal void SetTimerManager(ITimerModule timerModule);
    }
}
