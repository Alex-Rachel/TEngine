using System;
using System.Collections.Generic;
using UnityEngine;

namespace SuperScrollView
{
    public class ItemPool
    {
        GameObject mPrefabObj;
        string mPrefabName;
        int mInitCreateCount = 1;
        float mPadding = 0;
        float mStartPosOffset = 0;
        List<LoopListViewItem2> mTmpPooledItemList = new List<LoopListViewItem2>();
        List<LoopListViewItem2> mPooledItemList = new List<LoopListViewItem2>();
        static int mCurItemIdCount = 0;
        RectTransform mItemParent = null;
        public ItemPool()
        {

        }
        public void Init(GameObject prefabObj, float padding, float startPosOffset, int createCount, RectTransform parent)
        {
            mPrefabObj = prefabObj;
            mPrefabName = mPrefabObj.name;
            mInitCreateCount = createCount;
            mPadding = padding;
            mStartPosOffset = startPosOffset;
            mItemParent = parent;
            mPrefabObj.SetActive(false);
            for (int i = 0; i < mInitCreateCount; ++i)
            {
                LoopListViewItem2 tViewItem = CreateItem();
                RecycleItemReal(tViewItem);
            }
        }
        public LoopListViewItem2 GetItem(int itemIndexForSearch)
        {
            mCurItemIdCount++;
            LoopListViewItem2 tItem = null;
            if (mTmpPooledItemList.Count > 0)
            {
                int count = mTmpPooledItemList.Count;
                for(int i = 0;i < count;++i)
                {
                    if(mTmpPooledItemList[i].ItemIndex == itemIndexForSearch)
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
            return tItem;

        }

        public LoopListViewItem2 GetItemFromTmpPool(int itemIndex)
        {
            if (mTmpPooledItemList.Count == 0)
            {
                return null;
            }
            LoopListViewItem2 tItem = null;
            int count = mTmpPooledItemList.Count;
            for (int i = 0; i < count; ++i)
            {
                if (mTmpPooledItemList[i].ItemIndex == itemIndex)
                {
                    tItem = mTmpPooledItemList[i];
                    mTmpPooledItemList.RemoveAt(i);
                    break;
                }
            }
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
        public LoopListViewItem2 CreateItem()
        {

            GameObject go = GameObject.Instantiate<GameObject>(mPrefabObj, Vector3.zero, Quaternion.identity, mItemParent);
            go.SetActive(true);
            RectTransform rf = go.GetComponent<RectTransform>();
            rf.localScale = Vector3.one;
            rf.anchoredPosition3D = Vector3.zero;
            rf.localEulerAngles = Vector3.zero;
            LoopListViewItem2 tViewItem = go.GetComponent<LoopListViewItem2>();
            tViewItem.ItemPrefabName = mPrefabName;
            tViewItem.StartPosOffset = mStartPosOffset;
            return tViewItem;
        }
        public void RecycleItemReal(LoopListViewItem2 item)
        {
            item.gameObject.SetActive(false);
            mPooledItemList.Add(item);
        }
        public void RecycleItem(LoopListViewItem2 item)
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
