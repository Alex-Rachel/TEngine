using System;
using System.Collections.Generic;
using Fantasy;
using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// 页签切换管理器，用于管理 Tab 按钮和对应子页面的切换逻辑。
    /// </summary>
    public sealed class SwitchPageMgr
    {
        private class TabPageInfo
        {
            public string TabName { get; set; }
            public HashSet<Type> PageTypes { get; set; } = new HashSet<Type>(8);
            public GameObject ExistPage { get; set; }
            public SwitchTabItem TabItem { get; set; }
        }

        /// <summary>
        /// oldTabIndex newTabIndex
        /// </summary>
        private event Action<int, int> m_switchTabAction;

        // tab按钮父节点
        private readonly Transform m_tfTabParent;

        /// <summary>
        /// 获取子页面父节点 Transform。
        /// </summary>
        public Transform TfChildPageParent { get; private set; }

        private readonly Dictionary<int, TabPageInfo> m_tabPageInfoDict = new Dictionary<int, TabPageInfo>(8);
        private readonly Dictionary<Type, BaseChildPage> m_childPageDict = new Dictionary<Type, BaseChildPage>(8);
        private readonly List<int> m_idList = new List<int>(8);
        private readonly UIWindow m_parentWindow;
        private int m_curSelectChildID = -100;
        private readonly ChildPageShareData m_shareData = new ChildPageShareData();

        /// <summary>
        /// 获取共享数据 1。
        /// </summary>
        public object ShareData1 => m_shareData?.ShareData1;

        /// <summary>
        /// 获取共享数据 2。
        /// </summary>
        public object ShareData2 => m_shareData?.ShareData2;

        /// <summary>
        /// 获取共享数据 3。
        /// </summary>
        public object ShareData3 => m_shareData?.ShareData3;

        /// <summary>
        /// 获取 Tab 总数。
        /// </summary>
        public int TabCount => m_tabPageInfoDict.Count;

        /// <summary>
        /// 初始化页签切换管理器。
        /// </summary>
        /// <param name="tfTabParent">Tab 按钮的父节点</param>
        /// <param name="tfChildPageParent">子页面的父节点</param>
        /// <param name="parentWindow">父窗口</param>
        public SwitchPageMgr(Transform tfTabParent, Transform tfChildPageParent, UIWindow parentWindow)
        {
            m_tfTabParent = tfTabParent;
            m_parentWindow = parentWindow;
            TfChildPageParent = tfChildPageParent;
        }

        /// <summary>
        /// 添加 Tab 切换事件监听。
        /// </summary>
        /// <param name="switchTabAction">切换事件回调，参数为 (旧 Tab ID, 新 Tab ID)</param>
        public void AddSwitchAction(Action<int, int> switchTabAction)
        {
            m_switchTabAction += switchTabAction;
        }

        /// <summary>
        /// 移除 Tab 切换事件监听。
        /// </summary>
        /// <param name="switchTabAction">要移除的切换事件回调</param>
        public void RemoveSwitchAction(Action<int, int> switchTabAction)
        {
            m_switchTabAction -= switchTabAction;
        }

        /// <summary>
        /// 绑定子页面类型到指定 Tab ID。
        /// </summary>
        /// <typeparam name="T">子页面类型</typeparam>
        /// <param name="tabID">Tab ID</param>
        public void BindChildPage<T>(int tabID) where T : BaseChildPage, new()
        {
            BindChildPage<T>(tabID, string.Empty);
        }

        /// <summary>
        /// 绑定子页面类型到指定 Tab ID。
        /// </summary>
        /// <typeparam name="T">子页面类型</typeparam>
        /// <param name="tabID">Tab ID</param>
        /// <param name="tabName">Tab 名称</param>
        /// <param name="goPage">已存在的页面 GameObject（可选）</param>
        public void BindChildPage<T>(int tabID, string tabName, GameObject goPage = null) where T : BaseChildPage, new()
        {
            var pageType = typeof(T);

            if (!m_idList.Contains(tabID))
            {
                m_idList.Add(tabID);
            }

            if (!m_childPageDict.ContainsKey(pageType))
            {
                m_childPageDict.Add(pageType, new T());
            }

            if (!m_tabPageInfoDict.TryGetValue(tabID, out var tabInfo))
            {
                tabInfo = new TabPageInfo();
                m_tabPageInfoDict[tabID] = tabInfo;
            }

            tabInfo.TabName = tabName;
            if (!tabInfo.PageTypes.Contains(pageType))
            {
                tabInfo.PageTypes.Add(pageType);
            }

            if (goPage != null)
            {
                tabInfo.ExistPage = goPage;
            }
        }

        /// <summary>
        /// 创建所有已绑定页面的 Tab。
        /// </summary>
        /// <typeparam name="T">Tab 类型</typeparam>
        /// <param name="tabID">默认选中的 Tab ID</param>
        /// <param name="tabTemp">Tab 预制体（可选）</param>
        /// <param name="needSwitch">是否切换到指定 Tab</param>
        public void CreateTab<T>(int tabID, GameObject tabTemp = null, bool needSwitch = true) where T : SwitchTabItem, new()
        {
            InternalCreateTab<T>(tabID, tabTemp, needSwitch, false);
        }

        /// <summary>
        /// 创建单个 Tab。
        /// </summary>
        /// <typeparam name="T">Tab 类型</typeparam>
        /// <param name="tabID">Tab ID</param>
        /// <param name="tabTemp">Tab 预制体（可选）</param>
        /// <param name="needSwitch">是否切换到指定 Tab</param>
        public void CreateSingleTab<T>(int tabID, GameObject tabTemp = null, bool needSwitch = true) where T : SwitchTabItem, new()
        {
            if (!m_tabPageInfoDict.TryGetValue(tabID, out var tabInfo))
            {
                return;
            }

            if (tabInfo.TabItem == null)
            {
                T tab = tabTemp != null
                    ? m_parentWindow.CreateWidgetByPrefab<T>(tabTemp, m_tfTabParent)
                    : m_parentWindow.CreateWidgetByType<T>(m_tfTabParent);

                tab.UpdateTabName(tabInfo.TabName);
                tab.BindClickEvent(OnTabClick, tabID);
                tab.SetSelectedState(m_curSelectChildID == tabID);
                tabInfo.TabItem = tab;
            }

            if (needSwitch)
            {
                SwitchPage(tabID);
            }
        }

        /// <summary>
        /// 通过预制体创建 Tab。
        /// </summary>
        /// <typeparam name="T">Tab 类型</typeparam>
        /// <param name="tabID">默认选中的 Tab ID</param>
        /// <param name="tabTemp">Tab 预制体</param>
        /// <param name="needSwitch">是否切换到指定 Tab</param>
        /// <param name="setSizeDelta">是否自动调整大小</param>
        public void CreateTabByPrefab<T>(int tabID, GameObject tabTemp, bool needSwitch = true, bool setSizeDelta = true) where T : SwitchTabItem, new()
        {
            if (tabTemp == null)
            {
                Log.Error("CreatTabByPrefab failed, prefab is null: {0}", typeof(T).Name);
                return;
            }

            InternalCreateTab<T>(tabID, tabTemp, needSwitch, setSizeDelta, false);
        }

        /// <summary>
        /// 通过类型创建 Tab。
        /// </summary>
        /// <typeparam name="T">Tab 类型</typeparam>
        /// <param name="tabID">默认选中的 Tab ID</param>
        /// <param name="needSwitch">是否切换到指定 Tab</param>
        /// <param name="setSizeDelta">是否自动调整大小</param>
        public void CreateTabByType<T>(int tabID, bool needSwitch = true, bool setSizeDelta = true) where T : SwitchTabItem, new()
        {
            InternalCreateTab<T>(tabID, null, needSwitch, setSizeDelta, false);
        }

        /// <summary>
        /// 通过类型创建 Tab 并执行回调。
        /// </summary>
        /// <typeparam name="T">Tab 类型</typeparam>
        /// <param name="tabID">默认选中的 Tab ID</param>
        /// <param name="action">创建完成后的回调</param>
        /// <param name="needSwitch">是否切换到指定 Tab</param>
        /// <param name="setSizeDelta">是否自动调整大小</param>
        public void CreateTabByType<T>(int tabID, Action<int, T> action, bool needSwitch = true, bool setSizeDelta = true) where T : SwitchTabItem, new()
        {
            InternalCreateTab<T>(tabID, null, needSwitch, setSizeDelta, false, action);
        }

        private void DoCreateTabByType<T>(int tabID, GameObject tabTemp, bool needSwitch = true, bool setSizeDelta = true)
            where T : SwitchTabItem, new()
        {
            InternalCreateTab<T>(tabID, tabTemp, needSwitch, setSizeDelta, false);
        }

        private void DoCreateTabByType<T>(int tabID, GameObject tabTemp, Action<int, T> action, bool needSwitch = true, bool setSizeDelta = true)
            where T : SwitchTabItem, new()
        {
            InternalCreateTab<T>(tabID, tabTemp, needSwitch, setSizeDelta, false, action);
        }

        private void InternalCreateTab<T>(int tabID,
            GameObject tabTemp = null,
            bool needSwitch = true,
            bool setSizeDelta = true,
            bool setSelectedState = true,
            Action<int, T> callback = null)
            where T : SwitchTabItem, new()
        {
            for (int i = 0; i < m_idList.Count; i++)
            {
                var childID = m_idList[i];
                if (!m_tabPageInfoDict.TryGetValue(childID, out var tabInfo))
                {
                    continue;
                }

                if (tabInfo.TabItem == null)
                {
                    T tab = tabTemp != null
                        ? m_parentWindow.CreateWidgetByPrefab<T>(tabTemp, m_tfTabParent)
                        : m_parentWindow.CreateWidgetByType<T>(m_tfTabParent);

                    if (setSizeDelta)
                    {
                        tab.UpdateTabNameChangeSize(tabInfo.TabName, true);
                    }
                    else
                    {
                        tab.UpdateTabName(tabInfo.TabName);
                    }
                    tab.BindClickEvent(OnTabClick, childID);
                    if (setSelectedState)
                    {
                        tab.SetSelectedState(m_curSelectChildID == childID);
                    }
                    tabInfo.TabItem = tab;
                }
                else
                {
                    if (setSizeDelta)
                    {
                        tabInfo.TabItem.UpdateTabNameChangeSize(tabInfo.TabName, true);
                    }
                    else
                    {
                        tabInfo.TabItem.UpdateTabName(tabInfo.TabName);
                    }
                    tabInfo.TabItem.BindClickEvent(OnTabClick, childID);
                    if (setSelectedState)
                    {
                        tabInfo.TabItem.SetSelectedState(m_curSelectChildID == childID);
                    }
                }
                callback?.Invoke(i, tabInfo.TabItem as T);
            }

            if (needSwitch)
            {
                SwitchPage(tabID);
            }
        }

        /// <summary>
        /// 设置自定义 Tab 点击事件。
        /// </summary>
        /// <param name="tabID">Tab ID</param>
        /// <param name="clickAction">点击事件回调</param>
        /// <param name="shareData1">共享数据 1（可选）</param>
        /// <param name="shareData2">共享数据 2（可选）</param>
        /// <param name="shareData3">共享数据 3（可选）</param>
        public void SetCustomTabClickAction(int tabID, Action<SwitchTabItem> clickAction, object shareData1 = null,
            object shareData2 = null, object shareData3 = null)
        {
            if (m_tabPageInfoDict.TryGetValue(tabID, out var tabInfo))
            {
                tabInfo.TabItem?.BindClickEvent(clickAction, shareData1, shareData2, shareData3);
            }
        }

        /// <summary>
        /// 切换到指定 Tab 对应的页面。
        /// </summary>
        /// <param name="tabID">要切换到的 Tab ID</param>
        public void SwitchPage(int tabID)
        {
            if (m_curSelectChildID != tabID && m_tabPageInfoDict.TryGetValue(tabID, out var curTabInfo))
            {
                var pages = curTabInfo.PageTypes;
                if (pages != null)
                {
                    foreach (var pageType in pages)
                    {
                        var page = GetChildPageByType(pageType);
                        if (page != null && page.gameObject == null)
                        {
                            if (curTabInfo.ExistPage != null)
                            {
                                page.Create(m_parentWindow, curTabInfo.ExistPage, TfChildPageParent);
                                page.Init(m_shareData, this);
                            }
                            else
                            {
                                page.CreateByPath(pageType.Name, m_parentWindow, TfChildPageParent);
                                page.Init(m_shareData, this);
                            }
                        }
                    }
                }

                foreach (var item in m_childPageDict)
                {
                    var pageType = item.Key;
                    var page = item.Value;
                    bool beShow = curTabInfo.PageTypes.Contains(pageType);

                    if (page != null && page.gameObject != null)
                    {
                        page.Visible = beShow;

                        if (beShow)
                        {
                            page.OnPageShowed(m_curSelectChildID, tabID);
                        }
                    }
                }


                // 设置tab的状态
                for (var index = 0; index < m_idList.Count; index++)
                {
                    var childID = m_idList[index];

                    if (m_tabPageInfoDict.TryGetValue(childID, out var tabInfo))
                    {
                        tabInfo.TabItem?.SetSelectedState(tabID == childID);
                    }
                }
            }

            // 如果相同值则不触发切换回调
            var oldID = m_curSelectChildID;
            m_curSelectChildID = tabID;

            if (oldID != tabID)
            {
                m_switchTabAction?.Invoke(oldID, tabID);
            }
        }

        private void OnTabClick(SwitchTabItem tabItem)
        {
            var tabID = (int)tabItem.EventParam1;
            SwitchPage(tabID);
        }

        /// <summary>
        /// 根据类型获取子页面。
        /// </summary>
        /// <param name="pageType">页面类型</param>
        /// <returns>子页面实例</returns>
        public BaseChildPage GetChildPageByType(Type pageType)
            => m_childPageDict.GetValueOrDefault(pageType);

        /// <summary>
        /// 刷新当前显示的子页面。
        /// </summary>
        public void RefreshCurrentChildPage()
        {
            if (m_tabPageInfoDict.TryGetValue(m_curSelectChildID, out var curTabInfo))
            {
                foreach (var pageType in curTabInfo.PageTypes)
                {
                    var page = GetChildPageByType(pageType);
                    if (page != null && page.gameObject != null)
                    {
                        page.RefreshCurrentChildPage();
                    }
                }
            }
        }

        /// <summary>
        /// 刷新指定 Tab ID 对应的子页面。
        /// </summary>
        /// <param name="tabID">Tab ID</param>
        public void RefreshChildPage(int tabID)
        {
            if (m_tabPageInfoDict.TryGetValue(tabID, out var curTabInfo))
            {
                foreach (var pageType in curTabInfo.PageTypes)
                {
                    var page = GetChildPageByType(pageType);
                    if (page != null && page.gameObject != null)
                    {
                        page.RefreshCurrentChildPage();
                    }
                }
            }
        }

        /// <summary>
        /// 尝试获取指定类型的子页面。
        /// </summary>
        /// <typeparam name="T">子页面类型</typeparam>
        /// <param name="page">输出子页面实例</param>
        /// <returns>是否成功获取</returns>
        public bool TryGetChildPage<T>(out T page) where T : BaseChildPage
        {
            page = GetChildPage<T>();
            return page != null;
        }

        /// <summary>
        /// 获取指定类型的子页面。
        /// </summary>
        /// <typeparam name="T">子页面类型</typeparam>
        /// <returns>子页面实例</returns>
        public T GetChildPage<T>() where T : BaseChildPage
        {
            return m_childPageDict.TryGetValue(typeof(T), out var page) ? page as T : null;
        }

        /// <summary>
        /// 判断是否包含指定 Tab ID 的子页面。
        /// </summary>
        /// <param name="tabID">Tab ID</param>
        /// <returns>是否包含</returns>
        public bool ContainsChildPage(int tabID) => m_idList.Contains(tabID);

        /// <summary>
        /// 判断是否包含指定 Tab ID。
        /// </summary>
        /// <param name="tabID">Tab ID</param>
        /// <returns>是否包含</returns>
        public bool ContainsTab(int tabID) => m_idList.Contains(tabID);

        /// <summary>
        /// 获取当前显示的 Tab ID。
        /// </summary>
        /// <returns>当前 Tab ID</returns>
        public int GetCurrentShowTabID() => m_curSelectChildID;

        /// <summary>
        /// 设置共享数据。
        /// </summary>
        /// <param name="shareDataIndex">共享数据索引 (1-3)</param>
        /// <param name="shareData">共享数据</param>
        public void SetShareData(int shareDataIndex, object shareData)
            => m_shareData.SetShareData(shareDataIndex, shareData);

        /// <summary>
        /// 根据 Tab ID 获取对应的子页面。
        /// </summary>
        /// <typeparam name="T">子页面类型</typeparam>
        /// <param name="tabID">Tab ID</param>
        /// <returns>子页面实例</returns>
        public T GetChildPageByTabID<T>(int tabID) where T : BaseChildPage
        {
            if (m_tabPageInfoDict.TryGetValue(tabID, out var tabInfo))
            {
                foreach (var pageType in tabInfo.PageTypes)
                {
                    var page = GetChildPageByType(pageType);

                    if (page != null)
                    {
                        return page as T;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 绑定多个子页面类型到指定 Tab ID（2个）。
        /// </summary>
        /// <typeparam name="T">第一个子页面类型</typeparam>
        /// <typeparam name="U">第二个子页面类型</typeparam>
        /// <param name="tabID">Tab ID</param>
        /// <param name="tabName">Tab 名称</param>
        public void BindChildPage<T, U>(int tabID, string tabName)
            where T : BaseChildPage, new()
            where U : BaseChildPage, new()
        {
            BindChildPage<T>(tabID, tabName);
            BindChildPage<U>(tabID, tabName);
        }


        /// <summary>
        /// 绑定多个子页面类型到指定 Tab ID（3个）。
        /// </summary>
        /// <typeparam name="T">第一个子页面类型</typeparam>
        /// <typeparam name="U">第二个子页面类型</typeparam>
        /// <typeparam name="V">第三个子页面类型</typeparam>
        /// <param name="tabID">Tab ID</param>
        /// <param name="tabName">Tab 名称</param>
        public void BindChildPage<T, U, V>(int tabID, string tabName)
            where T : BaseChildPage, new()
            where U : BaseChildPage, new()
            where V : BaseChildPage, new()
        {
            BindChildPage<T>(tabID, tabName);
            BindChildPage<U>(tabID, tabName);
            BindChildPage<V>(tabID, tabName);
        }

        /// <summary>
        /// 绑定多个子页面类型到指定 Tab ID（4个）。
        /// </summary>
        /// <typeparam name="T">第一个子页面类型</typeparam>
        /// <typeparam name="U">第二个子页面类型</typeparam>
        /// <typeparam name="V">第三个子页面类型</typeparam>
        /// <typeparam name="W">第四个子页面类型</typeparam>
        /// <param name="tabID">Tab ID</param>
        /// <param name="tabName">Tab 名称</param>
        public void BindChildPage<T, U, V, W>(int tabID, string tabName)
            where T : BaseChildPage, new()
            where U : BaseChildPage, new()
            where V : BaseChildPage, new()
            where W : BaseChildPage, new()
        {
            BindChildPage<T>(tabID, tabName);
            BindChildPage<U>(tabID, tabName);
            BindChildPage<V>(tabID, tabName);
            BindChildPage<W>(tabID, tabName);
        }

        /// <summary>
        /// 获取父窗口。
        /// </summary>
        /// <returns>父窗口实例</returns>
        public UIWindow GetParentWindow() => m_parentWindow;

        // public void DestroyParentWindow() => m_parentWindow?.Destroy();

        /// <summary>
        /// 设置 Tab 红点显示状态。
        /// </summary>
        /// <param name="tabID">Tab ID</param>
        /// <param name="isShow">是否显示红点</param>
        public void SetTabRedNode(int tabID, bool isShow)
        {
            if (m_tabPageInfoDict.TryGetValue(tabID, out var tabInfo))
            {
                tabInfo.TabItem?.SetRedNodeActive(isShow);
            }
        }

        /// <summary>
        /// 设置 Tab 图标。
        /// </summary>
        /// <param name="tabID">Tab ID</param>
        /// <param name="selectIconPath">选中状态图标路径</param>
        /// <param name="noSelectIconPath">未选中状态图标路径</param>
        public void SetTabIcon(int tabID, string selectIconPath, string noSelectIconPath)
        {
            if (m_tabPageInfoDict.TryGetValue(tabID, out var tabInfo))
            {
                tabInfo.TabItem?.SetTabIcon(selectIconPath, noSelectIconPath);
            }
        }

        /// <summary>
        /// 设置 Tab 图标位置。
        /// </summary>
        /// <param name="tabID">Tab ID</param>
        /// <param name="selectedIconPos">选中状态图标位置</param>
        /// <param name="noSelectIconPos">未选中状态图标位置</param>
        public void SetTabIconPos(int tabID, Vector2 selectedIconPos, Vector2 noSelectIconPos)
        {
            if (m_tabPageInfoDict.TryGetValue(tabID, out var tabInfo))
            {
                tabInfo.TabItem?.SetTabIconPos(selectedIconPos, noSelectIconPos);
            }
        }

        /// <summary>
        /// 设置 Tab 文本字体大小。
        /// </summary>
        /// <param name="tabID">Tab ID</param>
        /// <param name="fontSize">字体大小</param>
        public void SetTabTextFontSize(int tabID, int fontSize)
        {
            if (m_tabPageInfoDict.TryGetValue(tabID, out var tabInfo))
            {
                tabInfo.TabItem?.SetTabTextFontSize(fontSize);
            }
        }

        /// <summary>
        /// 设置 Tab 背景图。
        /// </summary>
        /// <param name="tabID">Tab ID</param>
        /// <param name="selectBgPath">选中状态背景路径</param>
        /// <param name="noSelectBgPath">未选中状态背景路径</param>
        public void SetTabBg(int tabID, string selectBgPath, string noSelectBgPath)
        {
            if (m_tabPageInfoDict.TryGetValue(tabID, out var tabInfo))
            {
                tabInfo.TabItem?.SetTabBg(selectBgPath, noSelectBgPath);
            }
        }

        /// <summary>
        /// 设置所有 Tab 的背景图。
        /// </summary>
        /// <param name="selectBgPath">选中状态背景路径</param>
        /// <param name="noSelectBgPath">未选中状态背景路径</param>
        public void SetAllTabBg(string selectBgPath, string noSelectBgPath)
        {
            foreach (var tabInfo in m_tabPageInfoDict.Values)
            {
                tabInfo.TabItem?.SetTabBg(selectBgPath, noSelectBgPath);
            }
        }

        /// <summary>
        /// 设置 Tab 名称。
        /// </summary>
        /// <param name="tabID">Tab ID</param>
        /// <param name="tabName">Tab 名称</param>
        public void SetTabName(int tabID, string tabName)
        {
            if (m_tabPageInfoDict.TryGetValue(tabID, out var tabInfo))
            {
                tabInfo.TabItem?.UpdateTabName(tabName);
            }
        }

        /// <summary>
        /// 设置 Tab 文本颜色。
        /// </summary>
        /// <param name="tabID">Tab ID</param>
        /// <param name="selectedColor">选中状态颜色（十六进制）</param>
        /// <param name="noSelectColor">未选中状态颜色（十六进制）</param>
        public void SetTabTextColor(int tabID, Color selectedColor, Color noSelectColor)
        {
            if (m_tabPageInfoDict.TryGetValue(tabID, out var tabInfo))
            {
                tabInfo.TabItem?.SetTabTextColor(selectedColor, noSelectColor);
            }
        }

        /// <summary>
        /// 设置所有 Tab 的文本颜色。
        /// </summary>
        /// <param name="selectedColor">选中状态颜色（十六进制）</param>
        /// <param name="noSelectColor">未选中状态颜色（十六进制）</param>
        public void SetAllTabTextColor(Color selectedColor, Color noSelectColor)
        {
            foreach (var tabInfo in m_tabPageInfoDict.Values)
            {
                tabInfo.TabItem?.SetTabTextColor(selectedColor, noSelectColor);
            }
        }

        /// <summary>
        /// 根据 Tab ID 获取 Tab 项。
        /// </summary>
        /// <param name="tabID">Tab ID</param>
        /// <returns>Tab 项实例</returns>
        public SwitchTabItem GetTabByID(int tabID)
        {
            return m_tabPageInfoDict.TryGetValue(tabID, out var tabInfo) ? tabInfo.TabItem : null;
        }

        #region 页签从前到后排序

        private List<int> m_idListTemp = new List<int>();

        /// <summary>
        /// 对 Tab 进行排序（按 ID 从小到大）。
        /// </summary>
        public void SortTab()
        {
            m_idListTemp.Clear();
            m_idListTemp.AddRange(m_idList);
            m_idListTemp.Sort(OnSortTab);

            foreach (var item in m_tabPageInfoDict)
            {
                var tabID = item.Key;
                var tabItem = item.Value.TabItem;
                tabItem?.transform?.SetSiblingIndex(m_idListTemp.IndexOf(tabID));
            }
        }

        private int OnSortTab(int l, int r) => l.CompareTo(r);

        #endregion
    }
}