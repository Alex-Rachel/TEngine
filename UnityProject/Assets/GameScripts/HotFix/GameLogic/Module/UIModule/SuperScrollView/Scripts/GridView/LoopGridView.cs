using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SuperScrollView
{

    [System.Serializable]
    public class GridViewItemPrefabConfData
    {
        public GameObject mItemPrefab = null;
        public int mInitCreateCount = 0;
    }


    public class LoopGridViewInitParam
    {
        // all the default values
        public float mSmoothDumpRate = 0.3f;
        public float mSnapFinishThreshold = 0.01f;
        public float mSnapVecThreshold = 145;

        public static LoopGridViewInitParam CopyDefaultInitParam()
        {
            return new LoopGridViewInitParam();
        }
    }


    public class LoopGridViewSettingParam
    {
        public object mItemSize = null;
        public object mPadding = null;
        public object mItemPadding = null;
        public object mGridFixedType = null;
        public object mFixedRowOrColumnCount = null;
    }


    public class LoopGridView : MonoBehaviour, IBeginDragHandler, IEndDragHandler, IDragHandler
    {
        class SnapData
        {
            public SnapStatus mSnapStatus = SnapStatus.NoTargetSet;
            public RowColumnPair mSnapTarget;
            public Vector2 mSnapNeedMoveDir;
            public float mTargetSnapVal = 0;
            public float mCurSnapVal = 0;
            public bool mIsForceSnapTo = false;
            public void Clear()
            {
                mSnapStatus = SnapStatus.NoTargetSet;
                mIsForceSnapTo = false;
            }
        }
        class ItemRangeData
        {
            public int mMaxRow;
            public int mMinRow;
            public int mMaxColumn;
            public int mMinColumn;
            public Vector2 mCheckedPosition;
        }

        Dictionary<string, GridItemPool> mItemPoolDict = new Dictionary<string, GridItemPool>();
        List<GridItemPool> mItemPoolList = new List<GridItemPool>();
        [SerializeField]
        List<GridViewItemPrefabConfData> mItemPrefabDataList = new List<GridViewItemPrefabConfData>();

        [SerializeField]
        private GridItemArrangeType mArrangeType = GridItemArrangeType.TopLeftToBottomRight;
        public GridItemArrangeType ArrangeType { get { return mArrangeType; } set { mArrangeType = value; } }
        RectTransform mContainerTrans;
        ScrollRect mScrollRect = null;
        RectTransform mScrollRectTransform = null;
        RectTransform mViewPortRectTransform = null;
        int mItemTotalCount = 0;
        [SerializeField]
        int mFixedRowOrColumnCount = 0;
        [SerializeField]
        RectOffset mPadding = new RectOffset();
        [SerializeField]
        Vector2 mItemPadding = Vector2.zero;
        [SerializeField]
        Vector2 mItemSize = Vector2.zero;
        [SerializeField]
        Vector2 mItemRecycleDistance = new Vector2(50,50);
        Vector2 mItemSizeWithPadding = Vector2.zero;
        Vector2 mStartPadding;
        Vector2 mEndPadding;
        System.Func<LoopGridView,int,int,int, LoopGridViewItem> mOnGetItemByRowColumn;
        List<GridItemGroup> mItemGroupObjPool = new List<GridItemGroup>();

        //if GridFixedType is GridFixedType.ColumnCountFixed, then the GridItemGroup is one row of the GridView
        //if GridFixedType is GridFixedType.RowCountFixed, then the GridItemGroup is one column of the GridView
        //so mItemGroupList is current all shown rows or columns
        List<GridItemGroup> mItemGroupList = new List<GridItemGroup>();

        bool mIsDraging = false;
        int mRowCount = 0;
        int mColumnCount = 0;
        public System.Action<PointerEventData> mOnBeginDragAction = null;
        public System.Action<PointerEventData> mOnDragingAction = null;
        public System.Action<PointerEventData> mOnEndDragAction = null;
        float mSmoothDumpVel = 0;
        float mSmoothDumpRate = 0.3f;
        float mSnapFinishThreshold = 0.1f;
        float mSnapVecThreshold = 145;
        [SerializeField]
        bool mItemSnapEnable = false;
        [SerializeField]
        GridFixedType mGridFixedType = GridFixedType.ColumnCountFixed;
        public System.Action<LoopGridView, LoopGridViewItem> mOnSnapItemFinished = null;
        //in this callback, use CurSnapNearestItemRowColumn to get cur snaped item row column.
        public System.Action<LoopGridView> mOnSnapNearestChanged = null;
        int mLeftSnapUpdateExtraCount = 1;
        [SerializeField]
        Vector2 mViewPortSnapPivot = Vector2.zero;
        [SerializeField]
        Vector2 mItemSnapPivot = Vector2.zero;
        SnapData mCurSnapData = new SnapData();
        Vector3 mLastSnapCheckPos = Vector3.zero;
        bool mListViewInited = false;
        int mListUpdateCheckFrameCount = 0;
        ItemRangeData mCurFrameItemRangeData = new ItemRangeData();
        int mNeedCheckContentPosLeftCount = 1;
        ClickEventListener mScrollBarClickEventListener1 = null;
        ClickEventListener mScrollBarClickEventListener2 = null;
        RowColumnPair mCurSnapNearestItemRowColumn;
        bool mIsPointerDownInVerticalScrollBar = false;
        bool mIsPointerDownInHorizontalScrollBar = false;
        bool mNeedReplaceScrollbarEventHandler = true;
        int mCurCreatingItemIndex = -1;



        public bool IsListViewInited
        {
            get
            {
                return mListViewInited;
            }
        }


        public List<GridViewItemPrefabConfData> ItemPrefabDataList
        {
            get
            {
                return mItemPrefabDataList;
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

        public float ViewPortWidth
        {
            get { return mViewPortRectTransform.rect.width; }
        }

        public float ViewPortHeight
        {
            get { return mViewPortRectTransform.rect.height; }
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

        public bool ItemSnapEnable
        {
            get {return mItemSnapEnable;}
            set { mItemSnapEnable = value; }
        }

        public Vector2 ItemSize
        {
            get
            {
                return mItemSize;
            }
            set
            {
                SetItemSize(value);
            }
        }

        public Vector2 ItemPadding
        {
            get
            {
                return mItemPadding;
            }
            set
            {
                SetItemPadding(value);
            }
        }

        public Vector2 ItemSizeWithPadding
        {
            get
            {
                return mItemSizeWithPadding;
            }
        }
        public RectOffset Padding
        {
            get
            {
                return mPadding;
            }
            set
            {
                SetPadding(value);
            }
        }


        public GridViewItemPrefabConfData GetItemPrefabConfData(string prefabName)
        {
            foreach (GridViewItemPrefabConfData data in mItemPrefabDataList)
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
        LoopGridView method is to initiate the LoopGridView component. There are 4 parameters:
        itemTotalCount: the total item count in the GridView, this parameter must be set a value >=0 , then the ItemIndex can be from 0 to itemTotalCount -1.
        onGetItemByRowColumn: when a item is getting in the ScrollRect viewport, and this Action will be called with the item' index and the row and column index as the parameters, to let you create the item and update its content.
        settingParam: You can use this parameter to override the values in the Inspector Setting
        */
        public void InitGridView(int itemTotalCount, 
            System.Func<LoopGridView,int,int,int, LoopGridViewItem> onGetItemByRowColumn, 
            LoopGridViewSettingParam settingParam = null,
            LoopGridViewInitParam initParam = null)
        {
            if (mListViewInited == true)
            {
                Debug.LogError("LoopGridView.InitListView method can be called only once.");
                return;
            }
            mListViewInited = true;
            if (itemTotalCount < 0)
            {
                Debug.LogError("itemTotalCount is  < 0");
                itemTotalCount = 0;
            }
            if(settingParam != null)
            {
                UpdateFromSettingParam(settingParam);
            }
            if(initParam != null)
            {
                mSmoothDumpRate = initParam.mSmoothDumpRate;
                mSnapFinishThreshold = initParam.mSnapFinishThreshold;
                mSnapVecThreshold = initParam.mSnapVecThreshold;
            }
            mScrollRect = gameObject.GetComponent<ScrollRect>();
            if (mScrollRect == null)
            {
                Debug.LogError("ListView Init Failed! ScrollRect component not found!");
                return;
            }
            mCurSnapData.Clear();
            mScrollRectTransform = mScrollRect.GetComponent<RectTransform>();
            mContainerTrans = mScrollRect.content;
            mViewPortRectTransform = mScrollRect.viewport;
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
            SetScrollbarListener();
            if(mNeedReplaceScrollbarEventHandler)
            {
                ReplaceScrollbarEventHandlerForSmoothMove();
            }
            AdjustViewPortPivot();
            AdjustContainerAnchorAndPivot();
            InitItemPool();
            mOnGetItemByRowColumn = onGetItemByRowColumn;
            mNeedCheckContentPosLeftCount = 4;
            mCurSnapData.Clear();
            mItemTotalCount = itemTotalCount;
            UpdateAllGridSetting();
        }


        /*
        This method may use to set the item total count of the GridView at runtime. 
        this parameter must be set a value >=0 , and the ItemIndex can be from 0 to itemCount -1.  
        If resetPos is set false, then the ScrollRect’s content position will not changed after this method finished.
        */
        public void SetListItemCount(int itemCount, bool resetPos = true)
        {
            if(itemCount < 0)
            {
                return;
            }
            if(itemCount == mItemTotalCount)
            {
                return;
            }
            mCurSnapData.Clear();
            mItemTotalCount = itemCount;
            UpdateColumnRowCount();
            UpdateContentSize();
            ForceToCheckContentPos();
            if (mItemTotalCount == 0)
            {
                RecycleAllItem();
                ClearAllTmpRecycledItem();
                return;
            }
            VaildAndSetContainerPos();
            UpdateGridViewContent();
            ClearAllTmpRecycledItem();
            if (resetPos)
            {
                MovePanelToItemByRowColumn(0,0);
                return;
            }
        }

       //fetch or create a new item form the item pool.
        public LoopGridViewItem NewListViewItem(string itemPrefabName)
        {
            GridItemPool pool = null;
            if (mItemPoolDict.TryGetValue(itemPrefabName, out pool) == false)
            {
                return null;
            }
            LoopGridViewItem item = pool.GetItem(mCurCreatingItemIndex);
            RectTransform rf = item.GetComponent<RectTransform>();
            rf.SetParent(mContainerTrans);
            rf.localScale = Vector3.one;
            rf.anchoredPosition3D = Vector3.zero;
            rf.localEulerAngles = Vector3.zero;
            item.ParentGridView = this;
            return item;
        }

        public void ClearAllShownItems()
        {
            RecycleAllItem();
            ClearAllTmpRecycledItem();
        }


        /*
        To update a item by itemIndex.if the itemIndex-th item is not visible, then this method will do nothing.
        Otherwise this method will call RefreshItemByRowColumn to do real work.
        */
        public void RefreshItemByItemIndex(int itemIndex)
        {
            if(itemIndex < 0 || itemIndex >= ItemTotalCount)
            {
                return;
            }
            int count = mItemGroupList.Count;
            if (count == 0)
            {
                return;
            }
            RowColumnPair val = GetRowColumnByItemIndex(itemIndex);
            RefreshItemByRowColumn(val.mRow, val.mColumn);
        }


        /*
        To update a item by (row,column).if the item is not visible, then this method will do nothing.
        Otherwise this method will call mOnGetItemByRowColumn(row,column) to get a new updated item. 
        */
        public void RefreshItemByRowColumn(int row,int column)
        {
            int count = mItemGroupList.Count;
            if (count == 0)
            {
                return;
            }
            if (mGridFixedType == GridFixedType.ColumnCountFixed)
            {
                GridItemGroup group = GetShownGroup(row);
                if (group == null)
                {
                    return;
                }
                LoopGridViewItem curItem = group.GetItemByColumn(column);
                if(curItem == null)
                {
                    return;
                }
                LoopGridViewItem newItem = GetNewItemByRowColumn(row, column);
                if (newItem == null)
                {
                    return;
                }
                Vector3 pos = curItem.CachedRectTransform.anchoredPosition3D;
                group.ReplaceItem(curItem, newItem);
                RecycleItemTmp(curItem);
                newItem.CachedRectTransform.anchoredPosition3D = pos;
                ClearAllTmpRecycledItem();
            }
            else
            {
                GridItemGroup group = GetShownGroup(column);
                if (group == null)
                {
                    return;
                }
                LoopGridViewItem curItem = group.GetItemByRow(row);
                if (curItem == null)
                {
                    return;
                }
                LoopGridViewItem newItem = GetNewItemByRowColumn(row, column);
                if (newItem == null)
                {
                    return;
                }
                Vector3 pos = curItem.CachedRectTransform.anchoredPosition3D;
                group.ReplaceItem(curItem, newItem);
                RecycleItemTmp(curItem);
                newItem.CachedRectTransform.anchoredPosition3D = pos;
                ClearAllTmpRecycledItem();
            }
        }

        //Clear current snap target and then the GridView will auto snap to the CurSnapNearestItem.
        public void ClearSnapData()
        {
            mCurSnapData.Clear();
        }

        //set cur snap target
        public void SetSnapTargetItemRowColumn(int row, int column)
        {
            if(row < 0)
            {
                row = 0;
            }
            if(column < 0)
            {
                column = 0;
            }
            mCurSnapData.mSnapTarget.mRow = row;
            mCurSnapData.mSnapTarget.mColumn = column;
            mCurSnapData.mSnapStatus = SnapStatus.TargetHasSet;
            mCurSnapData.mIsForceSnapTo = true;
        }

        //Get the nearest item row and column with the viewport snap point.
        public RowColumnPair CurSnapNearestItemRowColumn
        {
            get { return mCurSnapNearestItemRowColumn; }
        }


        //force to update the mCurSnapNearestItemRowColumn value
        public void ForceSnapUpdateCheck()
        {
            if (mLeftSnapUpdateExtraCount <= 0)
            {
                mLeftSnapUpdateExtraCount = 1;
            }
        }

        //force to refresh the mCurFrameItemRangeData that what items should be shown in viewport.
        public void ForceToCheckContentPos()
        {
            if (mNeedCheckContentPosLeftCount <= 0)
            {
                mNeedCheckContentPosLeftCount = 1;
            }
        }

        /*
        This method will move the panel's position to ( the position of itemIndex'th item + offset ).
        */
        public void MovePanelToItemByIndex(int itemIndex, float offsetX = 0, float offsetY = 0)
        {
            if(ItemTotalCount == 0)
            {
                return;
            }
            if(itemIndex >= ItemTotalCount)
            {
                itemIndex = ItemTotalCount - 1;
            }
            if (itemIndex < 0)
            {
                itemIndex = 0;
            }
            RowColumnPair val = GetRowColumnByItemIndex(itemIndex);
            MovePanelToItemByRowColumn(val.mRow, val.mColumn, offsetX, offsetY);
        }

        /*
        This method will move the panel's position to ( the position of (row,column) item + offset ).
        */
        public void MovePanelToItemByRowColumn(int row,int column, float offsetX = 0,float offsetY = 0)
        {
            mScrollRect.StopMovement();
            mCurSnapData.Clear();
            if (mItemTotalCount == 0)
            {
                return;
            }
            Vector2 itemPos = GetItemPos(row, column);
            Vector3 pos = mContainerTrans.anchoredPosition3D;
            if (mScrollRect.horizontal)
            {
                float maxCanMoveX = Mathf.Max(ContainerTrans.rect.width - ViewPortWidth, 0);
                if(maxCanMoveX > 0)
                {
                    float x = -itemPos.x + offsetX;
                    x = Mathf.Min(Mathf.Abs(x), maxCanMoveX) * Mathf.Sign(x);
                    pos.x = x;
                } 
            }
            if(mScrollRect.vertical)
            {
                float maxCanMoveY = Mathf.Max(ContainerTrans.rect.height - ViewPortHeight, 0);
                if(maxCanMoveY > 0)
                {
                    float y = -itemPos.y + offsetY;
                    y = Mathf.Min(Mathf.Abs(y), maxCanMoveY) * Mathf.Sign(y);
                    pos.y = y;
                }
            }
            if(pos != mContainerTrans.anchoredPosition3D)
            {
                mContainerTrans.anchoredPosition3D = pos;
            }
            VaildAndSetContainerPos();
            ForceToCheckContentPos();
        }

        //update all visible items.
        public void RefreshAllShownItem()
        {
            int count = mItemGroupList.Count;
            if (count == 0)
            {
                return;
            }
            ForceToCheckContentPos();
            RecycleAllItem();
            UpdateGridViewContent();
        }

        //update the gridview content at once. You can call this method to let the LoopGridView to create all the items within the ScrollRect viewport immediately.
        public void ForceUpdateGridViewAtOnce()
        {
            if (mListViewInited == false)
            {
                return;
            }
            UpdateSnapMove();
            ForceToCheckContentPos();
            UpdateGridViewContent();
            ClearAllTmpRecycledItem();
        }

        public virtual void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }
            mCurSnapData.Clear();
            mIsDraging = true;
            if (mOnBeginDragAction != null)
            {
                mOnBeginDragAction(eventData);
            }
        }

        public virtual void OnEndDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }
            mIsDraging = false;
            ForceSnapUpdateCheck();
            if (mOnEndDragAction != null)
            {
                mOnEndDragAction(eventData);
            }
        }

        public virtual void OnDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }
            if (mOnDragingAction != null)
            {
                mOnDragingAction(eventData);
            }
        }


        public int GetItemIndexByRowColumn(int row, int column)
        {
            if (mGridFixedType == GridFixedType.ColumnCountFixed)
            {
                return row * mFixedRowOrColumnCount + column;
            }
            else
            {
                return column * mFixedRowOrColumnCount + row;
            }
        }


        public RowColumnPair GetRowColumnByItemIndex(int itemIndex)
        {
            if(itemIndex < 0)
            {
                itemIndex = 0;
            }
            if (mGridFixedType == GridFixedType.ColumnCountFixed)
            {
                int row = itemIndex / mFixedRowOrColumnCount;
                int column = itemIndex % mFixedRowOrColumnCount;
                return new RowColumnPair(row, column);
            }
            else
            {
                int column = itemIndex / mFixedRowOrColumnCount;
                int row = itemIndex % mFixedRowOrColumnCount;
                return new RowColumnPair(row, column);
            }
        }


        public Vector2 GetItemAbsPos(int row, int column)
        {
            float x = mStartPadding.x + column * mItemSizeWithPadding.x;
            float y = mStartPadding.y + row * mItemSizeWithPadding.y;
            return new Vector2(x, y);
        }


        public Vector2 GetItemPos(int row, int column)
        {
            Vector2 absPos = GetItemAbsPos(row, column);
            float x = absPos.x;
            float y = absPos.y;
            if (ArrangeType == GridItemArrangeType.TopLeftToBottomRight)
            {
                return new Vector2(x, -y);
            }
            else if (ArrangeType == GridItemArrangeType.BottomLeftToTopRight)
            {
                return new Vector2(x, y);
            }
            else if (ArrangeType == GridItemArrangeType.TopRightToBottomLeft)
            {
                return new Vector2(-x, -y);
            }
            else if (ArrangeType == GridItemArrangeType.BottomRightToTopLeft)
            {
                return new Vector2(-x, y);
            }
            return Vector2.zero;
        }

        //get the shown item of itemIndex, if this item is not shown,then return null.
        public LoopGridViewItem GetShownItemByItemIndex(int itemIndex)
        {
            if(itemIndex < 0 || itemIndex >= ItemTotalCount)
            {
                return null;
            }
            if(mItemGroupList.Count == 0)
            {
                return null;
            }
            RowColumnPair val = GetRowColumnByItemIndex(itemIndex);
            return GetShownItemByRowColumn(val.mRow, val.mColumn);
        }

        //get the shown item of (row, column), if this item is not shown,then return null.
        public LoopGridViewItem GetShownItemByRowColumn(int row, int column)
        {
            if (mItemGroupList.Count == 0)
            {
                return null;
            }
            if (mGridFixedType == GridFixedType.ColumnCountFixed)
            {
                GridItemGroup group = GetShownGroup(row);
                if (group == null)
                {
                    return null;
                }
                return group.GetItemByColumn(column);
            }
            else
            {
                GridItemGroup group = GetShownGroup(column);
                if (group == null)
                {
                    return null;
                }
                return group.GetItemByRow(row);
            }
        }

        public void UpdateAllGridSetting()
        {
            UpdateStartEndPadding();
            UpdateItemSize();
            UpdateColumnRowCount();
            UpdateContentSize();
            ForceSnapUpdateCheck();
            ForceToCheckContentPos();
        }

        //set mGridFixedType and mFixedRowOrColumnCount at runtime
        public void SetGridFixedGroupCount(GridFixedType fixedType,int count)
        {
            if(mGridFixedType == fixedType && mFixedRowOrColumnCount == count)
            {
                return;
            }
            mGridFixedType = fixedType;
            mFixedRowOrColumnCount = count;
            UpdateColumnRowCount();
            UpdateContentSize();
            if (mItemGroupList.Count == 0)
            {
                return;
            }
            RecycleAllItem();
            ForceSnapUpdateCheck();
            ForceToCheckContentPos();
        }
        //change item size at runtime
        public void SetItemSize(Vector2 newSize)
        {
            if (newSize == mItemSize)
            {
                return;
            }
            mItemSize = newSize;
            UpdateItemSize();
            UpdateContentSize();
            if (mItemGroupList.Count == 0)
            {
                return;
            }
            RecycleAllItem();
            ForceSnapUpdateCheck();
            ForceToCheckContentPos();
        }
        //change item padding at runtime
        public void SetItemPadding(Vector2 newPadding)
        {
            if (newPadding == mItemPadding)
            {
                return;
            }
            mItemPadding = newPadding;
            UpdateItemSize();
            UpdateContentSize();
            if (mItemGroupList.Count == 0)
            {
                return;
            }
            RecycleAllItem();
            ForceSnapUpdateCheck();
            ForceToCheckContentPos();
        }
        //change padding at runtime
        public void SetPadding(RectOffset newPadding)
        {
            if (newPadding == mPadding)
            {
                return;
            }
            mPadding = newPadding;
            UpdateStartEndPadding();
            UpdateContentSize();
            if (mItemGroupList.Count == 0)
            {
                return;
            }
            RecycleAllItem();
            ForceSnapUpdateCheck();
            ForceToCheckContentPos();
        }


        public void UpdateContentSize()
        {
            float width = mStartPadding.x + mColumnCount * mItemSizeWithPadding.x - mItemPadding.x + mEndPadding.x;
            float height = mStartPadding.y + mRowCount * mItemSizeWithPadding.y - mItemPadding.y + mEndPadding.y;
            if (mContainerTrans.rect.height != height)
            {
                mContainerTrans.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            }
            if (mContainerTrans.rect.width != width)
            {
                mContainerTrans.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            }
        }


        public void VaildAndSetContainerPos()
        {
            Vector3 pos = mContainerTrans.anchoredPosition3D;
            mContainerTrans.anchoredPosition3D = GetContainerVaildPos(pos.x, pos.y);
        }

        public void ClearAllTmpRecycledItem()
        {
            int count = mItemPoolList.Count;
            for (int i = 0; i < count; ++i)
            {
                mItemPoolList[i].ClearTmpRecycledItem();
            }
        }


        public void RecycleAllItem()
        {
            foreach (GridItemGroup group in mItemGroupList)
            {
                RecycleItemGroupTmp(group);
            }
            mItemGroupList.Clear();
        }

        public void UpdateGridViewContent()
        {
            mListUpdateCheckFrameCount++;
            if (mItemTotalCount == 0)
            {
                if (mItemGroupList.Count > 0)
                {
                    RecycleAllItem();
                }
                return;
            }
            UpdateCurFrameItemRangeData();
            if (mGridFixedType == GridFixedType.ColumnCountFixed)
            {
                int groupCount = mItemGroupList.Count;
                int minRow = mCurFrameItemRangeData.mMinRow;
                int maxRow = mCurFrameItemRangeData.mMaxRow;
                for (int i = groupCount - 1; i >= 0; --i)
                {
                    GridItemGroup group = mItemGroupList[i];
                    if (group.GroupIndex < minRow || group.GroupIndex > maxRow)
                    {
                        RecycleItemGroupTmp(group);
                        mItemGroupList.RemoveAt(i);
                    }
                }
                if (mItemGroupList.Count == 0)
                {
                    GridItemGroup group = CreateItemGroup(minRow);
                    mItemGroupList.Add(group);
                }
                while (mItemGroupList[0].GroupIndex > minRow)
                {
                    GridItemGroup group = CreateItemGroup(mItemGroupList[0].GroupIndex - 1);
                    mItemGroupList.Insert(0, group);
                }
                while (mItemGroupList[mItemGroupList.Count - 1].GroupIndex < maxRow)
                {
                    GridItemGroup group = CreateItemGroup(mItemGroupList[mItemGroupList.Count - 1].GroupIndex + 1);
                    mItemGroupList.Add(group);
                }
                int count = mItemGroupList.Count;
                for (int i = 0; i < count; ++i)
                {
                    UpdateRowItemGroupForRecycleAndNew(mItemGroupList[i]);
                }
            }
            else
            {
                int groupCount = mItemGroupList.Count;
                int minColumn = mCurFrameItemRangeData.mMinColumn;
                int maxColumn = mCurFrameItemRangeData.mMaxColumn;
                for (int i = groupCount - 1; i >= 0; --i)
                {
                    GridItemGroup group = mItemGroupList[i];
                    if (group.GroupIndex < minColumn || group.GroupIndex > maxColumn)
                    {
                        RecycleItemGroupTmp(group);
                        mItemGroupList.RemoveAt(i);
                    }
                }
                if (mItemGroupList.Count == 0)
                {
                    GridItemGroup group = CreateItemGroup(minColumn);
                    mItemGroupList.Add(group);
                }
                while (mItemGroupList[0].GroupIndex > minColumn)
                {
                    GridItemGroup group = CreateItemGroup(mItemGroupList[0].GroupIndex - 1);
                    mItemGroupList.Insert(0, group);
                }
                while (mItemGroupList[mItemGroupList.Count - 1].GroupIndex < maxColumn)
                {
                    GridItemGroup group = CreateItemGroup(mItemGroupList[mItemGroupList.Count - 1].GroupIndex + 1);
                    mItemGroupList.Add(group);
                }
                int count = mItemGroupList.Count;
                for (int i = 0; i < count; ++i)
                {
                    UpdateColumnItemGroupForRecycleAndNew(mItemGroupList[i]);
                }
            }
        }

        public void UpdateStartEndPadding()
        {
            if (ArrangeType == GridItemArrangeType.TopLeftToBottomRight)
            {
                mStartPadding.x = mPadding.left;
                mStartPadding.y = mPadding.top;
                mEndPadding.x = mPadding.right;
                mEndPadding.y = mPadding.bottom;
            }
            else if (ArrangeType == GridItemArrangeType.BottomLeftToTopRight)
            {
                mStartPadding.x = mPadding.left;
                mStartPadding.y = mPadding.bottom;
                mEndPadding.x = mPadding.right;
                mEndPadding.y = mPadding.top;
            }
            else if (ArrangeType == GridItemArrangeType.TopRightToBottomLeft)
            {
                mStartPadding.x = mPadding.right;
                mStartPadding.y = mPadding.top;
                mEndPadding.x = mPadding.left;
                mEndPadding.y = mPadding.bottom;
            }
            else if (ArrangeType == GridItemArrangeType.BottomRightToTopLeft)
            {
                mStartPadding.x = mPadding.right;
                mStartPadding.y = mPadding.bottom;
                mEndPadding.x = mPadding.left;
                mEndPadding.y = mPadding.top;
            }
        }


        public void UpdateItemSize()
        {
            if (mItemSize.x > 0f && mItemSize.y > 0f)
            {
                mItemSizeWithPadding = mItemSize + mItemPadding;
                return;
            }
            do
            {
                if (mItemPrefabDataList.Count == 0)
                {
                    break;
                }
                GameObject obj = mItemPrefabDataList[0].mItemPrefab;
                if (obj == null)
                {
                    break;
                }
                RectTransform rtf = obj.GetComponent<RectTransform>();
                if (rtf == null)
                {
                    break;
                }
                mItemSize = rtf.rect.size;
                mItemSizeWithPadding = mItemSize + mItemPadding;

            } while (false);

            if (mItemSize.x <= 0 || mItemSize.y <= 0)
            {
                Debug.LogError("Error, ItemSize is invaild.");
            }

        }

        public void UpdateColumnRowCount()
        {
            if (mGridFixedType == GridFixedType.ColumnCountFixed)
            {
                mColumnCount = mFixedRowOrColumnCount;
                mRowCount = mItemTotalCount / mColumnCount;
                if (mItemTotalCount % mColumnCount > 0)
                {
                    mRowCount++;
                }
                if (mItemTotalCount <= mColumnCount)
                {
                    mColumnCount = mItemTotalCount;
                }
            }
            else
            {
                mRowCount = mFixedRowOrColumnCount;
                mColumnCount = mItemTotalCount / mRowCount;
                if (mItemTotalCount % mRowCount > 0)
                {
                    mColumnCount++;
                }
                if (mItemTotalCount <= mRowCount)
                {
                    mRowCount = mItemTotalCount;
                }
            }
        }




        /// ///////////////////////////////////////////////////////////////////////////////////////////////////////
        /// </summary>


        bool IsContainerTransCanMove()
        {
            if (mItemTotalCount == 0)
            {
                return false;
            }
            if (mScrollRect.horizontal && ContainerTrans.rect.width > ViewPortWidth)
            {
                return true;
            }
            if (mScrollRect.vertical && ContainerTrans.rect.height > ViewPortHeight)
            {
                return true;
            }
            return false;
        }



        void RecycleItemGroupTmp(GridItemGroup group)
        {
            if (group == null)
            {
                return;
            }
            while(group.First != null)
            {
                LoopGridViewItem item = group.RemoveFirst();
                RecycleItemTmp(item);
            }
            group.Clear();
            RecycleOneItemGroupObj(group);
        }



        void RecycleItemTmp(LoopGridViewItem item)
        {
            if (item == null)
            {
                return;
            }
            if (string.IsNullOrEmpty(item.ItemPrefabName))
            {
                return;
            }
            GridItemPool pool = null;
            if (mItemPoolDict.TryGetValue(item.ItemPrefabName, out pool) == false)
            {
                return;
            }
            pool.RecycleItem(item);

        }


        void AdjustViewPortPivot()
        {
            RectTransform rtf = mViewPortRectTransform;
            if (ArrangeType == GridItemArrangeType.TopLeftToBottomRight)
            {
                rtf.pivot = new Vector2(0, 1);
            }
            else if (ArrangeType == GridItemArrangeType.BottomLeftToTopRight)
            {
                rtf.pivot = new Vector2(0, 0);
            }
            else if (ArrangeType == GridItemArrangeType.TopRightToBottomLeft)
            {
                rtf.pivot = new Vector2(1, 1);
            }
            else if (ArrangeType == GridItemArrangeType.BottomRightToTopLeft)
            {
                rtf.pivot = new Vector2(1, 0);
            }
        }

        void AdjustContainerAnchorAndPivot()
        {
            RectTransform rtf = ContainerTrans;

            if (ArrangeType == GridItemArrangeType.TopLeftToBottomRight)
            {
                rtf.anchorMin = new Vector2(0, 1);
                rtf.anchorMax = new Vector2(0, 1);
                rtf.pivot = new Vector2(0, 1);
            }
            else if (ArrangeType == GridItemArrangeType.BottomLeftToTopRight)
            {
                rtf.anchorMin = new Vector2(0, 0);
                rtf.anchorMax = new Vector2(0, 0);
                rtf.pivot = new Vector2(0, 0);
            }
            else if (ArrangeType == GridItemArrangeType.TopRightToBottomLeft)
            {
                rtf.anchorMin = new Vector2(1, 1);
                rtf.anchorMax = new Vector2(1, 1);
                rtf.pivot = new Vector2(1, 1);
            }
            else if (ArrangeType == GridItemArrangeType.BottomRightToTopLeft)
            {
                rtf.anchorMin = new Vector2(1, 0);
                rtf.anchorMax = new Vector2(1, 0);
                rtf.pivot = new Vector2(1, 0);
            }
        }

        void AdjustItemAnchorAndPivot(RectTransform rtf)
        {
            if (ArrangeType == GridItemArrangeType.TopLeftToBottomRight)
            {
                rtf.anchorMin = new Vector2(0, 1);
                rtf.anchorMax = new Vector2(0, 1);
                rtf.pivot = new Vector2(0, 1);
            }
            else if (ArrangeType == GridItemArrangeType.BottomLeftToTopRight)
            {
                rtf.anchorMin = new Vector2(0, 0);
                rtf.anchorMax = new Vector2(0, 0);
                rtf.pivot = new Vector2(0, 0);
            }
            else if (ArrangeType == GridItemArrangeType.TopRightToBottomLeft)
            {
                rtf.anchorMin = new Vector2(1, 1);
                rtf.anchorMax = new Vector2(1, 1);
                rtf.pivot = new Vector2(1, 1);
            }
            else if (ArrangeType == GridItemArrangeType.BottomRightToTopLeft)
            {
                rtf.anchorMin = new Vector2(1, 0);
                rtf.anchorMax = new Vector2(1, 0);
                rtf.pivot = new Vector2(1, 0);
            }
        }





        void InitItemPool()
        {
            foreach (GridViewItemPrefabConfData data in mItemPrefabDataList)
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
                AdjustItemAnchorAndPivot(rtf);
                LoopGridViewItem tItem = data.mItemPrefab.GetComponent<LoopGridViewItem>();
                if (tItem == null)
                {
                    data.mItemPrefab.AddComponent<LoopGridViewItem>();
                }
                GridItemPool pool = new GridItemPool();
                pool.Init(data.mItemPrefab,data.mInitCreateCount, mContainerTrans);
                mItemPoolDict.Add(prefabName, pool);
                mItemPoolList.Add(pool);
            }
        }


        LoopGridViewItem GetNewItemByRowColumn(int row,int column)
        {
            int itemIndex = GetItemIndexByRowColumn(row, column);
            if(itemIndex < 0 || itemIndex >= ItemTotalCount)
            {
                return null;
            }
            LoopGridViewItem newItem = mOnGetItemByRowColumn(this,itemIndex,row,column);
            if (newItem == null)
            {
                return null;
            }
            newItem.NextItem = null;
            newItem.PrevItem = null;
            newItem.Row = row;
            newItem.Column = column;
            newItem.ItemIndex = itemIndex;
            newItem.ItemCreatedCheckFrameCount = mListUpdateCheckFrameCount;
            return newItem;
        }


        RowColumnPair GetCeilItemRowColumnAtGivenAbsPos(float ax,float ay)
        {
            ax = Mathf.Abs(ax);
            ay = Mathf.Abs(ay);
            int row = Mathf.CeilToInt((ay - mStartPadding.y) / mItemSizeWithPadding.y)-1;
            int column = Mathf.CeilToInt((ax - mStartPadding.x) / mItemSizeWithPadding.x)-1;
            if(row < 0)
            {
                row = 0;
            }
            if(row >= mRowCount)
            {
                row = mRowCount - 1;
            }
            if(column < 0)
            {
                column = 0;
            }
            if(column >= mColumnCount)
            {
                column = mColumnCount - 1;
            }
            return new RowColumnPair(row,column);
        }

        void Update()
        {
            if(mListViewInited == false)
            {
                return;
            }
            UpdateSnapMove();
            UpdateGridViewContent();
            ClearAllTmpRecycledItem();
        }


        GridItemGroup CreateItemGroup(int groupIndex)
        {
            GridItemGroup ret = GetOneItemGroupObj();
            ret.GroupIndex = groupIndex;
            return ret;
        }
        Vector2 GetContainerMovedDistance()
        {
            Vector2 pos = GetContainerVaildPos(ContainerTrans.anchoredPosition3D.x, ContainerTrans.anchoredPosition3D.y);
            return new Vector2(Mathf.Abs(pos.x), Mathf.Abs(pos.y));
        }


        Vector2 GetContainerVaildPos(float curX, float curY)
        {
            float maxCanMoveX = Mathf.Max(ContainerTrans.rect.width - ViewPortWidth, 0);
            float maxCanMoveY = Mathf.Max(ContainerTrans.rect.height - ViewPortHeight, 0);
            if (mArrangeType == GridItemArrangeType.TopLeftToBottomRight)
            {
                curX = Mathf.Clamp(curX, -maxCanMoveX, 0);
                curY = Mathf.Clamp(curY, 0, maxCanMoveY);
            }
            else if (mArrangeType == GridItemArrangeType.BottomLeftToTopRight)
            {
                curX = Mathf.Clamp(curX, -maxCanMoveX, 0);
                curY = Mathf.Clamp(curY, -maxCanMoveY,0);
            }
            else if (mArrangeType == GridItemArrangeType.BottomRightToTopLeft)
            {
                curX = Mathf.Clamp(curX, 0, maxCanMoveX);
                curY = Mathf.Clamp(curY, -maxCanMoveY,0);

            }
            else if (mArrangeType == GridItemArrangeType.TopRightToBottomLeft)
            {
                curX = Mathf.Clamp(curX, 0,maxCanMoveX);
                curY = Mathf.Clamp(curY, 0,maxCanMoveY);
            }
            return new Vector2(curX, curY);
        }


        void UpdateCurFrameItemRangeData()
        {
            Vector2 distVector2 = GetContainerMovedDistance();
            if (mNeedCheckContentPosLeftCount <= 0 && mCurFrameItemRangeData.mCheckedPosition == distVector2)
            {
               return;
            }
            if (mNeedCheckContentPosLeftCount > 0)
            {
                mNeedCheckContentPosLeftCount--;
            }
            float distX = distVector2.x - mItemRecycleDistance.x;
            float distY = distVector2.y - mItemRecycleDistance.y;
            if(distX < 0)
            {
                distX = 0;
            }
            if(distY < 0)
            {
                distY = 0;
            }
            RowColumnPair val = GetCeilItemRowColumnAtGivenAbsPos(distX, distY);
            mCurFrameItemRangeData.mMinColumn = val.mColumn;
            mCurFrameItemRangeData.mMinRow = val.mRow;
            distX = distVector2.x + mItemRecycleDistance.x + ViewPortWidth;
            distY = distVector2.y + mItemRecycleDistance.y + ViewPortHeight;
            val = GetCeilItemRowColumnAtGivenAbsPos(distX, distY);
            mCurFrameItemRangeData.mMaxColumn = val.mColumn;
            mCurFrameItemRangeData.mMaxRow = val.mRow;
            mCurFrameItemRangeData.mCheckedPosition = distVector2;
        }

       


        void UpdateRowItemGroupForRecycleAndNew(GridItemGroup group)
        {
            int minColumn = mCurFrameItemRangeData.mMinColumn;
            int maxColumn = mCurFrameItemRangeData.mMaxColumn;
            int row = group.GroupIndex;
            while(group.First != null && group.First.Column < minColumn)
            {
                RecycleItemTmp(group.RemoveFirst());
            }
            while (group.Last != null && ( ( group.Last.Column > maxColumn ) || ( group.Last.ItemIndex >= ItemTotalCount ) ) )
            {
                RecycleItemTmp(group.RemoveLast());
            }
            if(group.First == null)
            {
                LoopGridViewItem item = GetNewItemByRowColumn(row, minColumn);
                if(item == null)
                {
                    return;
                }
                item.CachedRectTransform.anchoredPosition3D = GetItemPos(item.Row, item.Column);
                group.AddFirst(item);
            }
            while (group.First.Column > minColumn)
            {
                LoopGridViewItem item = GetNewItemByRowColumn(row, group.First.Column-1);
                if (item == null)
                {
                    break;
                }
                item.CachedRectTransform.anchoredPosition3D = GetItemPos(item.Row, item.Column);

                group.AddFirst(item);
            }
            while (group.Last.Column < maxColumn)
            {
                LoopGridViewItem item = GetNewItemByRowColumn(row, group.Last.Column + 1);
                if (item == null)
                {
                    break;
                }
                item.CachedRectTransform.anchoredPosition3D = GetItemPos(item.Row, item.Column);

                group.AddLast(item);
            }
        }

        void UpdateColumnItemGroupForRecycleAndNew(GridItemGroup group)
        {
            int minRow = mCurFrameItemRangeData.mMinRow;
            int maxRow = mCurFrameItemRangeData.mMaxRow;
            int column = group.GroupIndex;
            while (group.First != null && group.First.Row < minRow)
            {
                RecycleItemTmp(group.RemoveFirst());
            }
            while (group.Last != null && ( ( group.Last.Row > maxRow )|| (group.Last.ItemIndex >= ItemTotalCount)) )
            {
                RecycleItemTmp(group.RemoveLast());
            }
            if (group.First == null)
            {
                LoopGridViewItem item = GetNewItemByRowColumn(minRow, column);
                if (item == null)
                {
                    return;
                }
                item.CachedRectTransform.anchoredPosition3D = GetItemPos(item.Row, item.Column);
                group.AddFirst(item);
            }
            while (group.First.Row > minRow)
            {
                LoopGridViewItem item = GetNewItemByRowColumn(group.First.Row - 1, column);
                if (item == null)
                {
                    break;
                }
                item.CachedRectTransform.anchoredPosition3D = GetItemPos(item.Row, item.Column);

                group.AddFirst(item);
            }
            while (group.Last.Row < maxRow)
            {
                LoopGridViewItem item = GetNewItemByRowColumn(group.Last.Row + 1,column );
                if (item == null)
                {
                    break;
                }
                item.CachedRectTransform.anchoredPosition3D = GetItemPos(item.Row, item.Column);

                group.AddLast(item);
            }
        }


        void SetScrollbarListener()
        {
            mScrollBarClickEventListener1 = null;
            mScrollBarClickEventListener2 = null;
            if (mScrollRect.vertical && mScrollRect.verticalScrollbar != null)
            {
                Scrollbar curScrollBar = mScrollRect.verticalScrollbar;
                ClickEventListener listener = ClickEventListener.Get(curScrollBar.gameObject);
                mScrollBarClickEventListener1 = listener;
                listener.SetPointerUpHandler(OnPointerUpInVerticalScrollBar);
                listener.SetPointerDownHandler(OnPointerDownInVerticalScrollBar);
            }
            if (mScrollRect.horizontal && mScrollRect.horizontalScrollbar != null)
            {
                Scrollbar curScrollBar = mScrollRect.horizontalScrollbar;
                ClickEventListener listener = ClickEventListener.Get(curScrollBar.gameObject);
                mScrollBarClickEventListener2 = listener;
                listener.SetPointerUpHandler(OnPointerUpInHorizontalScrollBar);
                listener.SetPointerDownHandler(OnPointerDownInHorizontalScrollBar);
            }
        }



        void ReplaceScrollbarEventHandlerForSmoothMove()
        {
            if (mScrollRect.vertical && mScrollRect.verticalScrollbar != null)
            {
                Scrollbar curScrollBar = mScrollRect.verticalScrollbar;
                curScrollBar.onValueChanged.RemoveAllListeners();
                curScrollBar.onValueChanged.AddListener(OnVerticalScrollBarValueChanged);
            }
            if (mScrollRect.horizontal && mScrollRect.horizontalScrollbar != null)
            {
                Scrollbar curScrollBar = mScrollRect.horizontalScrollbar;
                curScrollBar.onValueChanged.RemoveAllListeners();
                curScrollBar.onValueChanged.AddListener(OnHorizontalScrollBarValueChanged);
            }
        }

        void OnVerticalScrollBarValueChanged(float value)
        {
            if (mIsPointerDownInVerticalScrollBar)
            {
                mScrollRect.verticalNormalizedPosition = value;
            }
        }

        void OnHorizontalScrollBarValueChanged(float value)
        {
            if (mIsPointerDownInHorizontalScrollBar)
            {
                mScrollRect.horizontalNormalizedPosition = value;
            }
        }



        void OnPointerDownInVerticalScrollBar(GameObject obj)
        {
            mIsPointerDownInVerticalScrollBar = true;
            mCurSnapData.Clear();
        }

        void OnPointerUpInVerticalScrollBar(GameObject obj)
        {
            mIsPointerDownInVerticalScrollBar = false;
            ForceSnapUpdateCheck();
        }

        void OnPointerDownInHorizontalScrollBar(GameObject obj)
        {
            mIsPointerDownInHorizontalScrollBar = true;
            mCurSnapData.Clear();
        }

        void OnPointerUpInHorizontalScrollBar(GameObject obj)
        {
            mIsPointerDownInHorizontalScrollBar = false;
            ForceSnapUpdateCheck();
        }

        RowColumnPair FindNearestItemWithLocalPos(float x,float y)
        {
            Vector2 targetPos = new Vector2(x, y);
            RowColumnPair val = GetCeilItemRowColumnAtGivenAbsPos(targetPos.x, targetPos.y);
            int row = val.mRow;
            int column = val.mColumn;
            float distance = 0;
            RowColumnPair ret = new RowColumnPair(-1, -1);
            Vector2 pos = Vector2.zero;
            float minDistance = float.MaxValue;
            for (int r = row - 1; r <= row + 1; ++r)
            {
                for (int c = column - 1; c <= column + 1; ++c)
                {
                    if (r >= 0 && r < mRowCount && c >= 0 && c < mColumnCount)
                    {
                        pos = GetItemSnapPivotLocalPos(r, c);
                        distance = (pos - targetPos).sqrMagnitude;
                        if(distance < minDistance)
                        {
                            minDistance = distance;
                            ret.mRow = r;
                            ret.mColumn = c;
                        }
                    }
                }
            }
            return ret;
        }

        Vector2 GetItemSnapPivotLocalPos(int row,int column)
        {
            Vector2 absPos = GetItemAbsPos(row, column);
            if (mArrangeType == GridItemArrangeType.TopLeftToBottomRight)
            {
                float x = absPos.x + mItemSize.x * mItemSnapPivot.x;
                float y = -absPos.y - mItemSize.y * (1 - mItemSnapPivot.y);
                return new Vector2(x, y);
            }
            else if(mArrangeType == GridItemArrangeType.BottomLeftToTopRight)
            {
                float x = absPos.x + mItemSize.x * mItemSnapPivot.x;
                float y = absPos.y + mItemSize.y * mItemSnapPivot.y;
                return new Vector2(x, y);
            }
            else if (mArrangeType == GridItemArrangeType.TopRightToBottomLeft)
            {
                float x = -absPos.x - mItemSize.x * (1-mItemSnapPivot.x);
                float y = -absPos.y - mItemSize.y * (1-mItemSnapPivot.y);
                return new Vector2(x, y);
            }
            else if (mArrangeType == GridItemArrangeType.BottomRightToTopLeft)
            {
                float x = -absPos.x - mItemSize.x * (1-mItemSnapPivot.x);
                float y = absPos.y + mItemSize.y * mItemSnapPivot.y;
                return new Vector2(x, y);
            }
            return Vector2.zero;
        }

        Vector2 GetViewPortSnapPivotLocalPos(Vector2 pos)
        {
            float pivotLocalPosX = 0;
            float pivotLocalPosY = 0;
            if (mArrangeType == GridItemArrangeType.TopLeftToBottomRight)
            {
                pivotLocalPosX = -pos.x + ViewPortWidth * mViewPortSnapPivot.x;
                pivotLocalPosY = -pos.y - ViewPortHeight * (1 - mViewPortSnapPivot.y);
            }
            else if (mArrangeType == GridItemArrangeType.BottomLeftToTopRight)
            {
                pivotLocalPosX = -pos.x + ViewPortWidth * mViewPortSnapPivot.x;
                pivotLocalPosY = -pos.y + ViewPortHeight * mViewPortSnapPivot.y;
            }
            else if (mArrangeType == GridItemArrangeType.TopRightToBottomLeft)
            {
                pivotLocalPosX = -pos.x - ViewPortWidth * (1 - mViewPortSnapPivot.x);
                pivotLocalPosY = -pos.y - ViewPortHeight * (1 - mViewPortSnapPivot.y);
            }
            else if (mArrangeType == GridItemArrangeType.BottomRightToTopLeft)
            {
                pivotLocalPosX = -pos.x - ViewPortWidth * (1 - mViewPortSnapPivot.x);
                pivotLocalPosY = -pos.y + ViewPortHeight * mViewPortSnapPivot.y;
            }
            return new Vector2(pivotLocalPosX, pivotLocalPosY);
        }

        void UpdateNearestSnapItem(bool forceSendEvent)
        {
            if (mItemSnapEnable == false)
            {
                return;
            }
            int count = mItemGroupList.Count;
            if (count == 0)
            {
                return;
            }
            if(IsContainerTransCanMove() == false)
            {
                return;
            }
            Vector2 pos = GetContainerVaildPos(ContainerTrans.anchoredPosition3D.x, ContainerTrans.anchoredPosition3D.y);
            bool needCheck = (pos.y != mLastSnapCheckPos.y || pos.x != mLastSnapCheckPos.x);
            mLastSnapCheckPos = pos;
            if (!needCheck)
            {
                if (mLeftSnapUpdateExtraCount > 0)
                {
                    mLeftSnapUpdateExtraCount--;
                    needCheck = true;
                }
            }
            if (needCheck)
            {
                RowColumnPair curVal = new RowColumnPair(-1,-1);
                Vector2 snapTartetPos = GetViewPortSnapPivotLocalPos(pos);
                curVal = FindNearestItemWithLocalPos(snapTartetPos.x, snapTartetPos.y);
                if (curVal.mRow >= 0)
                {
                    RowColumnPair oldNearestItem = mCurSnapNearestItemRowColumn;
                    mCurSnapNearestItemRowColumn = curVal;
                    if (forceSendEvent || oldNearestItem != mCurSnapNearestItemRowColumn)
                    {
                        if (mOnSnapNearestChanged != null)
                        {
                            mOnSnapNearestChanged(this);
                        }
                    }
                }
                else
                {
                    mCurSnapNearestItemRowColumn.mRow = -1;
                    mCurSnapNearestItemRowColumn.mColumn = -1;
                }
            }
        }

        void UpdateFromSettingParam(LoopGridViewSettingParam param)
        {
            if (param == null)
            {
                return;
            }
            if (param.mItemSize != null)
            {
                mItemSize = (Vector2)(param.mItemSize);
            }
            if (param.mItemPadding != null)
            {
                mItemPadding = (Vector2)(param.mItemPadding);
            }
            if (param.mPadding != null)
            {
                mPadding = (RectOffset)(param.mPadding);
            }
            if (param.mGridFixedType != null)
            {
                mGridFixedType = (GridFixedType)(param.mGridFixedType);
            }
            if (param.mFixedRowOrColumnCount != null)
            {
                mFixedRowOrColumnCount = (int)(param.mFixedRowOrColumnCount);
            }
        }

        //snap move will finish at once.
        public void FinishSnapImmediately()
        {
            UpdateSnapMove(true);
        }

        //update snap move. if immediate is set true, then the snap move will finish at once.
        void UpdateSnapMove(bool immediate = false, bool forceSendEvent = false)
        {
            if (mItemSnapEnable == false)
            {
                return;
            }
            UpdateNearestSnapItem(false);
            Vector2 pos = mContainerTrans.anchoredPosition3D;
            if (CanSnap() == false)
            {
                ClearSnapData();
                return;
            }
            UpdateCurSnapData();
            if (mCurSnapData.mSnapStatus != SnapStatus.SnapMoving)
            {
                return;
            }
            float v = Mathf.Abs(mScrollRect.velocity.x) + Mathf.Abs(mScrollRect.velocity.y);
            if (v > 0)
            {
                mScrollRect.StopMovement();
            }
            float old = mCurSnapData.mCurSnapVal;
            mCurSnapData.mCurSnapVal = Mathf.SmoothDamp(mCurSnapData.mCurSnapVal, mCurSnapData.mTargetSnapVal, ref mSmoothDumpVel, mSmoothDumpRate);
            float dt = mCurSnapData.mCurSnapVal - old;

            if (immediate || Mathf.Abs(mCurSnapData.mTargetSnapVal - mCurSnapData.mCurSnapVal) < mSnapFinishThreshold)
            {
                pos = pos + (mCurSnapData.mTargetSnapVal - old)* mCurSnapData.mSnapNeedMoveDir;
                mCurSnapData.mSnapStatus = SnapStatus.SnapMoveFinish;
                if (mOnSnapItemFinished != null)
                {
                    LoopGridViewItem targetItem = GetShownItemByRowColumn(mCurSnapNearestItemRowColumn.mRow, mCurSnapNearestItemRowColumn.mColumn);
                    if (targetItem != null)
                    {
                        mOnSnapItemFinished(this, targetItem);
                    }
                }
            }
            else
            {
                pos = pos + dt * mCurSnapData.mSnapNeedMoveDir;
            }
            mContainerTrans.anchoredPosition3D = GetContainerVaildPos(pos.x, pos.y);
        }

        GridItemGroup GetShownGroup(int groupIndex)
        {
            if(groupIndex < 0)
            {
                return null;
            }
            int count = mItemGroupList.Count;
            if (count == 0)
            {
                return null;
            }
            if (groupIndex < mItemGroupList[0].GroupIndex || groupIndex > mItemGroupList[count - 1].GroupIndex)
            {
                return null;
            }
            int i = groupIndex - mItemGroupList[0].GroupIndex;
            return mItemGroupList[i];
        }

 
        void FillCurSnapData(int row,int column)
        {
            Vector2 itemSnapPivotLocalPos = GetItemSnapPivotLocalPos(row, column);
            Vector2 containerPos = GetContainerVaildPos(ContainerTrans.anchoredPosition3D.x, ContainerTrans.anchoredPosition3D.y);
            Vector2 snapTartetPos = GetViewPortSnapPivotLocalPos(containerPos);
            Vector2 dir = snapTartetPos - itemSnapPivotLocalPos;
            if (mScrollRect.horizontal == false)
            {
                dir.x = 0;
            }
            if(mScrollRect.vertical == false)
            {
                dir.y = 0;
            }
            mCurSnapData.mTargetSnapVal = dir.magnitude;
            mCurSnapData.mCurSnapVal = 0;
            mCurSnapData.mSnapNeedMoveDir = dir.normalized;
        }


        void UpdateCurSnapData()
        {
            int count = mItemGroupList.Count;
            if (count == 0)
            {
                mCurSnapData.Clear();
                return;
            }

            if (mCurSnapData.mSnapStatus == SnapStatus.SnapMoveFinish)
            {
                if (mCurSnapData.mSnapTarget == mCurSnapNearestItemRowColumn)
                {
                    return;
                }
                mCurSnapData.mSnapStatus = SnapStatus.NoTargetSet;
            }
            if (mCurSnapData.mSnapStatus == SnapStatus.SnapMoving)
            {
                if ((mCurSnapData.mSnapTarget == mCurSnapNearestItemRowColumn) || mCurSnapData.mIsForceSnapTo)
                {
                    return;
                }
                mCurSnapData.mSnapStatus = SnapStatus.NoTargetSet;
            }
            if (mCurSnapData.mSnapStatus == SnapStatus.NoTargetSet)
            {
                LoopGridViewItem nearestItem = GetShownItemByRowColumn(mCurSnapNearestItemRowColumn.mRow, mCurSnapNearestItemRowColumn.mColumn);
                if (nearestItem == null)
                {
                    return;
                }
                mCurSnapData.mSnapTarget = mCurSnapNearestItemRowColumn;
                mCurSnapData.mSnapStatus = SnapStatus.TargetHasSet;
                mCurSnapData.mIsForceSnapTo = false;
            }
            if (mCurSnapData.mSnapStatus == SnapStatus.TargetHasSet)
            {
                LoopGridViewItem targetItem = GetShownItemByRowColumn(mCurSnapData.mSnapTarget.mRow, mCurSnapData.mSnapTarget.mColumn);
                if (targetItem == null)
                {
                    mCurSnapData.Clear();
                    return;
                }
                FillCurSnapData(targetItem.Row,targetItem.Column);
                mCurSnapData.mSnapStatus = SnapStatus.SnapMoving;
            }

        }
       

        bool CanSnap()
        {
            if (mIsDraging)
            {
                return false;
            }
            if (mScrollBarClickEventListener1 != null)
            {
                if (mScrollBarClickEventListener1.IsPressd)
                {
                    return false;
                }
            }
            if (mScrollBarClickEventListener2 != null)
            {
                if (mScrollBarClickEventListener2.IsPressd)
                {
                    return false;
                }
            }
            if(IsContainerTransCanMove() == false)
            {
                return false;
            }
            if (Mathf.Abs(mScrollRect.velocity.x) > mSnapVecThreshold)
            {
                return false;
            }
            if (Mathf.Abs(mScrollRect.velocity.y) > mSnapVecThreshold)
            {
                return false;
            }
            Vector3 pos = mContainerTrans.anchoredPosition3D;
            Vector2 vPos = GetContainerVaildPos(pos.x, pos.y);
            if(Mathf.Abs(pos.x - vPos.x) >3)
            {
                return false;
            }
            if (Mathf.Abs(pos.y - vPos.y) > 3)
            {
                return false;
            }
            return true;
        }

        GridItemGroup GetOneItemGroupObj()
        {
            int count = mItemGroupObjPool.Count;
            if (count == 0)
            {
                return new GridItemGroup();
            }
            GridItemGroup ret = mItemGroupObjPool[count - 1];
            mItemGroupObjPool.RemoveAt(count - 1);
            return ret;
        }
        void RecycleOneItemGroupObj(GridItemGroup obj)
        {
            mItemGroupObjPool.Add(obj);
        }


    }

}
