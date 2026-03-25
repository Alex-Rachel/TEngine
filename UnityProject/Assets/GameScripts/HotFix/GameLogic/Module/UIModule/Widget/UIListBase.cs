using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>
    /// UI列表Item
    /// </summary>
    /// <typeparam name="TData"></typeparam>
    public interface IListDataItem<in TData>
    {
        void SetItemData(TData d);
    }

    /// <summary>
    /// UI列表Item
    /// </summary>
    public interface IListSelectItem
    {
        /// <summary>
        /// 获取索引。
        /// </summary>
        /// <returns></returns>
        int GetItemIndex();

        /// <summary>
        /// 设置索引。
        /// </summary>
        /// <param name="i"></param>
        void SetItemIndex(int i);

        /// <summary>
        /// 是否被选中。
        /// </summary>
        /// <returns></returns>
        bool IsSelected();

        /// <summary>
        /// 设置是否选中。
        /// </summary>
        /// <param name="v"></param>
        void SetSelected(bool v);
    }

    public class SelectItemBase : UIEventItem<SelectItemBase>, IListSelectItem
    {
        /// <summary>
        /// 索引。
        /// </summary>
        protected int m_itemIndex;

        /// <summary>
        /// 获取索引
        /// </summary>
        public int GetItemIndex() => m_itemIndex;

        /// <summary>
        /// 设置索引
        /// </summary>
        public void SetItemIndex(int i) => m_itemIndex = i;

        /// <summary>
        /// 是否被选中。
        /// </summary>
        protected bool m_isSelected;

        /// <summary>
        /// 获取是否被选中
        /// </summary>
        public virtual bool IsSelected() => m_isSelected;

        /// <summary>
        /// 设置是否选中
        /// </summary>
        public virtual void SetSelected(bool v)
        {
            m_isSelected = v;
            UpdateSelectState();
        }

        /// <summary>
        /// 刷新选中状态。
        /// </summary>
        public virtual void UpdateSelectState()
        {
        }

        protected override void RegisterEvent()
        {
            base.RegisterEvent();
            AddSelectEvt();
        }

        /// <summary>
        /// 监听选中事件。
        /// </summary>
        protected virtual void AddSelectEvt()
        {
            if (Parent == null || Parent is not IUISelectList)
            {
                return;
            }
            var btn = TEngine.DUnityUtil.AddMonoBehaviour<Button>(gameObject);
            if (btn != null)
            {
                btn.onClick.AddListener(OnSelectClick);
            }
        }

        /// <summary>
        /// 选中点击
        /// </summary>
        protected virtual void OnSelectClick()
        {
            if (Parent is IUISelectList parent)
            {
                parent.OnItemClick(this, GetItemIndex());
            }
        }
    }

    interface IUISelectList
    {
        void OnItemClick(object item, int i);
    }

    /// <summary>
    /// UI列表
    /// </summary>
    public class UIListBase<TItem, TData> : UIWidget, IUISelectList where TItem : UIWidget, new()
    {
        /// <summary>
        /// item模板
        /// </summary>
        public GameObject BaseItemPrefab;

        /// <summary>
        /// 数据列表
        /// </summary>
        protected List<TData> m_datas;

        /// <summary>
        /// 数据列表
        /// </summary>
        public List<TData> Datas => m_datas;

        /// <summary>
        /// 数据数量
        /// </summary>
        public int DataNum => m_datas != null ? m_datas.Count : 0;

        /// <summary>
        /// 数量
        /// </summary>
        protected int m_num;

        /// <summary>
        /// 数量
        /// </summary>
        public int Num => m_num;

        /// <summary>
        /// 设置数据数量
        /// </summary>
        /// <param name="n"></param>
        /// <param name="funcItem"></param>
        public void SetDataNum(int n, Action<TItem, int> funcItem = null)
        {
            AdjustItemNum(n, null, funcItem);
        }

        /// <summary>
        /// 数据起始索引
        /// </summary>
        public int DataStartOffset = 0;

        /// <summary>
        /// 设置数据
        /// </summary>
        /// <param name="dataList"></param>
        /// <param name="n"></param>
        public void SetDatas(List<TData> dataList, int n = -1)
        {
            AdjustItemNum(Mathf.Max(0, n >= 0 ? n : (dataList == null ? 0 : (dataList.Count - DataStartOffset))),
                dataList);
        }

        /// <summary>
        /// 设置显示数据
        /// </summary>
        /// <param name="n"></param>
        /// <param name="datas"></param>
        /// <param name="funcItem"></param>
        protected virtual void AdjustItemNum(int n, List<TData> datas = null, Action<TItem, int> funcItem = null)
        {
            m_num = n;
            m_datas = datas;
            if (BaseItemPrefab != null)
            {
                BaseItemPrefab.SetActive(false);
            }
        }

        /// <summary>
        /// 刷新列表ITEM
        /// </summary>
        /// <param name="item"></param>
        /// <param name="i"></param>
        /// <param name="func"></param>
        protected virtual void UpdateListItem(TItem item, int i, Action<TItem, int> func)
        {
            if (item == null)
            {
                return;
            }
            if (func != null)
            {
                func.Invoke(item, i);
                return;
            }

            if (item is IListDataItem<TData> listDataItem)
            {
                listDataItem.SetItemData(GetData(i));
            }
        }

        /// <summary>
        /// 选中索引
        /// </summary>
        protected int m_selectIndex = -1;

        /// <summary>
        /// Item点击
        /// </summary>
        public Action<int> FuncOnItemClick;

        /// <summary>
        /// 选中变化回调函数
        /// </summary>
        public Action FuncOnSelectChange;

        /// <summary>
        /// 点击无选中变化回调
        /// </summary>
        public Action FuncNoSelectChange;

        /// <summary>
        /// 选中索引
        /// </summary>
        public int SelectIndex
        {
            get => m_selectIndex;
            set => SetSelectIndex(value);
        }

        /// <summary>
        /// 设置选中索引
        /// </summary>
        /// <param name="i"></param>
        /// <param name="forceUpdate"></param>
        /// <param name="triggerEvt"></param>
        public void SetSelectIndex(int i, bool forceUpdate = false, bool triggerEvt = true)
        {
            if (!forceUpdate && m_selectIndex == i)
            {
                FuncNoSelectChange?.Invoke();
                return;
            }

            var preIndex = SelectIndex;
            m_selectIndex = i;

            if (GetItem(preIndex) is IListSelectItem item)
            {
                item.SetSelected(false);
            }

            item = GetItem(SelectIndex) as IListSelectItem;
            if (item != null)
            {
                item.SetSelected(true);
            }

            if (triggerEvt)
            {
                FuncOnSelectChange?.Invoke();
            }
        }

        /// <summary>
        /// 获取当前选中的数据
        /// </summary>
        /// <returns></returns>
        public TData GetSelectData()
        {
            return GetData(SelectIndex);
        }

        /// <summary>
        /// 获取数据
        /// </summary>
        /// <param name="i"></param>
        /// <returns></returns>
        public TData GetData(int i)
        {
            i += DataStartOffset;
            return m_datas == null || i < 0 || i >= m_datas.Count ? default(TData) : m_datas[i];
        }

        /// <summary>
        /// 获取item
        /// </summary>
        /// <param name="i"></param>
        /// <returns></returns>
        public virtual TItem GetItem(int i)
        {
            return null;
        }

        /// <summary>
        /// item被点击
        /// </summary>
        /// <param name="item"></param>
        /// <param name="i"></param>
        public void OnItemClick(object item, int i)
        {
            FuncOnItemClick?.Invoke(i);
            SelectIndex = i;
        }
    }
}