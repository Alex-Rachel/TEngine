using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace SuperScrollView
{

    public class StaggeredGridItemGroup
    {

        LoopStaggeredGridView mParentGridView;
        ListItemArrangeType mArrangeType = ListItemArrangeType.TopToBottom;
        List<LoopStaggeredGridViewItem> mItemList = new List<LoopStaggeredGridViewItem>();
        RectTransform mContainerTrans;
        ScrollRect mScrollRect = null;
        public int mGroupIndex = 0;
        GameObject mGameObject;
        List<int> mItemIndexMap = new List<int>();
        RectTransform mScrollRectTransform = null;
        RectTransform mViewPortRectTransform = null;
        float mItemDefaultWithPaddingSize = 0;
        int mItemTotalCount = 0;
        bool mIsVertList = false;
        System.Func<int, int, LoopStaggeredGridViewItem> mOnGetItemByIndex;
        Vector3[] mItemWorldCorners = new Vector3[4];
        Vector3[] mViewPortRectLocalCorners = new Vector3[4];
        int mCurReadyMinItemIndex = 0;
        int mCurReadyMaxItemIndex = 0;
        bool mNeedCheckNextMinItem = true;
        bool mNeedCheckNextMaxItem = true;
        ItemPosMgr mItemPosMgr = null;
        bool mSupportScrollBar = true;
        int mLastItemIndex = 0;
        float mLastItemPadding = 0;
        Vector3 mLastFrameContainerPos = Vector3.zero;
        int mListUpdateCheckFrameCount = 0;

        public void Init(LoopStaggeredGridView parent, int itemTotalCount, int groupIndex,
            System.Func<int, int, LoopStaggeredGridViewItem> onGetItemByIndex)
        {
            mGroupIndex = groupIndex;
            mParentGridView = parent;
            mArrangeType = mParentGridView.ArrangeType;
            mGameObject = mParentGridView.gameObject;
            mScrollRect = mGameObject.GetComponent<ScrollRect>();
            mItemPosMgr = new ItemPosMgr(mItemDefaultWithPaddingSize);
            mScrollRectTransform = mScrollRect.GetComponent<RectTransform>();
            mContainerTrans = mScrollRect.content;
            mViewPortRectTransform = mScrollRect.viewport;
            if (mViewPortRectTransform == null)
            {
                mViewPortRectTransform = mScrollRectTransform;
            }
            mIsVertList = (mArrangeType == ListItemArrangeType.TopToBottom || mArrangeType == ListItemArrangeType.BottomToTop);
            mOnGetItemByIndex = onGetItemByIndex;
            mItemTotalCount = itemTotalCount;
            mViewPortRectTransform.GetLocalCorners(mViewPortRectLocalCorners);
            if (mItemTotalCount < 0)
            {
                mSupportScrollBar = false;
            }
            if (mSupportScrollBar)
            {
                mItemPosMgr.SetItemMaxCount(mItemTotalCount);
            }
            else
            {
                mItemPosMgr.SetItemMaxCount(0);
            }
            mCurReadyMaxItemIndex = 0;
            mCurReadyMinItemIndex = 0;
            mNeedCheckNextMaxItem = true;
            mNeedCheckNextMinItem = true;
        }


        public List<int> ItemIndexMap
        {
            get { return mItemIndexMap; }
        }

        public void ResetListView()
        {
            mViewPortRectTransform.GetLocalCorners(mViewPortRectLocalCorners);
        }

        //To get the visible item by itemIndex. If the item is not visible, then this method return null.
        public LoopStaggeredGridViewItem GetShownItemByItemIndex(int itemIndex)
        {
            int count = mItemList.Count;
            if (count == 0)
            {
                return null;
            }
            if (itemIndex < mItemList[0].ItemIndex || itemIndex > mItemList[count - 1].ItemIndex)
            {
                return null;
            }
            for(int i = 0;i<count;++i)
            {
                LoopStaggeredGridViewItem item = mItemList[i];
                if(item.ItemIndex == itemIndex)
                {
                    return item; 
                }
            }
            return null;
        }

        public float ViewPortSize
        {
            get
            {
                if (mIsVertList)
                {
                    return mViewPortRectTransform.rect.height;
                }
                else
                {
                    return mViewPortRectTransform.rect.width;
                }
            }
        }

        public float ViewPortWidth
        {
            get { return mViewPortRectTransform.rect.width; }
        }
        public float ViewPortHeight
        {
            get { return mViewPortRectTransform.rect.height; }
        }

        bool IsDraging
        {
            get { return mParentGridView.IsDraging; }
        }

        /*
         All visible items is stored in a List<LoopStaggeredGridViewItem> , which is named mItemList;
         this method is to get the visible item by the index in visible items list. The parameter index is from 0 to mItemList.Count.
        */
        public LoopStaggeredGridViewItem GetShownItemByIndexInGroup(int indexInGroup)
        {
            int count = mItemList.Count;
            if (count == 0)
            {
                return null;
            }
            if (indexInGroup < mItemList[0].ItemIndexInGroup || indexInGroup > mItemList[count - 1].ItemIndexInGroup)
            {
                return null;
            }
            int i = indexInGroup - mItemList[0].ItemIndexInGroup;
            return mItemList[i];
        }

        public int GetIndexInShownItemList(LoopStaggeredGridViewItem item)
        {
            if (item == null)
            {
                return -1;
            }
            int count = mItemList.Count;
            if (count == 0)
            {
                return -1;
            }
            for (int i = 0; i < count; ++i)
            {
                if (mItemList[i] == item)
                {
                    return i;
                }
            }
            return -1;
        }


        //update all visible items.
        public void RefreshAllShownItem()
        {
            int count = mItemList.Count;
            if (count == 0)
            {
                return;
            }
            RefreshAllShownItemWithFirstIndexInGroup(mItemList[0].ItemIndexInGroup);
        }


        /*
      For a vertical scrollrect, when a visible item’s height changed at runtime, then this method should be called to let the LoopListView2 component reposition all visible items’ position.
      For a horizontal scrollrect, when a visible item’s width changed at runtime, then this method should be called to let the LoopListView2 component reposition all visible items’ position.
      */
        public void OnItemSizeChanged(int indexInGroup)
        {
            LoopStaggeredGridViewItem item = GetShownItemByIndexInGroup(indexInGroup);
            if (item == null)
            {
                return;
            }
            if (mSupportScrollBar)
            {
                if (mIsVertList)
                {
                    SetItemSize(indexInGroup, item.CachedRectTransform.rect.height, item.Padding);
                }
                else
                {
                    SetItemSize(indexInGroup, item.CachedRectTransform.rect.width, item.Padding);
                }
            }
            UpdateAllShownItemsPos();
        }


        /*
        To update a item by itemIndex.if the itemIndex-th item is not visible, then this method will do nothing.
        Otherwise this method will first call onGetItemByIndex(itemIndex) to get a updated item and then reposition all visible items'position. 
        */
        public void RefreshItemByIndexInGroup(int indexInGroup)
        {
            int count = mItemList.Count;
            if(count == 0)
            {
                return;
            }
            if (indexInGroup < mItemList[0].ItemIndexInGroup || indexInGroup > mItemList[count - 1].ItemIndexInGroup)
            {
                return;
            }
            int firstItemIndexInGroup = mItemList[0].ItemIndexInGroup;
            int i = indexInGroup - firstItemIndexInGroup;
            LoopStaggeredGridViewItem curItem = mItemList[i];
            Vector3 pos = curItem.CachedRectTransform.anchoredPosition3D;
            RecycleItemTmp(curItem);
            LoopStaggeredGridViewItem newItem = GetNewItemByIndexInGroup(indexInGroup);
            if (newItem == null)
            {
                RefreshAllShownItemWithFirstIndexInGroup(firstItemIndexInGroup);
                return;
            }
            mItemList[i] = newItem;
            if (mIsVertList)
            {
                pos.x = newItem.StartPosOffset;
            }
            else
            {
                pos.y = newItem.StartPosOffset;
            }
            newItem.CachedRectTransform.anchoredPosition3D = pos;
            OnItemSizeChanged(indexInGroup);
            ClearAllTmpRecycledItem();
        }


        public void RefreshAllShownItemWithFirstIndexInGroup(int firstItemIndexInGroup)
        {
            int count = mItemList.Count;
            if (count == 0)
            {
                return;
            }
            LoopStaggeredGridViewItem firstItem = mItemList[0];
            Vector3 pos = firstItem.CachedRectTransform.anchoredPosition3D;
            RecycleAllItem();
            for (int i = 0; i < count; ++i)
            {
                int curIndex = firstItemIndexInGroup + i;
                LoopStaggeredGridViewItem newItem = GetNewItemByIndexInGroup(curIndex);
                if (newItem == null)
                {
                    break;
                }
                if (mIsVertList)
                {
                    pos.x = newItem.StartPosOffset;
                }
                else
                {
                    pos.y = newItem.StartPosOffset;
                }
                newItem.CachedRectTransform.anchoredPosition3D = pos;
                if (mSupportScrollBar)
                {
                    if (mIsVertList)
                    {
                        SetItemSize(curIndex, newItem.CachedRectTransform.rect.height, newItem.Padding);
                    }
                    else
                    {
                        SetItemSize(curIndex, newItem.CachedRectTransform.rect.width, newItem.Padding);
                    }
                }

                mItemList.Add(newItem);
            }
            UpdateAllShownItemsPos();
            ClearAllTmpRecycledItem();
        }


        public void RefreshAllShownItemWithFirstIndexAndPos(int firstItemIndexInGroup, Vector3 pos)
        {
            RecycleAllItem();
            LoopStaggeredGridViewItem newItem = GetNewItemByIndexInGroup(firstItemIndexInGroup);
            if (newItem == null)
            {
                return;
            }
            if (mIsVertList)
            {
                pos.x = newItem.StartPosOffset;
            }
            else
            {
                pos.y = newItem.StartPosOffset;
            }
            newItem.CachedRectTransform.anchoredPosition3D = pos;
            if (mSupportScrollBar)
            {
                if (mIsVertList)
                {
                    SetItemSize(firstItemIndexInGroup, newItem.CachedRectTransform.rect.height, newItem.Padding);
                }
                else
                {
                    SetItemSize(firstItemIndexInGroup, newItem.CachedRectTransform.rect.width, newItem.Padding);
                }
            }
            mItemList.Add(newItem);
            UpdateAllShownItemsPos();
            mParentGridView.UpdateListViewWithDefault();
            ClearAllTmpRecycledItem();
        }





        public void SetItemSize(int itemIndex, float itemSize, float padding)
        {
            mItemPosMgr.SetItemSize(itemIndex, itemSize + padding);
            if (itemIndex >= mLastItemIndex)
            {
                mLastItemIndex = itemIndex;
                mLastItemPadding = padding;
            }
        }

        bool GetPlusItemIndexAndPosAtGivenPos(float pos, ref int index, ref float itemPos)
        {
            return mItemPosMgr.GetItemIndexAndPosAtGivenPos(pos, ref index, ref itemPos);
        }


        public float GetItemPos(int itemIndex)
        {
            return mItemPosMgr.GetItemPos(itemIndex);
        }


        public Vector3 GetItemCornerPosInViewPort(LoopStaggeredGridViewItem item, ItemCornerEnum corner = ItemCornerEnum.LeftBottom)
        {
            item.CachedRectTransform.GetWorldCorners(mItemWorldCorners);
            return mViewPortRectTransform.InverseTransformPoint(mItemWorldCorners[(int)corner]);
        }


        public void RecycleItemTmp(LoopStaggeredGridViewItem item)
        {
            mParentGridView.RecycleItemTmp(item);
        }


        public void RecycleAllItem()
        {
            foreach (LoopStaggeredGridViewItem item in mItemList)
            {
                RecycleItemTmp(item);
            }
            mItemList.Clear();
        }

        public void ClearAllTmpRecycledItem()
        {
            mParentGridView.ClearAllTmpRecycledItem();
        }

        LoopStaggeredGridViewItem GetNewItemByIndexInGroup(int indexInGroup)
        {
            return mParentGridView.GetNewItemByGroupAndIndex(mGroupIndex, indexInGroup);
        }


        public int HadCreatedItemCount
        {
            get
            {
                return mItemIndexMap.Count;
            }
        }


        public void SetListItemCount(int itemCount)
        {
            if (itemCount == mItemTotalCount)
            {
                return;
            }
            int oldItemTotalCount = mItemTotalCount;
            mItemTotalCount = itemCount;
            UpdateItemIndexMap(oldItemTotalCount);
            if (oldItemTotalCount < mItemTotalCount)
            {
                mItemPosMgr.SetItemMaxCount(mItemTotalCount);
            }
            else
            {
                mItemPosMgr.SetItemMaxCount(HadCreatedItemCount);
                mItemPosMgr.SetItemMaxCount(mItemTotalCount);
            }
            RecycleAllItem();
            if (mItemTotalCount == 0)
            {
                mCurReadyMaxItemIndex = 0;
                mCurReadyMinItemIndex = 0;
                mNeedCheckNextMaxItem = false;
                mNeedCheckNextMinItem = false;
                mItemIndexMap.Clear();
                return;
            }
            
            if (mCurReadyMaxItemIndex >= mItemTotalCount)
            {
                mCurReadyMaxItemIndex = mItemTotalCount - 1;
            }
            mNeedCheckNextMaxItem = true;
            mNeedCheckNextMinItem = true;
        }

        void UpdateItemIndexMap(int oldItemTotalCount)
        {
            int count = mItemIndexMap.Count;
            if (count == 0)
            {
                return;
            }
            if (mItemTotalCount == 0)
            {
                mItemIndexMap.Clear();
                return;
            }
            if(mItemTotalCount >= oldItemTotalCount)
            {
                return;
            }
            int targetItemIndex = mParentGridView.ItemTotalCount;
            if (mItemIndexMap[count - 1] < targetItemIndex)
            {
                return;
            }
            int low = 0;
            int high = count - 1;
            int result = 0;
            while (low <= high)
            {
                int mid = (low + high) / 2;
                int index = mItemIndexMap[mid];
                if (index == targetItemIndex)
                {
                    result = mid;
                    break;
                }
                else if (index < targetItemIndex)
                {
                    low = mid + 1;
                    result = low;
                }
                else
                {
                    break;
                }
            }
            int startIndex = 0;
            for(int i = result; i< count; ++i)
            {
                if(mItemIndexMap[i] >= targetItemIndex)
                {
                    startIndex = i;
                    break;
                }
            }
            mItemIndexMap.RemoveRange(startIndex, count - startIndex);
        }


        public void UpdateListViewPart1(float distanceForRecycle0, float distanceForRecycle1, float distanceForNew0, float distanceForNew1)
        {
            if (mSupportScrollBar)
            {
                mItemPosMgr.Update(false);
            }
            mListUpdateCheckFrameCount = mParentGridView.ListUpdateCheckFrameCount;
            bool needContinueCheck = true;
            int checkCount = 0;
            int maxCount = 9999;
            while (needContinueCheck)
            {
                checkCount++;
                if (checkCount >= maxCount)
                {
                    Debug.LogError("UpdateListViewPart1 while loop " + checkCount + " times! something is wrong!");
                    break;
                }
                if(mIsVertList)
                {
                    needContinueCheck = UpdateForVertListPart1(distanceForRecycle0, distanceForRecycle1, distanceForNew0, distanceForNew1);
                }
                else
                {
                    needContinueCheck = UpdateForHorizontalListPart1(distanceForRecycle0, distanceForRecycle1, distanceForNew0, distanceForNew1);
                }
            }
            mLastFrameContainerPos = mContainerTrans.anchoredPosition3D;
        }


        public bool UpdateListViewPart2(float distanceForRecycle0, float distanceForRecycle1, float distanceForNew0, float distanceForNew1)
        {
            if (mIsVertList)
            {
                return UpdateForVertListPart2(distanceForRecycle0, distanceForRecycle1, distanceForNew0, distanceForNew1);
            }
            else
            {
                return UpdateForHorizontalListPart2(distanceForRecycle0, distanceForRecycle1, distanceForNew0, distanceForNew1);
            }
        }


        public bool UpdateForVertListPart1(float distanceForRecycle0, float distanceForRecycle1, float distanceForNew0, float distanceForNew1)
        {
            if (mItemTotalCount == 0)
            {
                if (mItemList.Count > 0)
                {
                    RecycleAllItem();
                }
                return false;
            }
            if (mArrangeType == ListItemArrangeType.TopToBottom)
            {
                int itemListCount = mItemList.Count;
                if (itemListCount == 0)
                {
                    float curY = mContainerTrans.anchoredPosition3D.y;
                    if (curY < 0)
                    {
                        curY = 0;
                    }
                    int index = 0;
                    float pos = -curY;
                    if (mSupportScrollBar)
                    {
                        bool succeed = GetPlusItemIndexAndPosAtGivenPos(curY, ref index, ref pos);
                        if (succeed == false)
                        {
                            return false;
                        }
                        pos = -pos;
                    }
                    LoopStaggeredGridViewItem newItem = GetNewItemByIndexInGroup(index);
                    if (newItem == null)
                    {
                        return false;
                    }
                    if (mSupportScrollBar)
                    {
                        SetItemSize(index, newItem.CachedRectTransform.rect.height, newItem.Padding);
                    }
                    mItemList.Add(newItem);
                    newItem.CachedRectTransform.anchoredPosition3D = new Vector3(newItem.StartPosOffset, pos, 0);
                    return true;
                }
                LoopStaggeredGridViewItem tViewItem0 = mItemList[0];
                tViewItem0.CachedRectTransform.GetWorldCorners(mItemWorldCorners);
                Vector3 topPos0 = mViewPortRectTransform.InverseTransformPoint(mItemWorldCorners[1]);
                Vector3 downPos0 = mViewPortRectTransform.InverseTransformPoint(mItemWorldCorners[0]);

                if (!IsDraging && tViewItem0.ItemCreatedCheckFrameCount != mListUpdateCheckFrameCount
                    && downPos0.y - mViewPortRectLocalCorners[1].y > distanceForRecycle0)
                {
                    mItemList.RemoveAt(0);
                    RecycleItemTmp(tViewItem0);
                    if (!mSupportScrollBar)
                    {
                        CheckIfNeedUpdateItemPos();
                    }
                    return true;
                }

                LoopStaggeredGridViewItem tViewItem1 = mItemList[mItemList.Count - 1];
                tViewItem1.CachedRectTransform.GetWorldCorners(mItemWorldCorners);
                Vector3 topPos1 = mViewPortRectTransform.InverseTransformPoint(mItemWorldCorners[1]);
                Vector3 downPos1 = mViewPortRectTransform.InverseTransformPoint(mItemWorldCorners[0]);
                if (!IsDraging && tViewItem1.ItemCreatedCheckFrameCount != mListUpdateCheckFrameCount
                    && mViewPortRectLocalCorners[0].y - topPos1.y > distanceForRecycle1)
                {
                    mItemList.RemoveAt(mItemList.Count - 1);
                    RecycleItemTmp(tViewItem1);
                    if (!mSupportScrollBar)
                    {
                        CheckIfNeedUpdateItemPos();
                    }
                    return true;
                }


                if (topPos0.y - mViewPortRectLocalCorners[1].y < distanceForNew0)
                {
                    if (tViewItem0.ItemIndexInGroup < mCurReadyMinItemIndex)
                    {
                        mCurReadyMinItemIndex = tViewItem0.ItemIndexInGroup;
                        mNeedCheckNextMinItem = true;
                    }
                    int nIndex = tViewItem0.ItemIndexInGroup - 1;
                    if (nIndex >= mCurReadyMinItemIndex || mNeedCheckNextMinItem)
                    {
                        LoopStaggeredGridViewItem newItem = GetNewItemByIndexInGroup(nIndex);
                        if (newItem == null)
                        {
                            mCurReadyMinItemIndex = tViewItem0.ItemIndexInGroup;
                            mNeedCheckNextMinItem = false;
                        }
                        else
                        {
                            if (mSupportScrollBar)
                            {
                                SetItemSize(nIndex, newItem.CachedRectTransform.rect.height, newItem.Padding);
                            }
                            mItemList.Insert(0, newItem);
                            float y = tViewItem0.CachedRectTransform.anchoredPosition3D.y + newItem.CachedRectTransform.rect.height + newItem.Padding;
                            newItem.CachedRectTransform.anchoredPosition3D = new Vector3(newItem.StartPosOffset, y, 0);
                            CheckIfNeedUpdateItemPos();
                            if (nIndex < mCurReadyMinItemIndex)
                            {
                                mCurReadyMinItemIndex = nIndex;
                            }
                            return true;
                        }

                    }

                }

                if (mViewPortRectLocalCorners[0].y - downPos1.y < distanceForNew1)
                {
                    if (tViewItem1.ItemIndexInGroup > mCurReadyMaxItemIndex)
                    {
                        mCurReadyMaxItemIndex = tViewItem1.ItemIndexInGroup;
                        mNeedCheckNextMaxItem = true;
                    }
                    int nIndex = tViewItem1.ItemIndexInGroup + 1;
                    if(nIndex >= mItemIndexMap.Count)
                    {
                        return false;
                    }
                    if (nIndex <= mCurReadyMaxItemIndex || mNeedCheckNextMaxItem)
                    {
                        LoopStaggeredGridViewItem newItem = GetNewItemByIndexInGroup(nIndex);
                        if (newItem == null)
                        {
                            mCurReadyMaxItemIndex = tViewItem1.ItemIndexInGroup;
                            mNeedCheckNextMaxItem = false;
                            CheckIfNeedUpdateItemPos();
                            return false;
                        }
                        else
                        {
                            if (mSupportScrollBar)
                            {
                                SetItemSize(nIndex, newItem.CachedRectTransform.rect.height, newItem.Padding);
                            }
                            mItemList.Add(newItem);
                            float y = tViewItem1.CachedRectTransform.anchoredPosition3D.y - tViewItem1.CachedRectTransform.rect.height - tViewItem1.Padding;
                            newItem.CachedRectTransform.anchoredPosition3D = new Vector3(newItem.StartPosOffset, y, 0);
                            CheckIfNeedUpdateItemPos();

                            if (nIndex > mCurReadyMaxItemIndex)
                            {
                                mCurReadyMaxItemIndex = nIndex;
                            }
                            return true;
                        }

                    }

                }

            }
            else
            {

                if (mItemList.Count == 0)
                {
                    float curY = mContainerTrans.anchoredPosition3D.y;
                    if (curY > 0)
                    {
                        curY = 0;
                    }
                    int index = 0;
                    float pos = -curY;
                    if (mSupportScrollBar)
                    {
                        bool succeed = GetPlusItemIndexAndPosAtGivenPos(-curY, ref index, ref pos);
                        if(succeed == false)
                        {
                            return false;
                        }
                    }
                    LoopStaggeredGridViewItem newItem = GetNewItemByIndexInGroup(index);
                    if (newItem == null)
                    {
                        return false;
                    }
                    if (mSupportScrollBar)
                    {
                        SetItemSize(index, newItem.CachedRectTransform.rect.height, newItem.Padding);
                    }
                    mItemList.Add(newItem);
                    newItem.CachedRectTransform.anchoredPosition3D = new Vector3(newItem.StartPosOffset, pos, 0);
                    return true;
                }
                LoopStaggeredGridViewItem tViewItem0 = mItemList[0];
                tViewItem0.CachedRectTransform.GetWorldCorners(mItemWorldCorners);
                Vector3 topPos0 = mViewPortRectTransform.InverseTransformPoint(mItemWorldCorners[1]);
                Vector3 downPos0 = mViewPortRectTransform.InverseTransformPoint(mItemWorldCorners[0]);

                if (!IsDraging && tViewItem0.ItemCreatedCheckFrameCount != mListUpdateCheckFrameCount
                    && mViewPortRectLocalCorners[0].y - topPos0.y > distanceForRecycle0)
                {
                    mItemList.RemoveAt(0);
                    RecycleItemTmp(tViewItem0);
                    if (!mSupportScrollBar)
                    {
                        CheckIfNeedUpdateItemPos();
                    }
                    return true;
                }

                LoopStaggeredGridViewItem tViewItem1 = mItemList[mItemList.Count - 1];
                tViewItem1.CachedRectTransform.GetWorldCorners(mItemWorldCorners);
                Vector3 topPos1 = mViewPortRectTransform.InverseTransformPoint(mItemWorldCorners[1]);
                Vector3 downPos1 = mViewPortRectTransform.InverseTransformPoint(mItemWorldCorners[0]);
                if (!IsDraging && tViewItem1.ItemCreatedCheckFrameCount != mListUpdateCheckFrameCount
                     && downPos1.y - mViewPortRectLocalCorners[1].y > distanceForRecycle1)
                {
                    mItemList.RemoveAt(mItemList.Count - 1);
                    RecycleItemTmp(tViewItem1);
                    if (!mSupportScrollBar)
                    {
                        CheckIfNeedUpdateItemPos();
                    }
                    return true;
                }


                if (mViewPortRectLocalCorners[0].y - downPos0.y < distanceForNew0)
                {
                    if (tViewItem0.ItemIndexInGroup < mCurReadyMinItemIndex)
                    {
                        mCurReadyMinItemIndex = tViewItem0.ItemIndexInGroup;
                        mNeedCheckNextMinItem = true;
                    }
                    int nIndex = tViewItem0.ItemIndexInGroup - 1;
                    if (nIndex >= mCurReadyMinItemIndex || mNeedCheckNextMinItem)
                    {
                        LoopStaggeredGridViewItem newItem = GetNewItemByIndexInGroup(nIndex);
                        if (newItem == null)
                        {
                            mCurReadyMinItemIndex = tViewItem0.ItemIndexInGroup;
                            mNeedCheckNextMinItem = false;
                        }
                        else
                        {
                            if (mSupportScrollBar)
                            {
                                SetItemSize(nIndex, newItem.CachedRectTransform.rect.height, newItem.Padding);
                            }
                            mItemList.Insert(0, newItem);
                            float y = tViewItem0.CachedRectTransform.anchoredPosition3D.y - newItem.CachedRectTransform.rect.height - newItem.Padding;
                            newItem.CachedRectTransform.anchoredPosition3D = new Vector3(newItem.StartPosOffset, y, 0);
                            CheckIfNeedUpdateItemPos();
                            if (nIndex < mCurReadyMinItemIndex)
                            {
                                mCurReadyMinItemIndex = nIndex;
                            }
                            return true;
                        }

                    }
                }

                if (topPos1.y - mViewPortRectLocalCorners[1].y < distanceForNew1)
                {
                    if (tViewItem1.ItemIndexInGroup > mCurReadyMaxItemIndex)
                    {
                        mCurReadyMaxItemIndex = tViewItem1.ItemIndexInGroup;
                        mNeedCheckNextMaxItem = true;
                    }
                    int nIndex = tViewItem1.ItemIndexInGroup + 1;
                    if (nIndex >= mItemIndexMap.Count)
                    {
                        return false;
                    }

                    if (nIndex <= mCurReadyMaxItemIndex || mNeedCheckNextMaxItem)
                    {
                        LoopStaggeredGridViewItem newItem = GetNewItemByIndexInGroup(nIndex);
                        if (newItem == null)
                        {
                            mCurReadyMaxItemIndex = tViewItem1.ItemIndexInGroup;
                            mNeedCheckNextMaxItem = false;
                            CheckIfNeedUpdateItemPos();
                            return false;
                        }
                        else
                        {
                            if (mSupportScrollBar)
                            {
                                SetItemSize(nIndex, newItem.CachedRectTransform.rect.height, newItem.Padding);
                            }
                            mItemList.Add(newItem);
                            float y = tViewItem1.CachedRectTransform.anchoredPosition3D.y + tViewItem1.CachedRectTransform.rect.height + tViewItem1.Padding;
                            newItem.CachedRectTransform.anchoredPosition3D = new Vector3(newItem.StartPosOffset, y, 0);
                            CheckIfNeedUpdateItemPos();
                            if (nIndex > mCurReadyMaxItemIndex)
                            {
                                mCurReadyMaxItemIndex = nIndex;
                            }
                            return true;
                        }

                    }

                }

            }

            return false;

        }


        public bool UpdateForVertListPart2(float distanceForRecycle0, float distanceForRecycle1, float distanceForNew0, float distanceForNew1)
        {
            if (mItemTotalCount == 0)
            {
                if (mItemList.Count > 0)
                {
                    RecycleAllItem();
                }
                return false;
            }
            if (mArrangeType == ListItemArrangeType.TopToBottom)
            {
                int itemListCount = mItemList.Count;
                if (itemListCount == 0)
                {
                    float curY = mContainerTrans.anchoredPosition3D.y;
                    if (curY < 0)
                    {
                        curY = 0;
                    }
                    int index = 0;
                    float pos = -curY;
                    if (mSupportScrollBar)
                    {
                        bool succeed = GetPlusItemIndexAndPosAtGivenPos(curY, ref index, ref pos);
                        if (succeed == false)
                        {
                            return false;
                        }
                        pos = -pos;
                    }
                    LoopStaggeredGridViewItem newItem = GetNewItemByIndexInGroup(index);
                    if (newItem == null)
                    {
                        return false;
                    }
                    if (mSupportScrollBar)
                    {
                        SetItemSize(index, newItem.CachedRectTransform.rect.height, newItem.Padding);
                    }
                    mItemList.Add(newItem);
                    newItem.CachedRectTransform.anchoredPosition3D = new Vector3(newItem.StartPosOffset, pos, 0);
                    return true;
                }

                LoopStaggeredGridViewItem tViewItem1 = mItemList[mItemList.Count - 1];
                tViewItem1.CachedRectTransform.GetWorldCorners(mItemWorldCorners);
                Vector3 downPos1 = mViewPortRectTransform.InverseTransformPoint(mItemWorldCorners[0]);
                
                if (mViewPortRectLocalCorners[0].y - downPos1.y < distanceForNew1)
                {
                    if (tViewItem1.ItemIndexInGroup > mCurReadyMaxItemIndex)
                    {
                        mCurReadyMaxItemIndex = tViewItem1.ItemIndexInGroup;
                        mNeedCheckNextMaxItem = true;
                    }
                    int nIndex = tViewItem1.ItemIndexInGroup + 1;
                    if (nIndex <= mCurReadyMaxItemIndex || mNeedCheckNextMaxItem)
                    {
                        LoopStaggeredGridViewItem newItem = GetNewItemByIndexInGroup(nIndex);
                        if (newItem == null)
                        {
                            mCurReadyMaxItemIndex = tViewItem1.ItemIndexInGroup;
                            mNeedCheckNextMaxItem = false;
                            CheckIfNeedUpdateItemPos();
                            return false;
                        }
                        else
                        {
                            if (mSupportScrollBar)
                            {
                                SetItemSize(nIndex, newItem.CachedRectTransform.rect.height, newItem.Padding);
                            }
                            mItemList.Add(newItem);
                            float y = tViewItem1.CachedRectTransform.anchoredPosition3D.y - tViewItem1.CachedRectTransform.rect.height - tViewItem1.Padding;
                            newItem.CachedRectTransform.anchoredPosition3D = new Vector3(newItem.StartPosOffset, y, 0);
                            CheckIfNeedUpdateItemPos();

                            if (nIndex > mCurReadyMaxItemIndex)
                            {
                                mCurReadyMaxItemIndex = nIndex;
                            }
                            return true;
                        }

                    }

                }

            }
            else
            {

                if (mItemList.Count == 0)
                {
                    float curY = mContainerTrans.anchoredPosition3D.y;
                    if (curY > 0)
                    {
                        curY = 0;
                    }
                    int index = 0;
                    float pos = -curY;
                    if (mSupportScrollBar)
                    {
                        bool succeed = GetPlusItemIndexAndPosAtGivenPos(-curY, ref index, ref pos);
                        if(succeed == false)
                        {
                            return false;
                        }
                    }
                    LoopStaggeredGridViewItem newItem = GetNewItemByIndexInGroup(index);
                    if (newItem == null)
                    {
                        return false;
                    }
                    if (mSupportScrollBar)
                    {
                        SetItemSize(index, newItem.CachedRectTransform.rect.height, newItem.Padding);
                    }
                    mItemList.Add(newItem);
                    newItem.CachedRectTransform.anchoredPosition3D = new Vector3(newItem.StartPosOffset, pos, 0);
                    return true;
                }
               
                LoopStaggeredGridViewItem tViewItem1 = mItemList[mItemList.Count - 1];
                tViewItem1.CachedRectTransform.GetWorldCorners(mItemWorldCorners);
                Vector3 topPos1 = mViewPortRectTransform.InverseTransformPoint(mItemWorldCorners[1]);
                
                if (topPos1.y - mViewPortRectLocalCorners[1].y < distanceForNew1)
                {
                    if (tViewItem1.ItemIndexInGroup > mCurReadyMaxItemIndex)
                    {
                        mCurReadyMaxItemIndex = tViewItem1.ItemIndexInGroup;
                        mNeedCheckNextMaxItem = true;
                    }
                    int nIndex = tViewItem1.ItemIndexInGroup + 1;
                    if (nIndex <= mCurReadyMaxItemIndex || mNeedCheckNextMaxItem)
                    {
                        LoopStaggeredGridViewItem newItem = GetNewItemByIndexInGroup(nIndex);
                        if (newItem == null)
                        {
                            mCurReadyMaxItemIndex = tViewItem1.ItemIndexInGroup;
                            mNeedCheckNextMaxItem = false;
                            CheckIfNeedUpdateItemPos();
                            return false;
                        }
                        else
                        {
                            if (mSupportScrollBar)
                            {
                                SetItemSize(nIndex, newItem.CachedRectTransform.rect.height, newItem.Padding);
                            }
                            mItemList.Add(newItem);
                            float y = tViewItem1.CachedRectTransform.anchoredPosition3D.y + tViewItem1.CachedRectTransform.rect.height + tViewItem1.Padding;
                            newItem.CachedRectTransform.anchoredPosition3D = new Vector3(newItem.StartPosOffset, y, 0);
                            CheckIfNeedUpdateItemPos();
                            if (nIndex > mCurReadyMaxItemIndex)
                            {
                                mCurReadyMaxItemIndex = nIndex;
                            }
                            return true;
                        }

                    }

                }

            }

            return false;

        }




        public bool UpdateForHorizontalListPart1(float distanceForRecycle0, float distanceForRecycle1, float distanceForNew0, float distanceForNew1)
        {
            if (mItemTotalCount == 0)
            {
                if (mItemList.Count > 0)
                {
                    RecycleAllItem();
                }
                return false;
            }
            if (mArrangeType == ListItemArrangeType.LeftToRight)
            {

                if (mItemList.Count == 0)
                {
                    float curX = mContainerTrans.anchoredPosition3D.x;
                    if (curX > 0)
                    {
                        curX = 0;
                    }
                    int index = 0;
                    float pos = -curX;
                    if (mSupportScrollBar)
                    {
                        bool succeed = GetPlusItemIndexAndPosAtGivenPos(-curX, ref index, ref pos);
                        if (succeed == false)
                        {
                            return false;
                        }
                    }
                    LoopStaggeredGridViewItem newItem = GetNewItemByIndexInGroup(index);
                    if (newItem == null)
                    {
                        return false;
                    }
                    if (mSupportScrollBar)
                    {
                        SetItemSize(index, newItem.CachedRectTransform.rect.width, newItem.Padding);
                    }
                    mItemList.Add(newItem);
                    newItem.CachedRectTransform.anchoredPosition3D = new Vector3(pos, newItem.StartPosOffset, 0);
                    return true;
                }
                LoopStaggeredGridViewItem tViewItem0 = mItemList[0];
                tViewItem0.CachedRectTransform.GetWorldCorners(mItemWorldCorners);
                Vector3 leftPos0 = mViewPortRectTransform.InverseTransformPoint(mItemWorldCorners[1]);
                Vector3 rightPos0 = mViewPortRectTransform.InverseTransformPoint(mItemWorldCorners[2]);

                if (!IsDraging && tViewItem0.ItemCreatedCheckFrameCount != mListUpdateCheckFrameCount
                    && mViewPortRectLocalCorners[1].x - rightPos0.x > distanceForRecycle0)
                {
                    mItemList.RemoveAt(0);
                    RecycleItemTmp(tViewItem0);
                    if (!mSupportScrollBar)
                    {
                        CheckIfNeedUpdateItemPos();
                    }
                    return true;
                }

                LoopStaggeredGridViewItem tViewItem1 = mItemList[mItemList.Count - 1];
                tViewItem1.CachedRectTransform.GetWorldCorners(mItemWorldCorners);
                Vector3 leftPos1 = mViewPortRectTransform.InverseTransformPoint(mItemWorldCorners[1]);
                Vector3 rightPos1 = mViewPortRectTransform.InverseTransformPoint(mItemWorldCorners[2]);
                if (!IsDraging && tViewItem1.ItemCreatedCheckFrameCount != mListUpdateCheckFrameCount
                    && leftPos1.x - mViewPortRectLocalCorners[2].x > distanceForRecycle1)
                {
                    mItemList.RemoveAt(mItemList.Count - 1);
                    RecycleItemTmp(tViewItem1);
                    if (!mSupportScrollBar)
                    {
                        CheckIfNeedUpdateItemPos();
                    }
                    return true;
                }


                if (mViewPortRectLocalCorners[1].x - leftPos0.x < distanceForNew0)
                {
                    if (tViewItem0.ItemIndexInGroup < mCurReadyMinItemIndex)
                    {
                        mCurReadyMinItemIndex = tViewItem0.ItemIndexInGroup;
                        mNeedCheckNextMinItem = true;
                    }
                    int nIndex = tViewItem0.ItemIndexInGroup - 1;
                    if (nIndex >= mCurReadyMinItemIndex || mNeedCheckNextMinItem)
                    {
                        LoopStaggeredGridViewItem newItem = GetNewItemByIndexInGroup(nIndex);
                        if (newItem == null)
                        {
                            mCurReadyMinItemIndex = tViewItem0.ItemIndexInGroup;
                            mNeedCheckNextMinItem = false;
                        }
                        else
                        {
                            if (mSupportScrollBar)
                            {
                                SetItemSize(nIndex, newItem.CachedRectTransform.rect.width, newItem.Padding);
                            }
                            mItemList.Insert(0, newItem);
                            float x = tViewItem0.CachedRectTransform.anchoredPosition3D.x - newItem.CachedRectTransform.rect.width - newItem.Padding;
                            newItem.CachedRectTransform.anchoredPosition3D = new Vector3(x, newItem.StartPosOffset, 0);
                            CheckIfNeedUpdateItemPos();
                            if (nIndex < mCurReadyMinItemIndex)
                            {
                                mCurReadyMinItemIndex = nIndex;
                            }
                            return true;
                        }

                    }

                }


                if (rightPos1.x - mViewPortRectLocalCorners[2].x < distanceForNew1)
                {
                    if (tViewItem1.ItemIndexInGroup > mCurReadyMaxItemIndex)
                    {
                        mCurReadyMaxItemIndex = tViewItem1.ItemIndexInGroup;
                        mNeedCheckNextMaxItem = true;
                    }
                    int nIndex = tViewItem1.ItemIndexInGroup + 1;
                    if (nIndex >= mItemIndexMap.Count)
                    {
                        return false;
                    }
                    if (nIndex <= mCurReadyMaxItemIndex || mNeedCheckNextMaxItem)
                    {
                        LoopStaggeredGridViewItem newItem = GetNewItemByIndexInGroup(nIndex);
                        if (newItem == null)
                        {
                            mCurReadyMaxItemIndex = tViewItem1.ItemIndexInGroup;
                            mNeedCheckNextMaxItem = false;
                            CheckIfNeedUpdateItemPos();
                        }
                        else
                        {
                            if (mSupportScrollBar)
                            {
                                SetItemSize(nIndex, newItem.CachedRectTransform.rect.width, newItem.Padding);
                            }
                            mItemList.Add(newItem);
                            float x = tViewItem1.CachedRectTransform.anchoredPosition3D.x + tViewItem1.CachedRectTransform.rect.width + tViewItem1.Padding;
                            newItem.CachedRectTransform.anchoredPosition3D = new Vector3(x, newItem.StartPosOffset, 0);
                            CheckIfNeedUpdateItemPos();

                            if (nIndex > mCurReadyMaxItemIndex)
                            {
                                mCurReadyMaxItemIndex = nIndex;
                            }
                            return true;
                        }

                    }

                }


            }
            else
            {

                if (mItemList.Count == 0)
                {
                    float curX = mContainerTrans.anchoredPosition3D.x;
                    if (curX < 0)
                    {
                        curX = 0;
                    }
                    int index = 0;
                    float pos = -curX;
                    if (mSupportScrollBar)
                    {
                        bool succeed = GetPlusItemIndexAndPosAtGivenPos(curX, ref index, ref pos);
                        if (succeed == false)
                        {
                            return false;
                        }
                        pos = -pos;
                    }
                    LoopStaggeredGridViewItem newItem = GetNewItemByIndexInGroup(index);
                    if (newItem == null)
                    {
                        return false;
                    }
                    if (mSupportScrollBar)
                    {
                        SetItemSize(index, newItem.CachedRectTransform.rect.width, newItem.Padding);
                    }
                    mItemList.Add(newItem);
                    newItem.CachedRectTransform.anchoredPosition3D = new Vector3(pos, newItem.StartPosOffset, 0);
                    return true;
                }
                LoopStaggeredGridViewItem tViewItem0 = mItemList[0];
                tViewItem0.CachedRectTransform.GetWorldCorners(mItemWorldCorners);
                Vector3 leftPos0 = mViewPortRectTransform.InverseTransformPoint(mItemWorldCorners[1]);
                Vector3 rightPos0 = mViewPortRectTransform.InverseTransformPoint(mItemWorldCorners[2]);

                if (!IsDraging && tViewItem0.ItemCreatedCheckFrameCount != mListUpdateCheckFrameCount
                    && leftPos0.x - mViewPortRectLocalCorners[2].x > distanceForRecycle0)
                {
                    mItemList.RemoveAt(0);
                    RecycleItemTmp(tViewItem0);
                    if (!mSupportScrollBar)
                    {
                        CheckIfNeedUpdateItemPos();
                    }
                    return true;
                }

                LoopStaggeredGridViewItem tViewItem1 = mItemList[mItemList.Count - 1];
                tViewItem1.CachedRectTransform.GetWorldCorners(mItemWorldCorners);
                Vector3 leftPos1 = mViewPortRectTransform.InverseTransformPoint(mItemWorldCorners[1]);
                Vector3 rightPos1 = mViewPortRectTransform.InverseTransformPoint(mItemWorldCorners[2]);
                if (!IsDraging && tViewItem1.ItemCreatedCheckFrameCount != mListUpdateCheckFrameCount
                    && mViewPortRectLocalCorners[1].x - rightPos1.x > distanceForRecycle1)
                {
                    mItemList.RemoveAt(mItemList.Count - 1);
                    RecycleItemTmp(tViewItem1);
                    if (!mSupportScrollBar)
                    {
                        CheckIfNeedUpdateItemPos();
                    }
                    return true;
                }


                if (rightPos0.x - mViewPortRectLocalCorners[2].x < distanceForNew0)
                {
                    if (tViewItem0.ItemIndexInGroup < mCurReadyMinItemIndex)
                    {
                        mCurReadyMinItemIndex = tViewItem0.ItemIndexInGroup;
                        mNeedCheckNextMinItem = true;
                    }
                    int nIndex = tViewItem0.ItemIndexInGroup - 1;
                    if (nIndex >= mCurReadyMinItemIndex || mNeedCheckNextMinItem)
                    {
                        LoopStaggeredGridViewItem newItem = GetNewItemByIndexInGroup(nIndex);
                        if (newItem == null)
                        {
                            mCurReadyMinItemIndex = tViewItem0.ItemIndexInGroup;
                            mNeedCheckNextMinItem = false;
                        }
                        else
                        {
                            if (mSupportScrollBar)
                            {
                                SetItemSize(nIndex, newItem.CachedRectTransform.rect.width, newItem.Padding);
                            }
                            mItemList.Insert(0, newItem);
                            float x = tViewItem0.CachedRectTransform.anchoredPosition3D.x + newItem.CachedRectTransform.rect.width + newItem.Padding;
                            newItem.CachedRectTransform.anchoredPosition3D = new Vector3(x, newItem.StartPosOffset, 0);
                            CheckIfNeedUpdateItemPos();
                            if (nIndex < mCurReadyMinItemIndex)
                            {
                                mCurReadyMinItemIndex = nIndex;
                            }
                            return true;
                        }

                    }

                }


                if (mViewPortRectLocalCorners[1].x - leftPos1.x < distanceForNew1)
                {
                    if (tViewItem1.ItemIndexInGroup > mCurReadyMaxItemIndex)
                    {
                        mCurReadyMaxItemIndex = tViewItem1.ItemIndexInGroup;
                        mNeedCheckNextMaxItem = true;
                    }
                    int nIndex = tViewItem1.ItemIndexInGroup + 1;
                    if (nIndex >= mItemIndexMap.Count)
                    {
                        return false;
                    }
                    if (nIndex <= mCurReadyMaxItemIndex || mNeedCheckNextMaxItem)
                    {
                        LoopStaggeredGridViewItem newItem = GetNewItemByIndexInGroup(nIndex);
                        if (newItem == null)
                        {
                            mCurReadyMaxItemIndex = tViewItem1.ItemIndexInGroup;
                            mNeedCheckNextMaxItem = false;
                            CheckIfNeedUpdateItemPos();
                        }
                        else
                        {
                            if (mSupportScrollBar)
                            {
                                SetItemSize(nIndex, newItem.CachedRectTransform.rect.width, newItem.Padding);
                            }
                            mItemList.Add(newItem);
                            float x = tViewItem1.CachedRectTransform.anchoredPosition3D.x - tViewItem1.CachedRectTransform.rect.width - tViewItem1.Padding;
                            newItem.CachedRectTransform.anchoredPosition3D = new Vector3(x, newItem.StartPosOffset, 0);
                            CheckIfNeedUpdateItemPos();

                            if (nIndex > mCurReadyMaxItemIndex)
                            {
                                mCurReadyMaxItemIndex = nIndex;
                            }
                            return true;
                        }

                    }

                }


            }

            return false;

        }




        public bool UpdateForHorizontalListPart2(float distanceForRecycle0, float distanceForRecycle1, float distanceForNew0, float distanceForNew1)
        {
            if (mItemTotalCount == 0)
            {
                if (mItemList.Count > 0)
                {
                    RecycleAllItem();
                }
                return false;
            }
            if (mArrangeType == ListItemArrangeType.LeftToRight)
            {

                if (mItemList.Count == 0)
                {
                    float curX = mContainerTrans.anchoredPosition3D.x;
                    if (curX > 0)
                    {
                        curX = 0;
                    }
                    int index = 0;
                    float pos = -curX;
                    if (mSupportScrollBar)
                    {
                        bool succeed = GetPlusItemIndexAndPosAtGivenPos(-curX, ref index, ref pos);
                        if (succeed == false)
                        {
                            return false;
                        }
                    }
                    LoopStaggeredGridViewItem newItem = GetNewItemByIndexInGroup(index);
                    if (newItem == null)
                    {
                        return false;
                    }
                    if (mSupportScrollBar)
                    {
                        SetItemSize(index, newItem.CachedRectTransform.rect.width, newItem.Padding);
                    }
                    mItemList.Add(newItem);
                    newItem.CachedRectTransform.anchoredPosition3D = new Vector3(pos, newItem.StartPosOffset, 0);
                    return true;
                }

                LoopStaggeredGridViewItem tViewItem1 = mItemList[mItemList.Count - 1];
                tViewItem1.CachedRectTransform.GetWorldCorners(mItemWorldCorners);
                Vector3 rightPos1 = mViewPortRectTransform.InverseTransformPoint(mItemWorldCorners[2]);
               
                if (rightPos1.x - mViewPortRectLocalCorners[2].x < distanceForNew1)
                {
                    if (tViewItem1.ItemIndexInGroup > mCurReadyMaxItemIndex)
                    {
                        mCurReadyMaxItemIndex = tViewItem1.ItemIndexInGroup;
                        mNeedCheckNextMaxItem = true;
                    }
                    int nIndex = tViewItem1.ItemIndexInGroup + 1;
                    if (nIndex <= mCurReadyMaxItemIndex || mNeedCheckNextMaxItem)
                    {
                        LoopStaggeredGridViewItem newItem = GetNewItemByIndexInGroup(nIndex);
                        if (newItem == null)
                        {
                            mCurReadyMaxItemIndex = tViewItem1.ItemIndexInGroup;
                            mNeedCheckNextMaxItem = false;
                            CheckIfNeedUpdateItemPos();
                        }
                        else
                        {
                            if (mSupportScrollBar)
                            {
                                SetItemSize(nIndex, newItem.CachedRectTransform.rect.width, newItem.Padding);
                            }
                            mItemList.Add(newItem);
                            float x = tViewItem1.CachedRectTransform.anchoredPosition3D.x + tViewItem1.CachedRectTransform.rect.width + tViewItem1.Padding;
                            newItem.CachedRectTransform.anchoredPosition3D = new Vector3(x, newItem.StartPosOffset, 0);
                            CheckIfNeedUpdateItemPos();

                            if (nIndex > mCurReadyMaxItemIndex)
                            {
                                mCurReadyMaxItemIndex = nIndex;
                            }
                            return true;
                        }

                    }

                }


            }
            else
            {

                if (mItemList.Count == 0)
                {
                    float curX = mContainerTrans.anchoredPosition3D.x;
                    if (curX < 0)
                    {
                        curX = 0;
                    }
                    int index = 0;
                    float pos = -curX;
                    if (mSupportScrollBar)
                    {
                        bool succeed = GetPlusItemIndexAndPosAtGivenPos(curX, ref index, ref pos);
                        if (succeed == false)
                        {
                            return false;
                        }
                        pos = -pos;
                    }
                    LoopStaggeredGridViewItem newItem = GetNewItemByIndexInGroup(index);
                    if (newItem == null)
                    {
                        return false;
                    }
                    if (mSupportScrollBar)
                    {
                        SetItemSize(index, newItem.CachedRectTransform.rect.width, newItem.Padding);
                    }
                    mItemList.Add(newItem);
                    newItem.CachedRectTransform.anchoredPosition3D = new Vector3(pos, newItem.StartPosOffset, 0);
                    return true;
                }
                
                LoopStaggeredGridViewItem tViewItem1 = mItemList[mItemList.Count - 1];
                tViewItem1.CachedRectTransform.GetWorldCorners(mItemWorldCorners);
                Vector3 leftPos1 = mViewPortRectTransform.InverseTransformPoint(mItemWorldCorners[1]);
                
                if (mViewPortRectLocalCorners[1].x - leftPos1.x < distanceForNew1)
                {
                    if (tViewItem1.ItemIndexInGroup > mCurReadyMaxItemIndex)
                    {
                        mCurReadyMaxItemIndex = tViewItem1.ItemIndexInGroup;
                        mNeedCheckNextMaxItem = true;
                    }
                    int nIndex = tViewItem1.ItemIndexInGroup + 1;
                    if (nIndex <= mCurReadyMaxItemIndex || mNeedCheckNextMaxItem)
                    {
                        LoopStaggeredGridViewItem newItem = GetNewItemByIndexInGroup(nIndex);
                        if (newItem == null)
                        {
                            mCurReadyMaxItemIndex = tViewItem1.ItemIndexInGroup;
                            mNeedCheckNextMaxItem = false;
                            CheckIfNeedUpdateItemPos();
                        }
                        else
                        {
                            if (mSupportScrollBar)
                            {
                                SetItemSize(nIndex, newItem.CachedRectTransform.rect.width, newItem.Padding);
                            }
                            mItemList.Add(newItem);
                            float x = tViewItem1.CachedRectTransform.anchoredPosition3D.x - tViewItem1.CachedRectTransform.rect.width - tViewItem1.Padding;
                            newItem.CachedRectTransform.anchoredPosition3D = new Vector3(x, newItem.StartPosOffset, 0);
                            CheckIfNeedUpdateItemPos();

                            if (nIndex > mCurReadyMaxItemIndex)
                            {
                                mCurReadyMaxItemIndex = nIndex;
                            }
                            return true;
                        }

                    }

                }
            }

            return false;

        }




        public float GetContentPanelSize()
        {
            float tTotalSize = mItemPosMgr.mTotalSize > 0 ? (mItemPosMgr.mTotalSize - mLastItemPadding) : 0;
            if (tTotalSize < 0)
            {
                tTotalSize = 0;
            }
            return tTotalSize;
        }

        public float GetContentPanelSizeWithLastItemPadding()
        {
            float tTotalSize = mItemPosMgr.mTotalSize > 0 ? mItemPosMgr.mTotalSize : 0;
            if (tTotalSize < 0)
            {
                tTotalSize = 0;
            }
            return tTotalSize;
        }


        public float GetShownItemPosMaxValue()
        {
            int count = mItemList.Count;
            if (count == 0)
            {
                return 0f;
            }
            LoopStaggeredGridViewItem lastItem = mItemList[mItemList.Count - 1];
            if (mArrangeType == ListItemArrangeType.TopToBottom)
            {
                return Mathf.Abs(lastItem.BottomY);
            }
            else if (mArrangeType == ListItemArrangeType.BottomToTop)
            {
                return Mathf.Abs(lastItem.TopY);
            }
            else if (mArrangeType == ListItemArrangeType.LeftToRight)
            {
                return Mathf.Abs(lastItem.RightX);
            }
            else if (mArrangeType == ListItemArrangeType.RightToLeft)
            {
                return Mathf.Abs(lastItem.LeftX);
            }
            return 0f;
        }

        public void CheckIfNeedUpdateItemPos()
        {
            int count = mItemList.Count;
            if (count == 0)
            {
                return;
            }
            if (mArrangeType == ListItemArrangeType.TopToBottom)
            {
                LoopStaggeredGridViewItem firstItem = mItemList[0];
                LoopStaggeredGridViewItem lastItem = mItemList[mItemList.Count - 1];
                if (firstItem.TopY > 0 || (firstItem.ItemIndexInGroup == mCurReadyMinItemIndex && firstItem.TopY != 0))
                {
                    UpdateAllShownItemsPos();
                    return;
                }
                float viewMaxY = GetContentPanelSize();
                if ((-lastItem.BottomY) > viewMaxY || (lastItem.ItemIndexInGroup == mCurReadyMaxItemIndex && (-lastItem.BottomY) != viewMaxY))
                {
                    UpdateAllShownItemsPos();
                    return;
                }
            }
            else if (mArrangeType == ListItemArrangeType.BottomToTop)
            {
                LoopStaggeredGridViewItem firstItem = mItemList[0];
                LoopStaggeredGridViewItem lastItem = mItemList[mItemList.Count - 1];
                if (firstItem.BottomY < 0 || (firstItem.ItemIndexInGroup == mCurReadyMinItemIndex && firstItem.BottomY != 0))
                {
                    UpdateAllShownItemsPos();
                    return;
                }
                float viewMaxY = GetContentPanelSize();
                if (lastItem.TopY > viewMaxY || (lastItem.ItemIndexInGroup == mCurReadyMaxItemIndex && lastItem.TopY != viewMaxY))
                {
                    UpdateAllShownItemsPos();
                    return;
                }
            }
            else if (mArrangeType == ListItemArrangeType.LeftToRight)
            {
                LoopStaggeredGridViewItem firstItem = mItemList[0];
                LoopStaggeredGridViewItem lastItem = mItemList[mItemList.Count - 1];
                if (firstItem.LeftX < 0 || (firstItem.ItemIndexInGroup == mCurReadyMinItemIndex && firstItem.LeftX != 0))
                {
                    UpdateAllShownItemsPos();
                    return;
                }
                float viewMaxX = GetContentPanelSize();
                if ((lastItem.RightX) > viewMaxX || (lastItem.ItemIndexInGroup == mCurReadyMaxItemIndex && lastItem.RightX != viewMaxX))
                {
                    UpdateAllShownItemsPos();
                    return;
                }
            }
            else if (mArrangeType == ListItemArrangeType.RightToLeft)
            {
                LoopStaggeredGridViewItem firstItem = mItemList[0];
                LoopStaggeredGridViewItem lastItem = mItemList[mItemList.Count - 1];
                if (firstItem.RightX > 0 || (firstItem.ItemIndexInGroup == mCurReadyMinItemIndex && firstItem.RightX != 0))
                {
                    UpdateAllShownItemsPos();
                    return;
                }
                float viewMaxX = GetContentPanelSize();
                if ((-lastItem.LeftX) > viewMaxX || (lastItem.ItemIndexInGroup == mCurReadyMaxItemIndex && (-lastItem.LeftX) != viewMaxX))
                {
                    UpdateAllShownItemsPos();
                    return;
                }
            }

        }


        public void UpdateAllShownItemsPos()
        {
            int count = mItemList.Count;
            if (count == 0)
            {
                return;
            }

            if (mArrangeType == ListItemArrangeType.TopToBottom)
            {
                float pos = 0;
                if (mSupportScrollBar)
                {
                    pos = -GetItemPos(mItemList[0].ItemIndexInGroup);
                }
                float curY = pos;
                for (int i = 0; i < count; ++i)
                {
                    LoopStaggeredGridViewItem item = mItemList[i];
                    item.CachedRectTransform.anchoredPosition3D = new Vector3(item.StartPosOffset, curY, 0);
                    curY = curY - item.CachedRectTransform.rect.height - item.Padding;
                }
            }
            else if (mArrangeType == ListItemArrangeType.BottomToTop)
            {
                float pos = 0;
                if (mSupportScrollBar)
                {
                    pos = GetItemPos(mItemList[0].ItemIndexInGroup);
                }
                float curY = pos;
                for (int i = 0; i < count; ++i)
                {
                    LoopStaggeredGridViewItem item = mItemList[i];
                    item.CachedRectTransform.anchoredPosition3D = new Vector3(item.StartPosOffset, curY, 0);
                    curY = curY + item.CachedRectTransform.rect.height + item.Padding;
                }
            }
            else if (mArrangeType == ListItemArrangeType.LeftToRight)
            {
                float pos = 0;
                if (mSupportScrollBar)
                {
                    pos = GetItemPos(mItemList[0].ItemIndexInGroup);
                }
                float curX = pos;
                for (int i = 0; i < count; ++i)
                {
                    LoopStaggeredGridViewItem item = mItemList[i];
                    item.CachedRectTransform.anchoredPosition3D = new Vector3(curX, item.StartPosOffset, 0);
                    curX = curX + item.CachedRectTransform.rect.width + item.Padding;
                }
            }
            else if (mArrangeType == ListItemArrangeType.RightToLeft)
            {
                float pos = 0;
                if (mSupportScrollBar)
                {
                    pos = -GetItemPos(mItemList[0].ItemIndexInGroup);
                }
                float curX = pos;
                for (int i = 0; i < count; ++i)
                {
                    LoopStaggeredGridViewItem item = mItemList[i];
                    item.CachedRectTransform.anchoredPosition3D = new Vector3(curX, item.StartPosOffset, 0);
                    curX = curX - item.CachedRectTransform.rect.width - item.Padding;
                }
            }
        }
    }



}
