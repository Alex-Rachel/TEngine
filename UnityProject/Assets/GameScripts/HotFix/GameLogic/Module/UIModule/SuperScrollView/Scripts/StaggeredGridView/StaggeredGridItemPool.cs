using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace SuperScrollView
{

    public class StaggeredGridItemPool
    {
        GameObject mPrefabObj;
        string mPrefabName;
        int mInitCreateCount = 1;
        float mPadding = 0;
        List<LoopStaggeredGridViewItem> mTmpPooledItemList = new List<LoopStaggeredGridViewItem>();
        List<LoopStaggeredGridViewItem> mPooledItemList = new List<LoopStaggeredGridViewItem>();
        static int mCurItemIdCount = 0;
        RectTransform mItemParent = null;
        public StaggeredGridItemPool()
        {

        }
        public void Init(GameObject prefabObj, float padding, int createCount, RectTransform parent)
        {
            mPrefabObj = prefabObj;
            mPrefabName = mPrefabObj.name;
            mInitCreateCount = createCount;
            mPadding = padding;
            mItemParent = parent;
            mPrefabObj.SetActive(false);
            for (int i = 0; i < mInitCreateCount; ++i)
            {
                LoopStaggeredGridViewItem tViewItem = CreateItem();
                RecycleItemReal(tViewItem);
            }
        }
        public LoopStaggeredGridViewItem GetItem(int itemIndexForSearch)
        {
            mCurItemIdCount++;
            LoopStaggeredGridViewItem tItem = null;
            if (mTmpPooledItemList.Count > 0)
            {
                int count = mTmpPooledItemList.Count;
                for (int i = 0; i < count; ++i)
                {
                    if (mTmpPooledItemList[i].ItemIndex == itemIndexForSearch)
                    {
                        tItem = mTmpPooledItemList[i];
                        mTmpPooledItemList.RemoveAt(i);
                        break;
                    }
                }
                if(tItem == null)
                {
                    tItem = mTmpPooledItemList[count - 1];
                    mTmpPooledItemList.RemoveAt(count - 1);
                }
                tItem.gameObject.SetActive(true);
            }
            else
            {
                int count = mPooledItemList.Count;
                if (count == 0)
                {
                    tItem = CreateItem();
                }
                else
                {
                    tItem = mPooledItemList[count - 1];
                    mPooledItemList.RemoveAt(count - 1);
                    tItem.gameObject.SetActive(true);
                }
            }
            tItem.Padding = mPadding;
            tItem.ItemId = mCurItemIdCount;
            return tItem;

        }

        public void DestroyAllItem()
        {
            ClearTmpRecycledItem();
            int count = mPooledItemList.Count;
            for (int i = 0; i < count; ++i)
            {
                GameObject.DestroyImmediate(mPooledItemList[i].gameObject);
            }
            mPooledItemList.Clear();
        }
        public LoopStaggeredGridViewItem CreateItem()
        {

            GameObject go = GameObject.Instantiate<GameObject>(mPrefabObj, Vector3.zero, Quaternion.identity, mItemParent);
            go.SetActive(true);
            RectTransform rf = go.GetComponent<RectTransform>();
            rf.localScale = Vector3.one;
            rf.anchoredPosition3D = Vector3.zero;
            rf.localEulerAngles = Vector3.zero;
            LoopStaggeredGridViewItem tViewItem = go.GetComponent<LoopStaggeredGridViewItem>();
            tViewItem.ItemPrefabName = mPrefabName;
            tViewItem.StartPosOffset = 0;
            return tViewItem;
        }
        void RecycleItemReal(LoopStaggeredGridViewItem item)
        {
            item.gameObject.SetActive(false);
            mPooledItemList.Add(item);
        }
        public void RecycleItem(LoopStaggeredGridViewItem item)
        {
            mTmpPooledItemList.Add(item);
        }
        public void ClearTmpRecycledItem()
        {
            int count = mTmpPooledItemList.Count;
            if (count == 0)
            {
                return;
            }
            for (int i = 0; i < count; ++i)
            {
                RecycleItemReal(mTmpPooledItemList[i]);
            }
            mTmpPooledItemList.Clear();
        }
    }


}
