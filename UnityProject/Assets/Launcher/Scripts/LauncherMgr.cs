using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Launcher
{
    /// <summary>
    /// 热更界面加载管理器。
    /// </summary>
    public static class LauncherMgr
    {
        private static Transform _uiRoot;
        private static readonly Dictionary<string, string> _uiList = new Dictionary<string, string>();
        private static readonly Dictionary<string, UIBase> _uiMap = new Dictionary<string, UIBase>();

        /// <summary>
        /// 初始化根节点。
        /// </summary>
        public static void Initialize()
        {
            _uiRoot = GameObject.Find("[UI Root]/UICanvas")?.transform;
            if (_uiRoot == null)
            {
                Debug.LogError("Failed to Find UIRoot. Please check the resource path");
                return;
            }

            RegisterUI();
        }

        public static void RegisterUI()
        {
            UIDefine.RegisterUI(_uiList);
        }

        /// <summary>
        /// show ui
        /// </summary>
        /// <param name="uiInfo">对应的ui的名称。</param>
        /// <param name="param">参数。</param>
        public static void Show(string uiInfo, object param = null)
        {
            if (string.IsNullOrEmpty(uiInfo))
            {
                return;
            }

            if (!_uiList.ContainsKey(uiInfo))
            {
                Debug.LogError($"not define ui:{uiInfo}");
                return;
            }

            GameObject ui = null;
            if (!_uiMap.ContainsKey(uiInfo))
            {
                Object obj = Resources.Load(_uiList[uiInfo]);
                if (obj != null)
                {
                    ui = Object.Instantiate(obj) as GameObject;
                    if (ui != null)
                    {
                        ui.transform.SetParent(_uiRoot.transform);
                        ui.transform.localScale = Vector3.one;
                        ui.transform.localRotation = Quaternion.identity;
                        ui.transform.localPosition = Vector3.zero;
                        RectTransform rect = ui.GetComponent<RectTransform>();
                        rect.sizeDelta = Vector2.zero;
                    }
                }

                UIBase component = ui.GetComponent<UIBase>();
                if (component != null)
                {
                    _uiMap.Add(uiInfo, component);
                }
            }

            _uiMap[uiInfo].gameObject.SetActive(true);
            if (param != null)
            {
                UIBase component = _uiMap[uiInfo].GetComponent<UIBase>();
                if (component != null)
                {
                    component.OnEnter(param);
                }
            }
        }

        /// <summary>
        /// 隐藏ui对象。
        /// </summary>
        /// <param name="uiName">对应的ui的名称。</param>
        public static void Hide(string uiName)
        {
            if (string.IsNullOrEmpty(uiName))
            {
                return;
            }

            if (!_uiMap.TryGetValue(uiName, out var ui))
            {
                return;
            }

            ui.gameObject.SetActive(false);
            Object.DestroyImmediate(_uiMap[uiName].gameObject);
            _uiMap.Remove(uiName);
        }

        /// <summary>
        /// 获取显示的ui对象。
        /// </summary>
        /// <param name="uiName">对应的ui的名称。</param>
        /// <returns></returns>
        public static UIBase GetActiveUI(string uiName)
        {
            return _uiMap.GetValueOrDefault(uiName);
        }

        /// <summary>
        /// 隐藏所有热更相关UI。
        /// </summary>
        public static void HideAll()
        {
            foreach (var item in _uiMap)
            {
                if (item.Value && item.Value.gameObject)
                {
                    Object.Destroy(item.Value.gameObject);
                }
            }

            _uiMap.Clear();
        }

        #region 流程调用方法

        /// <summary>
        /// 显示提示框，目前最多支持三个按钮
        /// </summary>
        /// <param name="desc">描述</param>
        /// <param name="showtype">类型（MessageShowType）</param>
        /// <param name="style">StyleEnum</param>
        /// <param name="onOk">点击事件</param>
        /// <param name="onCancel">取消事件</param>
        /// <param name="onPackage">更新事件</param>
        public static void ShowMessageBox(string desc, MessageShowType showtype = MessageShowType.OneButton,
            LoadStyle.StyleEnum style = LoadStyle.StyleEnum.Style_Default,
            Action onOk = null,
            Action onCancel = null,
            Action onPackage = null)
        {
            LauncherMgr.Show(UIDefine.UILoadTip, desc);
            var ui = LauncherMgr.GetActiveUI(UIDefine.UILoadTip) as UILoadTip;
            if (ui == null)
            {
                return;
            }

            ui.OnOk = onOk;
            ui.OnCancel = onCancel;
            ui.Showtype = showtype;
            ui.OnEnter(desc);

            var loadStyleUI = ui.GetComponent<LoadStyle>();
            if (loadStyleUI)
            {
                loadStyleUI.SetStyle(style);
            }
        }

        /// <summary>
        /// 刷新UI版本号。
        /// </summary>
        /// <param name="appId">AppID。</param>
        /// <param name="resId">资源ID。</param>
        public static void RefreshVersion(string appId, string resId)
        {
            LauncherMgr.Show(UIDefine.UILoadUpdate);
            var ui = LauncherMgr.GetActiveUI(UIDefine.UILoadUpdate) as UILoadUpdate;
            if (ui == null)
            {
                return;
            }

            ui.OnRefreshVersion(appId, resId);
        }

        /// <summary>
        /// 更新UI文本。
        /// </summary>
        /// <param name="label">文本ID。</param>
        public static void UpdateUILabel(string label)
        {
            LauncherMgr.Show(UIDefine.UILoadUpdate);
            var ui = LauncherMgr.GetActiveUI(UIDefine.UILoadUpdate) as UILoadUpdate;
            if (ui == null)
            {
                return;
            }

            ui.OnEnter(label);
        }

        /// <summary>
        /// 更新UI进度。
        /// </summary>
        /// <param name="progress">当前进度。</param>
        public static void UpdateUIProgress(float progress)
        {
            LauncherMgr.Show(UIDefine.UILoadUpdate);
            var ui = LauncherMgr.GetActiveUI(UIDefine.UILoadUpdate) as UILoadUpdate;
            if (ui == null)
            {
                return;
            }

            ui.OnUpdateUIProgress(progress);
        }

        #endregion
    }
}
