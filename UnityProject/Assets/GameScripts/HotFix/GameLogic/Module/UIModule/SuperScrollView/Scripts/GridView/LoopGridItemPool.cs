using System;
using System.Collections.Generic;
using UnityEngine;


namespace SuperScrollView
{
    public class GridItemPool
    {
        GameObject mPrefabObj;
        string mPrefabName;
        int mInitCreateCount = 1;
        List<LoopGridViewItem> mTmpPooledItemList = new List<LoopGridViewItem>();
        List<LoopGridViewItem> mPooledItemList = new List<LoopGridViewItem>();
        static int mCurItemIdCount = 0;
        RectTransform mItemParent = null;
        public GridItemPool()
        {

        }
        public void Init(GameObject prefabObj, int createCount, RectTransform parent)
        {
            mPrefabObj = prefabObj;
            mPrefabName = mPrefabObj.name;
            mInitCreateCount = createCount;
            mItemParent = parent;
            mPrefabObj.SetActive(false);
            for (int i = 0; i < mInitCreateCount; ++i)
            {
                LoopGridViewItem tViewItem = CreateItem();
                RecycleItemReal(tViewItem);
            }
        }
        public LoopGridViewItem GetItem(int itemIndexForSearch)
        {
            mCurItemIdCount++;
            LoopGridViewItem tItem = null;
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
        public LoopGridViewItem CreateItem()
        {

            GameObject go = GameObject.Instantiate<GameObject>(mPrefabObj, Vector3.zero, Quaternion.identity, mItemParent);
            go.SetActive(true);
            RectTransform rf = go.GetComponent<RectTransform>();
            rf.localScale = Vector3.one;
            rf.anchoredPosition3D = Vector3.zero;
            rf.localEulerAngles = Vector3.zero;
            LoopGridViewItem tViewItem = go.GetComponent<LoopGridViewItem>();
            tViewItem.ItemPrefabName = mPrefabName;
            return tViewItem;
        }
        void RecycleItemReal(LoopGridViewItem item)
        {
            item.gameObject.SetActive(false);
            mPooledItemList.Add(item);
        }
        public void RecycleItem(LoopGridViewItem item)
        {
            item.PrevItem = null;
            item.NextItem = null;
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
