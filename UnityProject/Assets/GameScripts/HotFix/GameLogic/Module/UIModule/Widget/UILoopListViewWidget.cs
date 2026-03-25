using System.Collections.Generic;
using SuperScrollView;
using UnityEngine;

namespace GameLogic
{
    public class UILoopListViewWidget<T> : UIWidget where T : UILoopItemWidget, new()
    {
        /// <summary>
        /// LoopListView组件
        /// </summary>
        public LoopListView2 LoopRectView { private set; get; }

        private Dictionary<int, T> m_itemCache = new Dictionary<int, T>();

        protected override void BindMemberProperty()
        {
            base.BindMemberProperty();
            LoopRectView = this.rectTransform.GetComponent<LoopListView2>();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            m_itemCache.Clear();
        }

        /// <summary>
        /// 创建Item
        /// </summary>
        public T CreateItem()
        {
            string typeName = typeof(T).Name;
            return CreateItem(typeName);
        }

        /// <summary>
        /// 创建Item
        /// </summary>
        /// <param name="itemName">Item名称</param>
        public T CreateItem(string itemName)
        {
            T widget = null;
            var item = LoopRectView.NewListViewItem(itemName);
            if (item != null)
            {
                widget = CreateItem(item);
            }

            return widget;
        }

        /// <summary>
        /// 创建Item
        /// </summary>
        /// <param name="prefab">预制体</param>
        public T CreateItem(GameObject prefab)
        {
            if(prefab == null)
            {
                return null;
            }
            T widget = null;
            var item = LoopRectView.NewListViewItem(prefab);
            if (item != null)
            {
                widget = CreateItem(item);
            }

            return widget;
        }

        private T CreateItem(LoopListViewItem2 item)
        {
            if (!m_itemCache.TryGetValue(item.GoId, out var widget))
            {
                widget = CreateWidget<T>(item.gameObject);
                widget.LoopItem = item;
                m_itemCache.Add(item.GoId, widget);
            }

            return widget;
        }

        /// <summary>
        /// 获取所有Item列表
        /// </summary>
        public List<T> GetItemList()
        {
            List<T> list = new List<T>();
            for (int i = 0; i < m_itemCache.Count; i++)
            {
                list.Add(m_itemCache[i]);
            }

            return list;
        }

        /// <summary>
        /// 获取Item数量
        /// </summary>
        public int GetItemCount() => m_itemCache.Count;

        /// <summary>
        /// 根据GoID获取Item
        /// </summary>
        public T GetItem(int goID) => m_itemCache[goID];

        /// <summary>
        /// 获取Item。
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public T GetItemByIndex(int index) => m_itemCache[index];
    }
}