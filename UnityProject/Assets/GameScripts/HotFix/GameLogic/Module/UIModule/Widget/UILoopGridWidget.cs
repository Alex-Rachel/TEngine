using System;
using System.Collections.Generic;
using SuperScrollView;
using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// UI列表。
    /// </summary>
    public class UILoopGridWidget<TItem, TData> : UIListBase<TItem, TData> where TItem : UILoopGridItemWidget, new()
    {
        /// <summary>
        /// LoopRectView
        /// </summary>
        public LoopGridView LoopRectView { private set; get; }

        /// <summary>
        /// Item字典
        /// </summary>
        private Dictionary<int, TItem> m_itemCache = new Dictionary<int, TItem>();

        /// <summary>
        /// 计算偏差后的ItemList
        /// </summary>
        private List<TItem> m_items = new List<TItem>();

        /// <summary>
        /// 计算偏差后的ItemList
        /// </summary>
        public List<TItem> items => m_items;

        protected override void BindMemberProperty()
        {
            base.BindMemberProperty();
            LoopRectView = rectTransform.GetComponent<LoopGridView>();
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            LoopRectView.InitGridView(0, OnGetItemByIndex);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            m_itemCache.Clear();
        }

        /// <summary>
        /// Item回调函数
        /// </summary>
        protected Action<TItem, int> m_tpFuncItem;

        /// <summary>
        /// 设置显示数据
        /// </summary>
        /// <param name="n"></param>
        /// <param name="datas"></param>
        /// <param name="funcItem"></param>
        protected override void AdjustItemNum(int n, List<TData> datas = null, Action<TItem, int> funcItem = null)
        {
            base.AdjustItemNum(n, datas, funcItem);
            m_tpFuncItem = funcItem;
            LoopRectView.SetListItemCount(n);
            LoopRectView.RefreshAllShownItem();
            m_tpFuncItem = null;
        }

        /// <summary>
        /// 获取Item
        /// </summary>
        /// <param name="listView"></param>
        /// <param name="itemIndex"></param>
        /// <param name="row"></param>
        /// <param name="column"></param>
        /// <returns></returns>
        protected LoopGridViewItem OnGetItemByIndex(LoopGridView listView, int itemIndex, int row, int column)
        {
            if (itemIndex < 0 || itemIndex >= Num) return null;
            var item = BaseItemPrefab == null ? CreateItem() : CreateItem(BaseItemPrefab);
            if (item == null) return null;
            item.SetItemIndex(itemIndex);
            UpdateListItem(item, itemIndex, m_tpFuncItem);
            return item.LoopItem;
        }

        /// <summary>
        /// 创建Item
        /// </summary>
        /// <returns></returns>
        public TItem CreateItem() => CreateItem(typeof(TItem).Name);

        /// <summary>
        /// 创建Item
        /// </summary>
        /// <param name="itemName"></param>
        /// <returns></returns>
        public TItem CreateItem(string itemName)
        {
            TItem widget = null;
            LoopGridViewItem item = LoopRectView.NewListViewItem(itemName);

            if (item != null)
            {
                widget = CreateItem(item);
            }

            return widget;
        }

        /// <summary>
        /// 创建Item
        /// </summary>
        /// <param name="prefab"></param>
        /// <returns></returns>
        public TItem CreateItem(GameObject prefab)
        {
            if(prefab == null)
            {
                return null;
            }
            TItem widget = null;
            var item = LoopRectView.NewListViewItem(prefab);
            if (item != null)
            {
                widget = CreateItem(item);
            }

            return widget;
        }

        /// <summary>
        /// 创建Item
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        private TItem CreateItem(LoopGridViewItem item)
        {
            if (!m_itemCache.TryGetValue(item.GoId, out var widget))
            {
                widget = CreateWidget<TItem>(item.gameObject);
                widget.LoopItem = item;
                m_itemCache.Add(item.GoId, widget);
            }

            return widget;
        }

        /// <summary>
        /// 获取item
        /// </summary>
        /// <param name="i"></param>
        /// <returns></returns>
        public override TItem GetItem(int i) => i >= 0 && i < m_items.Count ? m_items[i] : null;

        /// <summary>
        /// 获取itemList
        /// </summary>
        /// <returns></returns>
        public List<TItem> GetItemList()
        {
            m_items.Clear();
            for (int i = 0; i < m_itemCache.Count; i++)
            {
                m_items.Add(m_itemCache[i]);
            }
            return m_items;
        }
    }
}