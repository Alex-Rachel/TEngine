using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace SuperScrollView
{

    [System.Serializable]
    public class StaggeredGridItemPrefabConfData
    {
        public GameObject mItemPrefab = null;
        public float mPadding = 0;
        public int mInitCreateCount = 0;
    }


    public class StaggeredGridViewInitParam
    {
        // all the default values
        public float mDistanceForRecycle0 = 300; //mDistanceForRecycle0 should be larger than mDistanceForNew0
        public float mDistanceForNew0 = 200;
        public float mDistanceForRecycle1 = 300;//mDistanceForRecycle1 should be larger than mDistanceForNew1
        public float mDistanceForNew1 = 200;
        public float mItemDefaultWithPaddingSize = 100;//item's default size (with padding)

        public static StaggeredGridViewInitParam CopyDefaultInitParam()
        {
            return new StaggeredGridViewInitParam();
        }
    }

    public class ItemIndexData
    {
        public int mGroupIndex;
        public int mIndexInGroup;
    }

    public class SGVAutoMoveToItemData
    {
        public int mTargetItemIndex;
        public float mBeginPos;
        public float mTargetPos;
        public float mStartMoveTime;
        public float mDuration;
    }


    /*
    For an vertical GridView, mColumnOrRowCount is the column count, 
    mItemWidthOrHeight is the item’s width, mPadding1 is the viewport left margin, 
    mPadding2 is the viewport right margin.
    For an horizontal GridView, mColumnOrRowCount is the row count, 
    mItemWidthOrHeight is the item’s height, mPadding1 is the viewport top margin, 
    mPadding2 is the viewport bottom margin. 
    If mCustomColumnOrRowOffsetArray is null, 
    that is to say, you do not set value for this parameter,
    then the GiriView would arrange all the columns or rows averaged.
    If mCustomColumnOrRowOffsetArray is not null, 
    the values of the array is the XOffset/YOffset of each column/row, 
    and mCustomColumnOrRowOffsetArray.length must be same to mColumnOrRowCount.
    */
    public class GridViewLayoutParam
    {
        public int mColumnOrRowCount = 0;//gridview column or row count
        public float mItemWidthOrHeight = 0; //gridview item width or height
        public float mPadding1 = 0;
        public float mPadding2 = 0;
        public float[] mCustomColumnOrRowOffsetArray = null;

        public bool CheckParam()
        {
            if (mColumnOrRowCount <= 0)
            {
                Debug.LogError("mColumnOrRowCount shoud be > 0");
                return false;
            }
            if (mCustomColumnOrRowOffsetArray != null && mCustomColumnOrRowOffsetArray.Length != mColumnOrRowCount)
            {
                Debug.LogError("mGroupOffsetArray.Length != mColumnOrRowCount");
                return false;
            }
            return true;
        }
    }



    public class LoopStaggeredGridView : MonoBehaviour, IBeginDragHandler, IEndDragHandler, IDragHandler
    {
        Dictionary<string, StaggeredGridItemPool> mItemPoolDict = new Dictionary<string, StaggeredGridItemPool>();
        List<StaggeredGridItemPool> mItemPoolList = new List<StaggeredGridItemPool>();
        [SerializeField]
        List<StaggeredGridItemPrefabConfData> mItemPrefabDataList = new List<StaggeredGridItemPrefabConfData>();

        [SerializeField]
        private ListItemArrangeType mArrangeType = ListItemArrangeType.TopToBottom;
        public ListItemArrangeType ArrangeType { get { return mArrangeType; } set { mArrangeType = value; } }

        RectTransform mContainerTrans;
        ScrollRect mScrollRect = null;
        int mGroupCount = 0;

        List<StaggeredGridItemGroup> mItemGroupList = new List<StaggeredGridItemGroup>();
        List<ItemIndexData> mItemIndexDataList = new List<ItemIndexData>();

        RectTransform mScrollRectTransform = null;
        RectTransform mViewPortRectTransform = null;
        float mItemDefaultWithPaddingSize = 100;
        int mItemTotalCount = 0;
        bool mIsVertList = false;
        System.Func<LoopStaggeredGridView, int,LoopStaggeredGridViewItem> mOnGetItemByItemIndex;
        System.Func<int,(float,float)> mOnGetItemSizeByItemIndex;
        Vector3[] mItemWorldCorners = new Vector3[4];
        Vector3[] mViewPortRectLocalCorners = new Vector3[4];
        float mDistanceForRecycle0 = 300;
        float mDistanceForNew0 = 200;
        float mDistanceForRecycle1 = 300;
        float mDistanceForNew1 = 200;
        bool mIsDraging = false;
        PointerEventData mPointerEventData = null;
        public System.Action mOnBeginDragAction = null;
        public System.Action mOnDragingAction = null;
        public System.Action mOnEndDragAction = null;
        Vector3 mLastFrameContainerPos = Vector3.zero;
        bool mListViewInited = false;
        int mListUpdateCheckFrameCount = 0;
        GridViewLayoutParam mLayoutParam = null;
        bool mIsPointerDownInScrollBar = false;
        bool mNeedReplaceScrollbarEventHandler = true;
        ClickEventListener mScrollBarClickEventListener = null;
        int mCurCreatingItemIndex = -1;

        SGVAutoMoveToItemData mSGVAutoMoveToItemData = null;

        public List<StaggeredGridItemPrefabConfData> ItemPrefabDataList
        {
            get
            {
                return mItemPrefabDataList;
            }
        }

        public int ListUpdateCheckFrameCount
        {
            get
            {
                return mListUpdateCheckFrameCount;
            }
        }


        public bool IsVertList
        {
            get
            {
                return mIsVertList;
            }
        }
        public int ItemTotalCount
        {
            get
            {
                return mItemTotalCount;
            }
        }

        public RectTransform ContainerTrans
        {
            get
            {
                return mContainerTrans;
            }
        }

        public ScrollRect ScrollRect
        {
            get
            {
                return mScrollRect;
            }
        }

        public bool IsDraging
        {
            get
            {
                return mIsDraging;
            }
        }

        public GridViewLayoutParam LayoutParam
        {
            get { return mLayoutParam; }
        }

        public bool IsInited
        {
            get { return mListViewInited; }
        }

        public StaggeredGridItemGroup GetItemGroupByIndex(int index)
        {
            int count = mItemGroupList.Count;
            if(index < 0 || index >= count)
            {
                return null;
            }
            return mItemGroupList[index];
        }


        public StaggeredGridItemPrefabConfData GetItemPrefabConfData(string prefabName)
        {
            foreach (StaggeredGridItemPrefabConfData data in mItemPrefabDataList)
            {
                if (data.mItemPrefab == null)
                {
                    Debug.LogError("A item prefab is null ");
                    continue;
                }
                if (prefabName == data.mItemPrefab.name)
                {
                    return data;
                }

            }
            return null;
        }

        /*
        InitListView method is to initiate the LoopStaggeredGridView component. There are 4 parameters:

        itemTotalCount: the total item count in the scrollview, this parameter should be >=0.

        layoutParam: this class is very sample, and you need new a GridViewLayoutParam instance and set the values you want.
        
        onGetItemByItemIndex: when an item is getting in the scrollrect viewport, this Action will be called with the item’ index as a parameter, to let you create the item and update its content.
        
        onGetItemSizeByItemIndex: if you can know every item's size beforehand, you may set this delegate that is to return the size and padding of a given itemIndex.
        Because every item may have different size, and LoopStaggeredGridView needs the item's size to make layout, so if you cannot know every item's size beforehand,
        the MovePanelToItemIndex method of LoopStaggeredGridView can only move to an item that had ever been shown in the scrollRect viewport. 
        But if you can know every item's size beforehand, you may set this delegate that is to return the size and padding of a given itemIndex, 
        and then when you want to move the panel to a given itemIndex,  you may firstly call LoopStaggeredGridView.UpdateContentSizeUpToItemIndex(itemIndex) to ensure the LoopStaggeredGridView have made layout up to the itemIndex,
        and then call MovePanelToItemIndex(itemIndex) would move panel to that item successfully.
        the parameter is null by default.
        StaggeredViewMoveToItemDemo scene shows more details.
        
        return value: LoopStaggeredGridViewItem is the return value of onGetItemByItemIndex

        Every created item has a LoopStaggeredGridViewItem component auto attached
        */
        public void InitListView(int itemTotalCount, GridViewLayoutParam layoutParam,
            System.Func<LoopStaggeredGridView, int, LoopStaggeredGridViewItem> onGetItemByItemIndex,
            StaggeredGridViewInitParam initParam = null,
            System.Func<int,(float, float)> onGetItemSizeByItemIndex = null)
        {
            mLayoutParam = layoutParam;
            if(mLayoutParam == null)
            {
                Debug.LogError("layoutParam can not be null!");
                return;
            }
            if (mLayoutParam.CheckParam() == false)
            {
                return;
            }
            if (initParam != null)
            {
                mDistanceForRecycle0 = initParam.mDistanceForRecycle0;
                mDistanceForNew0 = initParam.mDistanceForNew0;
                mDistanceForRecycle1 = initParam.mDistanceForRecycle1;
                mDistanceForNew1 = initParam.mDistanceForNew1;
                mItemDefaultWithPaddingSize = initParam.mItemDefaultWithPaddingSize;
            }
            mScrollRect = gameObject.GetComponent<ScrollRect>();
            if (mScrollRect == null)
            {
                Debug.LogError("LoopStaggeredGridView Init Failed! ScrollRect component not found!");
                return;
            }
            if (mDistanceForRecycle0 <= mDistanceForNew0)
            {
                Debug.LogError("mDistanceForRecycle0 should be bigger than mDistanceForNew0");
            }
            if (mDistanceForRecycle1 <= mDistanceForNew1)
            {
                Debug.LogError("mDistanceForRecycle1 should be bigger than mDistanceForNew1");
            }
            mScrollRectTransform = mScrollRect.GetComponent<RectTransform>();
            mContainerTrans = mScrollRect.content;
            mViewPortRectTransform = mScrollRect.viewport;
            mGroupCount = mLayoutParam.mColumnOrRowCount;
            if (mViewPortRectTransform == null)
            {
                mViewPortRectTransform = mScrollRectTransform;
            }
            if (mScrollRect.horizontalScrollbarVisibility == ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport && mScrollRect.horizontalScrollbar != null)
            {
                Debug.LogError("ScrollRect.horizontalScrollbarVisibility cannot be set to AutoHideAndExpandViewport");
            }
            if (mScrollRect.verticalScrollbarVisibility == ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport && mScrollRect.verticalScrollbar != null)
            {
                Debug.LogError("ScrollRect.verticalScrollbarVisibility cannot be set to AutoHideAndExpandViewport");
            }
            mIsVertList = (mArrangeType == ListItemArrangeType.TopToBottom || mArrangeType == ListItemArrangeType.BottomToTop);
            if(mIsVertList)
            {
                mScrollRect.vertical = true;
            }
            else
            {
                mScrollRect.horizontal = true;
            }
            UpdateItemSize();
            AdjustPivot(mViewPortRectTransform);
            AdjustAnchor(mContainerTrans);
            AdjustContainerPivot(mContainerTrans);
            InitItemPool();
            mOnGetItemByItemIndex = onGetItemByItemIndex;
            mOnGetItemSizeByItemIndex = onGetItemSizeByItemIndex;
            if (mListViewInited == true)
            {
                Debug.LogError("LoopStaggeredGridView.InitListView method can be called only once.");
            }
            mListViewInited = true;
            if(mNeedReplaceScrollbarEventHandler)
            {
                ReplaceScrollbarEventHandlerForSmoothMove();
            }
            mViewPortRectTransform.GetLocalCorners(mViewPortRectLocalCorners);
            mContainerTrans.anchoredPosition3D = Vector3.zero;
            mItemTotalCount = itemTotalCount;
            UpdateLayoutParamAutoValue();
            mItemGroupList.Clear();
            for (int i = 0;i<mGroupCount;++i)
            {
                StaggeredGridItemGroup group = new StaggeredGridItemGroup();
                group.Init(this, mItemTotalCount, i, GetNewItemByGroupAndIndex);
                mItemGroupList.Add(group);
            }
            UpdateContentSize();
        }

        //reset the layout param, such as column count, item width/height,padding size
        public void ResetGridViewLayoutParam(int itemTotalCount, GridViewLayoutParam layoutParam)
        {
            if(mListViewInited == false)
            {
                Debug.LogError("ResetLayoutParam can not use before LoopStaggeredGridView.InitListView are called!");
                return;
            }
            mScrollRect.StopMovement();
            SetListItemCount(0,true);
            RecycleAllItem();
            ClearAllTmpRecycledItem();
            mLayoutParam = layoutParam;
            if (mLayoutParam == null)
            {
                Debug.LogError("layoutParam can not be null!");
                return;
            }
            if (mLayoutParam.CheckParam() == false)
            {
                return;
            }
            mGroupCount = mLayoutParam.mColumnOrRowCount;
            mViewPortRectTransform.GetLocalCorners(mViewPortRectLocalCorners);
            mContainerTrans.anchoredPosition3D = Vector3.zero;
            mItemTotalCount = itemTotalCount;
            UpdateLayoutParamAutoValue();
            mItemGroupList.Clear();
            for (int i = 0; i < mGroupCount; ++i)
            {
                StaggeredGridItemGroup group = new StaggeredGridItemGroup();
                group.Init(this, mItemTotalCount, i, GetNewItemByGroupAndIndex);
                mItemGroupList.Add(group);
            }
            UpdateContentSize();
        }


        void OnPointerDownInScrollBar(GameObject obj)
        {
            mIsPointerDownInScrollBar = true;
        }

        void OnPointerUpInScrollBar(GameObject obj)
        {
            mIsPointerDownInScrollBar = false;
        }


        void ReplaceScrollbarEventHandlerForSmoothMove()
        {
            mScrollBarClickEventListener = null;
            Scrollbar curScrollBar = null;
            if (mIsVertList && mScrollRect.verticalScrollbar != null)
            {
                curScrollBar = mScrollRect.verticalScrollbar;
            }
            if (!mIsVertList && mScrollRect.horizontalScrollbar != null)
            {
                curScrollBar = mScrollRect.horizontalScrollbar;
            }
            if(curScrollBar != null)
            {
                ClickEventListener listener = ClickEventListener.Get(curScrollBar.gameObject);
                mScrollBarClickEventListener = listener;
                listener.SetPointerUpHandler(OnPointerUpInScrollBar);
                listener.SetPointerDownHandler(OnPointerDownInScrollBar);
                curScrollBar.onValueChanged.RemoveAllListeners();
                curScrollBar.onValueChanged.AddListener(OnScrollBarValueChanged);
            }
        }

        void OnScrollBarValueChanged(float value)
        {
            if (mIsPointerDownInScrollBar)
            {
                ClearAutoMoveToItemData();
                if (mIsVertList)
                {
                    mScrollRect.verticalNormalizedPosition = value;
                }
                else
                {
                    mScrollRect.horizontalNormalizedPosition = value;
                }
            }
        }



        void UpdateItemSize()
        {
            if (mLayoutParam.mItemWidthOrHeight > 0f)
            {
                return;
            }
            if (mItemPrefabDataList.Count == 0)
            {
                return;
            }
            GameObject obj = mItemPrefabDataList[0].mItemPrefab;
            if (obj == null)
            {
                return;
            }
            RectTransform rtf = obj.GetComponent<RectTransform>();
            if (rtf == null)
            {
                return;
            }
            if (IsVertList)
            {
                mLayoutParam.mItemWidthOrHeight = rtf.rect.width;
            }
            else
            {
                mLayoutParam.mItemWidthOrHeight = rtf.rect.height;
            }
            if (mLayoutParam.mItemWidthOrHeight <= 0)
            {
                Debug.LogError("mLayoutParam.mItemWidthOrHeight shoud be > 0");
                return;
            }
        }

        void UpdateLayoutParamAutoValue()
        {
            if (mLayoutParam.mCustomColumnOrRowOffsetArray == null)
            {
                mLayoutParam.mCustomColumnOrRowOffsetArray = new float[mGroupCount];
                float itemTotalSize = mLayoutParam.mItemWidthOrHeight * mGroupCount;
                float itemPadding = 0;
                if (IsVertList)
                {
                    itemPadding = (ViewPortWidth - mLayoutParam.mPadding1 - mLayoutParam.mPadding2 - itemTotalSize) / (mGroupCount - 1);
                }
                else
                {
                    itemPadding = (ViewPortHeight - mLayoutParam.mPadding1 - mLayoutParam.mPadding2 - itemTotalSize) / (mGroupCount - 1);
                }
                float cur = mLayoutParam.mPadding1;
                for (int i = 0; i < mGroupCount; ++i)
                {
                    if (IsVertList)
                    {
                        mLayoutParam.mCustomColumnOrRowOffsetArray[i] = cur;
                    }
                    else
                    {
                        mLayoutParam.mCustomColumnOrRowOffsetArray[i] = -cur;
                    }
                    cur = cur + mLayoutParam.mItemWidthOrHeight + itemPadding;
                }
            }
        }


        //This method is used to get a new item, and the new item is a clone from the prefab named itemPrefabName.
        //This method is usually used in onGetItemByItemIndex.
        public LoopStaggeredGridViewItem NewListViewItem(string itemPrefabName)
        {
            StaggeredGridItemPool pool = null;
            if (mItemPoolDict.TryGetValue(itemPrefabName, out pool) == false)
            {
                return null;
            }
            LoopStaggeredGridViewItem item = pool.GetItem(mCurCreatingItemIndex);
            RectTransform rf = item.GetComponent<RectTransform>();
            rf.SetParent(mContainerTrans);
            rf.localScale = Vector3.one;
            rf.anchoredPosition3D = Vector3.zero;
            rf.localEulerAngles = Vector3.zero;
            item.ParentListView = this;
            return item;
        }


        //This method may use to set the item total count of the GridView at runtime.
        //If resetPos is set false, then the scrollrect's content position will not changed after this method finished.
        public void SetListItemCount(int itemCount, bool resetPos = true)
        {
            if (itemCount == mItemTotalCount)
            {
                return;
            }
            int groupCount = mItemGroupList.Count;
            mItemTotalCount = itemCount;
            for (int i = 0; i < groupCount; ++i)
            {
                mItemGroupList[i].SetListItemCount(mItemTotalCount);
            }
            UpdateContentSize();
            if (mItemTotalCount == 0)
            {
                mItemIndexDataList.Clear();
                ClearAllTmpRecycledItem();
                mScrollRect.StopMovement();
                if (IsVertList)
                {
                    SetAnchoredPositionY(mContainerTrans, 0f);
                }
                else
                {
                    SetAnchoredPositionX(mContainerTrans, 0f);
                }
                return;
            }
            int count = mItemIndexDataList.Count;

            if (count > mItemTotalCount)
            {
                mItemIndexDataList.RemoveRange(mItemTotalCount, count - mItemTotalCount);
            }
            if (resetPos)
            {
                MovePanelToItemIndex(0, 0);
                return;
            }
            if (count > mItemTotalCount)
            {
                MovePanelToItemIndex(mItemTotalCount - 1, 0); ;
            }
        }


        //This method will move the scrollrect content’s position 
        //to ( the positon of itemIndex-th item + offset ) immediately
        public void MovePanelToItemIndexImmediately(int itemIndex, float offset)
        {
            ClearAutoMoveToItemData();
            mScrollRect.StopMovement();
            if (mItemTotalCount == 0 || itemIndex < 0)
            {
                return;
            }
            CheckAllGroupIfNeedUpdateItemPos();
            UpdateContentSize();
            float viewPortSize = ViewPortSize;
            float contentSize = GetContentSize();
            if (contentSize <= viewPortSize)
            {
                if (IsVertList)
                {
                    SetAnchoredPositionY(mContainerTrans, 0f);
                }
                else
                {
                    SetAnchoredPositionX(mContainerTrans, 0f);
                }
                return;
            }
            if (itemIndex >= mItemTotalCount)
            {
                itemIndex = mItemTotalCount - 1;
            }
            float itemAbsPos = GetItemAbsPosByItemIndex(itemIndex);
            if (itemAbsPos < 0)
            {
                return;
            }
            if (IsVertList)
            {
                float sign = (mArrangeType == ListItemArrangeType.TopToBottom) ? 1 : -1;
                float newYAbs = itemAbsPos + offset;
                if (newYAbs < 0)
                {
                    newYAbs = 0;
                }
                if (contentSize - newYAbs >= viewPortSize)
                {
                    SetAnchoredPositionY(mContainerTrans, sign * newYAbs);
                }
                else
                {
                    SetAnchoredPositionY(mContainerTrans, sign * (contentSize - viewPortSize));
                    UpdateListView(viewPortSize + 100, viewPortSize + 100, viewPortSize, viewPortSize);
                    ClearAllTmpRecycledItem();
                    UpdateContentSize();
                    contentSize = GetContentSize();
                    if (contentSize - newYAbs >= viewPortSize)
                    {
                        SetAnchoredPositionY(mContainerTrans, sign * newYAbs);
                    }
                    else
                    {
                        SetAnchoredPositionY(mContainerTrans, sign * (contentSize - viewPortSize));
                    }
                }

            }
            else
            {
                float sign = (mArrangeType == ListItemArrangeType.RightToLeft) ? 1 : -1;
                float newXAbs = itemAbsPos + offset;
                if (newXAbs < 0)
                {
                    newXAbs = 0;
                }
                if (contentSize - newXAbs >= viewPortSize)
                {
                    SetAnchoredPositionX(mContainerTrans, sign * newXAbs);
                }
                else
                {
                    SetAnchoredPositionX(mContainerTrans, sign * (contentSize - viewPortSize));
                    UpdateListView(viewPortSize + 100, viewPortSize + 100, viewPortSize, viewPortSize);
                    ClearAllTmpRecycledItem();
                    UpdateContentSize();
                    contentSize = GetContentSize();
                    if (contentSize - newXAbs >= viewPortSize)
                    {
                        SetAnchoredPositionX(mContainerTrans, sign * newXAbs);
                    }
                    else
                    {
                        SetAnchoredPositionX(mContainerTrans, sign * (contentSize - viewPortSize));
                    }
                }
            }

        }



        //This method will move the scrollrect content’s position 
        //to ( the positon of itemIndex-th item + offset ) in duration seconds.

        public void MovePanelToItemIndex(int itemIndex, float offset,float duration = 0)
        {
            ClearAutoMoveToItemData();
            if (duration <= 0.1f)
            {
                MovePanelToItemIndexImmediately(itemIndex, offset);
                return;
            }
            mScrollRect.StopMovement();
            if (mItemTotalCount == 0 || itemIndex < 0)
            {
                return;
            }
            CheckAllGroupIfNeedUpdateItemPos();
            UpdateContentSize();
            float viewPortSize = ViewPortSize;
            float contentSize = GetContentSize();
            if (contentSize <= viewPortSize)
            {
                if (IsVertList)
                {
                    SetAnchoredPositionY(mContainerTrans, 0f);
                }
                else
                {
                    SetAnchoredPositionX(mContainerTrans, 0f);
                }
                return;
            }
            if (itemIndex >= mItemTotalCount)
            {
                itemIndex = mItemTotalCount - 1;
            }
            float itemAbsPos = GetItemAbsPosByItemIndex(itemIndex);
            if (itemAbsPos < 0)
            {
                return;
            }
            mSGVAutoMoveToItemData = new SGVAutoMoveToItemData();
            mSGVAutoMoveToItemData.mTargetItemIndex = itemIndex;
            mSGVAutoMoveToItemData.mStartMoveTime = Time.time;
            mSGVAutoMoveToItemData.mDuration = duration;
            if (IsVertList)
            {
                float CurPosY = mContainerTrans.anchoredPosition3D.y;
                mSGVAutoMoveToItemData.mBeginPos = CurPosY;
                float sign = (mArrangeType == ListItemArrangeType.TopToBottom) ? 1 : -1;
                float newYAbs = itemAbsPos + offset;
                if (newYAbs < 0)
                {
                    newYAbs = 0;
                }
                if (contentSize - newYAbs >= viewPortSize)
                {
                    mSGVAutoMoveToItemData.mTargetPos = sign * newYAbs;
                }
                else
                {
                    SetAnchoredPositionY(mContainerTrans, sign * (contentSize - viewPortSize));
                    UpdateListView(viewPortSize + 100, viewPortSize + 100, viewPortSize, viewPortSize);
                    ClearAllTmpRecycledItem();
                    UpdateContentSize();
                    contentSize = GetContentSize();
                    if (contentSize - newYAbs >= viewPortSize)
                    {
                        mSGVAutoMoveToItemData.mTargetPos = sign * newYAbs;
                    }
                    else
                    {
                        mSGVAutoMoveToItemData.mTargetPos = sign * (contentSize - viewPortSize);
                    }
                    SetAnchoredPositionY(mContainerTrans, CurPosY);
                }

            }
            else
            {
                float CurPosX = mContainerTrans.anchoredPosition3D.x;
                mSGVAutoMoveToItemData.mBeginPos = CurPosX;
                float sign = (mArrangeType == ListItemArrangeType.RightToLeft) ? 1 : -1;
                float newXAbs = itemAbsPos + offset;
                if (newXAbs < 0)
                {
                    newXAbs = 0;
                }
                if (contentSize - newXAbs >= viewPortSize)
                {
                    mSGVAutoMoveToItemData.mTargetPos = sign * newXAbs;
                }
                else
                {
                    SetAnchoredPositionX(mContainerTrans, sign * (contentSize - viewPortSize));
                    UpdateListView(viewPortSize + 100, viewPortSize + 100, viewPortSize, viewPortSize);
                    ClearAllTmpRecycledItem();
                    UpdateContentSize();
                    contentSize = GetContentSize();
                    if (contentSize - newXAbs >= viewPortSize)
                    {
                        mSGVAutoMoveToItemData.mTargetPos = sign * newXAbs;
                    }
                    else
                    {
                        mSGVAutoMoveToItemData.mTargetPos = sign * (contentSize - viewPortSize);
                    }
                    SetAnchoredPositionX(mContainerTrans, CurPosX);
                }
            }
        }


        //To get the visible item by itemIndex. If the item is not visible, then this method return null.
        public LoopStaggeredGridViewItem GetShownItemByItemIndex(int itemIndex)
        {
            ItemIndexData indexData = GetItemIndexData(itemIndex);
            if (indexData == null)
            {
                return null;
            }
            StaggeredGridItemGroup group = GetItemGroupByIndex(indexData.mGroupIndex);
            return group.GetShownItemByIndexInGroup(indexData.mIndexInGroup);
        }

        //update all visible items.
        public void RefreshAllShownItem()
        {
            int count = mItemGroupList.Count;
            for (int i = 0; i < count; ++i)
            {
                mItemGroupList[i].RefreshAllShownItem();
            }
        }


        /*
      For a vertical scrollrect, when a visible item’s height changed at runtime, 
      then this method should be called to let the LoopStaggeredGridView component reposition all visible items’ position of the same group (that is the same column / row).
      For a horizontal scrollrect, when a visible item’s width changed at runtime,
      then this method should be called to let the LoopStaggeredGridView component reposition all visible items’ position of the same group (that is the same column / row).
      */
        public void OnItemSizeChanged(int itemIndex)
        {
            ItemIndexData indexData = GetItemIndexData(itemIndex);
            if (indexData == null)
            {
                return;
            }
            StaggeredGridItemGroup group = GetItemGroupByIndex(indexData.mGroupIndex);
            group.OnItemSizeChanged(indexData.mIndexInGroup);
        }


        /*
        To update a item by itemIndex.if the itemIndex-th item is not visible, then this method will do nothing.
        Otherwise this method will first call onGetItemByIndex(itemIndex) to get a updated item and then reposition all visible items'position. 
        */
        public void RefreshItemByItemIndex(int itemIndex)
        {
            ItemIndexData indexData = GetItemIndexData(itemIndex);
            if (indexData == null)
            {
                return;
            }
            StaggeredGridItemGroup group = GetItemGroupByIndex(indexData.mGroupIndex);
            group.RefreshItemByIndexInGroup(indexData.mIndexInGroup);
        }


        public void ResetListView(bool resetPos = true)
        {
            mViewPortRectTransform.GetLocalCorners(mViewPortRectLocalCorners);
            if (resetPos)
            {
                mContainerTrans.anchoredPosition3D = Vector3.zero;
            }
        }


        /*
         If you can know every item's size beforehand, you may set the onGetItemSizeByItemIndex delegate that is to return the size and padding of a given itemIndex.
         and then when you want to move the panel to a given itemIndex, 
         you may firstly call LoopStaggeredGridView.UpdateContentSizeUpToItemIndex(itemIndex) to ensure the LoopStaggeredGridView have made layout up to the itemIndex, 
         and then call MovePanelToItemIndex(itemIndex) would move panel to that item successfully.
         */
        public void UpdateContentSizeUpToItemIndex(int itemIndex)
        {
            if(mItemTotalCount == 0)
            {
                return;
            }
            if(mOnGetItemSizeByItemIndex == null)
            {
                return;
            }
            if(itemIndex >= mItemTotalCount)
            {
                itemIndex = mItemTotalCount - 1;
            }
            if(itemIndex <= 0)
            {
                return;
            }
            int curMaxCreatedItemIndexCount = mItemIndexDataList.Count;
            if (itemIndex < curMaxCreatedItemIndexCount)
            {
                return;
            }

            float v = float.MaxValue;
            int groupCount = mItemGroupList.Count;
            int groupIndex = 0;
            float[] groupContentSize = new float[groupCount];
            for (int i = 0; i < groupCount; ++i)
            {
                float size = mItemGroupList[i].GetContentPanelSizeWithLastItemPadding();
                groupContentSize[i] = size;
            }
            for (int curItemIndex = curMaxCreatedItemIndexCount; curItemIndex <= itemIndex;++curItemIndex)
            {
                v = float.MaxValue;
                groupIndex = 0;
                for (int i = 0; i < groupCount; ++i)
                {
                    float size = groupContentSize[i];
                    if (size < v)
                    {
                        v = size;
                        groupIndex = i;
                    }
                }
                var (itemSize, itemPadding) = mOnGetItemSizeByItemIndex(curItemIndex);
                groupContentSize[groupIndex] = groupContentSize[groupIndex] + itemSize + itemPadding;
                List<int> mItemIndexMap = mItemGroupList[groupIndex].ItemIndexMap;
                mItemIndexMap.Add(curItemIndex);
                ItemIndexData indexData = new ItemIndexData();
                indexData.mGroupIndex = groupIndex;
                indexData.mIndexInGroup = mItemIndexMap.Count - 1;
                mItemIndexDataList.Add(indexData);
                mItemGroupList[groupIndex].SetItemSize(indexData.mIndexInGroup, itemSize, itemPadding);
            }
            UpdateContentSize();
        }


        public void ClearAutoMoveToItemData()
        {
            mSGVAutoMoveToItemData = null;
        }


        public void UpdateAutoMoveToItem()
        {
            if (mSGVAutoMoveToItemData == null)
            {
                return;
            }
            float movedTime = Time.time - mSGVAutoMoveToItemData.mStartMoveTime;
            float p = movedTime / mSGVAutoMoveToItemData.mDuration;
            float pos = Mathf.Lerp(mSGVAutoMoveToItemData.mBeginPos, mSGVAutoMoveToItemData.mTargetPos, p);
            if (IsVertList)
            {
                SetAnchoredPositionY(mContainerTrans, pos);
            }
            else
            {
                SetAnchoredPositionX(mContainerTrans, pos);
            }
            if (p >= 1)
            {
                mSGVAutoMoveToItemData = null;
            }
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

        public void RecycleAllItem()
        {
            int groupCount = mItemGroupList.Count;
            for (int i = 0; i < groupCount; ++i)
            {
                mItemGroupList[i].RecycleAllItem();
            }
        }


        public void RecycleItemTmp(LoopStaggeredGridViewItem item)
        {
            if (item == null)
            {
                return;
            }
            if (string.IsNullOrEmpty(item.ItemPrefabName))
            {
                return;
            }
            StaggeredGridItemPool pool = null;
            if (mItemPoolDict.TryGetValue(item.ItemPrefabName, out pool) == false)
            {
                return;
            }
            pool.RecycleItem(item);

        }


        public void ClearAllTmpRecycledItem()
        {
            int count = mItemPoolList.Count;
            for (int i = 0; i < count; ++i)
            {
                mItemPoolList[i].ClearTmpRecycledItem();
            }
        }


        void AdjustContainerPivot(RectTransform rtf)
        {
            Vector2 pivot = rtf.pivot;
            if (mArrangeType == ListItemArrangeType.BottomToTop)
            {
                pivot.y = 0;
            }
            else if (mArrangeType == ListItemArrangeType.TopToBottom)
            {
                pivot.y = 1;
            }
            else if (mArrangeType == ListItemArrangeType.LeftToRight)
            {
                pivot.x = 0;
            }
            else if (mArrangeType == ListItemArrangeType.RightToLeft)
            {
                pivot.x = 1;
            }
            rtf.pivot = pivot;
        }


        void AdjustPivot(RectTransform rtf)
        {
            Vector2 pivot = rtf.pivot;

            if (mArrangeType == ListItemArrangeType.BottomToTop)
            {
                pivot.y = 0;
            }
            else if (mArrangeType == ListItemArrangeType.TopToBottom)
            {
                pivot.y = 1;
            }
            else if (mArrangeType == ListItemArrangeType.LeftToRight)
            {
                pivot.x = 0;
            }
            else if (mArrangeType == ListItemArrangeType.RightToLeft)
            {
                pivot.x = 1;
            }
            rtf.pivot = pivot;
        }

        void AdjustContainerAnchor(RectTransform rtf)
        {
            Vector2 anchorMin = rtf.anchorMin;
            Vector2 anchorMax = rtf.anchorMax;
            if (mArrangeType == ListItemArrangeType.BottomToTop)
            {
                anchorMin.y = 0;
                anchorMax.y = 0;
            }
            else if (mArrangeType == ListItemArrangeType.TopToBottom)
            {
                anchorMin.y = 1;
                anchorMax.y = 1;
            }
            else if (mArrangeType == ListItemArrangeType.LeftToRight)
            {
                anchorMin.x = 0;
                anchorMax.x = 0;
            }
            else if (mArrangeType == ListItemArrangeType.RightToLeft)
            {
                anchorMin.x = 1;
                anchorMax.x = 1;
            }
            rtf.anchorMin = anchorMin;
            rtf.anchorMax = anchorMax;
        }


        void AdjustAnchor(RectTransform rtf)
        {
            Vector2 anchorMin = rtf.anchorMin;
            Vector2 anchorMax = rtf.anchorMax;
            if (mArrangeType == ListItemArrangeType.BottomToTop)
            {
                anchorMin.y = 0;
                anchorMax.y = 0;
            }
            else if (mArrangeType == ListItemArrangeType.TopToBottom)
            {
                anchorMin.y = 1;
                anchorMax.y = 1;
            }
            else if (mArrangeType == ListItemArrangeType.LeftToRight)
            {
                anchorMin.x = 0;
                anchorMax.x = 0;
            }
            else if (mArrangeType == ListItemArrangeType.RightToLeft)
            {
                anchorMin.x = 1;
                anchorMax.x = 1;
            }
            rtf.anchorMin = anchorMin;
            rtf.anchorMax = anchorMax;
        }

        void InitItemPool()
        {
            foreach (StaggeredGridItemPrefabConfData data in mItemPrefabDataList)
            {
                if (data.mItemPrefab == null)
                {
                    Debug.LogError("A item prefab is null ");
                    continue;
                }
                string prefabName = data.mItemPrefab.name;
                if (mItemPoolDict.ContainsKey(prefabName))
                {
                    Debug.LogError("A item prefab with name " + prefabName + " has existed!");
                    continue;
                }
                RectTransform rtf = data.mItemPrefab.GetComponent<RectTransform>();
                if (rtf == null)
                {
                    Debug.LogError("RectTransform component is not found in the prefab " + prefabName);
                    continue;
                }
                AdjustAnchor(rtf);
                AdjustPivot(rtf);
                LoopStaggeredGridViewItem tItem = data.mItemPrefab.GetComponent<LoopStaggeredGridViewItem>();
                if (tItem == null)
                {
                    data.mItemPrefab.AddComponent<LoopStaggeredGridViewItem>();
                }
                StaggeredGridItemPool pool = new StaggeredGridItemPool();
                pool.Init(data.mItemPrefab, data.mPadding, data.mInitCreateCount, mContainerTrans);
                mItemPoolDict.Add(prefabName, pool);
                mItemPoolList.Add(pool);
            }
        }



        public virtual void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }
            mIsDraging = true;
            CacheDragPointerEventData(eventData);
            if (mOnBeginDragAction != null)
            {
                mOnBeginDragAction();
            }
        }

        public virtual void OnEndDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }
            mIsDraging = false;
            mPointerEventData = null;
            if (mOnEndDragAction != null)
            {
                mOnEndDragAction();
            }
        }

        public virtual void OnDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }
            CacheDragPointerEventData(eventData);
            if (mOnDragingAction != null)
            {
                mOnDragingAction();
            }
        }

        void CacheDragPointerEventData(PointerEventData eventData)
        {
            if (mPointerEventData == null)
            {
                mPointerEventData = new PointerEventData(EventSystem.current);
            }
            mPointerEventData.button = eventData.button;
            mPointerEventData.position = eventData.position;
            mPointerEventData.pointerPressRaycast = eventData.pointerPressRaycast;
            mPointerEventData.pointerCurrentRaycast = eventData.pointerCurrentRaycast;
        }


        public int CurMaxCreatedItemIndexCount
        {
            get { return mItemIndexDataList.Count; }
        }

       
        void SetAnchoredPositionX(RectTransform rtf,float x)
        {
            Vector3 pos = rtf.anchoredPosition3D;
            pos.x = x;
            rtf.anchoredPosition3D = pos;
        }

        void SetAnchoredPositionY(RectTransform rtf, float y)
        {
            Vector3 pos = rtf.anchoredPosition3D;
            pos.y = y;
            rtf.anchoredPosition3D = pos;
        }

        public ItemIndexData GetItemIndexData(int itemIndex)
        {
            int count = mItemIndexDataList.Count;
            if(itemIndex < 0 || itemIndex >= count)
            {
                return null;
            }
            return mItemIndexDataList[itemIndex];
        }




        public void UpdateAllGroupShownItemsPos()
        {
            int groupCount = mItemGroupList.Count;
            for (int i = 0; i < groupCount; ++i)
            {
                mItemGroupList[i].UpdateAllShownItemsPos();
            }
        }

        void CheckAllGroupIfNeedUpdateItemPos()
        {
            int groupCount = mItemGroupList.Count;
            for (int i = 0; i < groupCount; ++i)
            {
                mItemGroupList[i].CheckIfNeedUpdateItemPos();
            }
        }


        public float GetItemAbsPosByItemIndex(int itemIndex)
        {
            if(itemIndex < 0 || itemIndex >= mItemIndexDataList.Count)
            {
                return -1;
            }
            ItemIndexData tData = mItemIndexDataList[itemIndex];
            return mItemGroupList[tData.mGroupIndex].GetItemPos(tData.mIndexInGroup);
        }

        public LoopStaggeredGridViewItem GetNewItemByGroupAndIndex(int groupIndex,int indexInGroup)
        {
            if (indexInGroup < 0)
            {
                return null;
            }
            if(mItemTotalCount == 0)
            {
                return null;
            }
            LoopStaggeredGridViewItem newItem = null;
            int index = 0;
            List<int> mItemIndexMap = mItemGroupList[groupIndex].ItemIndexMap;
            int count = mItemIndexMap.Count;
            if (count > indexInGroup)
            {
                index = mItemIndexMap[indexInGroup];
                mCurCreatingItemIndex = index;
                newItem = mOnGetItemByItemIndex(this, index);
                if (newItem == null)
                {
                    return null;
                }
                newItem.StartPosOffset = mLayoutParam.mCustomColumnOrRowOffsetArray[groupIndex];
                newItem.ItemIndexInGroup = indexInGroup;
                newItem.ItemIndex = index;
                newItem.ItemCreatedCheckFrameCount = mListUpdateCheckFrameCount;
                return newItem;
            }
            if(count != indexInGroup)
            {
                return null;
            }
            int curMaxCreatedItemIndexCount = mItemIndexDataList.Count;
            if (curMaxCreatedItemIndexCount >= mItemTotalCount)
            {
                return null;
            }
            index = curMaxCreatedItemIndexCount;
            mCurCreatingItemIndex = index;
            newItem = mOnGetItemByItemIndex(this, index);
            if (newItem == null)
            {
                return null;
            }
            mItemIndexMap.Add(index);
            ItemIndexData indexData = new ItemIndexData();
            indexData.mGroupIndex = groupIndex;
            indexData.mIndexInGroup = indexInGroup;
            mItemIndexDataList.Add(indexData);
            newItem.StartPosOffset = mLayoutParam.mCustomColumnOrRowOffsetArray[groupIndex];
            newItem.ItemIndexInGroup = indexInGroup;
            newItem.ItemIndex = index;
            newItem.ItemCreatedCheckFrameCount = mListUpdateCheckFrameCount;
            return newItem;
        }

        int GetCurShouldAddNewItemGroupIndex()
        {
            float v = float.MaxValue;
            int groupCount = mItemGroupList.Count;
            int groupIndex = 0;
            for (int i = 0; i < groupCount; ++i)
            {
                float size = mItemGroupList[i].GetShownItemPosMaxValue();
                if(size < v)
                {
                    v = size;
                    groupIndex = i;
                }
            }
            return groupIndex;
        }

        public void UpdateListViewWithDefault()
        {
            UpdateListView(mDistanceForRecycle0, mDistanceForRecycle1, mDistanceForNew0, mDistanceForNew1);
            UpdateContentSize();
        }


        void Update()
        {
            if (mListViewInited == false)
            {
                return;
            }
            UpdateAutoMoveToItem();
            UpdateListViewWithDefault();
            ClearAllTmpRecycledItem();
            mLastFrameContainerPos = mContainerTrans.anchoredPosition3D;
        }




        public void UpdateListView(float distanceForRecycle0, float distanceForRecycle1, float distanceForNew0, float distanceForNew1)
        {
            mListUpdateCheckFrameCount++;
            bool needContinueCheck = true;
            int checkCount = 0;
            int maxCount = 9999;
            int groupCount = mItemGroupList.Count;
            for (int i = 0; i < groupCount; ++i)
            {
                mItemGroupList[i].UpdateListViewPart1(distanceForRecycle0, distanceForRecycle1, distanceForNew0, distanceForNew1);
            }
            while (needContinueCheck)
            {
                checkCount++;
                if (checkCount >= maxCount)
                {
                    Debug.LogError("UpdateListView while loop " + checkCount + " times! something is wrong!");
                    break;
                }
                int groupIndex = GetCurShouldAddNewItemGroupIndex();
                needContinueCheck = mItemGroupList[groupIndex].UpdateListViewPart2(distanceForRecycle0, distanceForRecycle1, distanceForNew0, distanceForNew1);
            }
               
        }

        public float GetContentSize()
        {
            if (mIsVertList)
            {
                return mContainerTrans.rect.height;
            }
            else
            {
                return mContainerTrans.rect.width;
            }
        }
        public void UpdateContentSize()
        {
            int groupCount = mItemGroupList.Count;
            float size = 0;
            for (int i = 0; i < groupCount; ++i)
            {
                float s = mItemGroupList[i].GetContentPanelSize();
                if (s > size)
                {
                    size = s;
                }
            }
            if (mIsVertList)
            {
                if (mContainerTrans.rect.height != size)
                {
                    mContainerTrans.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, size);
                }
            }
            else
            {
                if (mContainerTrans.rect.width != size)
                {
                    mContainerTrans.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size);
                }
            }
        }
    }

}
