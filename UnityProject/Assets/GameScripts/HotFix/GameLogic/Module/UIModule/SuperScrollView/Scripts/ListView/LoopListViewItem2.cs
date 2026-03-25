using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SuperScrollView
{

    public class LoopListViewItem2 : MonoBehaviour
    {
        // indicates the item’s index in the list
        //if itemTotalCount is set -1, then the mItemIndex can be from –MaxInt to +MaxInt.
        //If itemTotalCount is set a value >=0 , then the mItemIndex can only be from 0 to itemTotalCount -1.
        int mItemIndex = -1;

        //indicates the user defined id of an item, and can be set to any user defined integer value, for any purpose, such as to help searching items.
        int mItemId = -1;

        LoopListView2 mParentListView = null;
        bool mIsInitHandlerCalled = false;
        string mItemPrefabName;
        RectTransform mCachedRectTransform;
        float mPadding;
        float mDistanceWithViewPortSnapCenter = 0;
        int mItemCreatedCheckFrameCount = 0;
        float mStartPosOffset = 0;

        object mUserObjectData = null;
        int mUserIntData1 = 0;
        int mUserIntData2 = 0;
        string mUserStringData1 = null;
        string mUserStringData2 = null;

        public int GoId => gameObject.GetHashCode();

        public object UserObjectData
        {
            get { return mUserObjectData; }
            set { mUserObjectData = value; }
        }
        public int UserIntData1
        {
            get { return mUserIntData1; }
            set { mUserIntData1 = value; }
        }
        public int UserIntData2
        {
            get { return mUserIntData2; }
            set { mUserIntData2 = value; }
        }
        public string UserStringData1
        {
            get { return mUserStringData1; }
            set { mUserStringData1 = value; }
        }
        public string UserStringData2
        {
            get { return mUserStringData2; }
            set { mUserStringData2 = value; }
        }

        public float DistanceWithViewPortSnapCenter
        {
            get { return mDistanceWithViewPortSnapCenter; }
            set { mDistanceWithViewPortSnapCenter = value; }
        }

        public float StartPosOffset
        {
            get { return mStartPosOffset; }
            set { mStartPosOffset = value; }
        }

        public int ItemCreatedCheckFrameCount
        {
            get { return mItemCreatedCheckFrameCount; }
            set { mItemCreatedCheckFrameCount = value; }
        }

        public float Padding
        {
            get { return mPadding; }
            set { mPadding = value; }
        }

        public RectTransform CachedRectTransform
        {
            get
            {
                if (mCachedRectTransform == null)
                {
                    mCachedRectTransform = gameObject.GetComponent<RectTransform>();
                }
                return mCachedRectTransform;
            }
        }

        public string ItemPrefabName
        {
            get
            {
                return mItemPrefabName;
            }
            set
            {
                mItemPrefabName = value;
            }
        }

        public int ItemIndex
        {
            get
            {
                return mItemIndex;
            }
            set
            {
                mItemIndex = value;
            }
        }

        //user defined id of the item
        public int ItemId
        {
            get
            {
                return mItemId;
            }
            set
            {
                mItemId = value;
            }
        }


        public bool IsInitHandlerCalled
        {
            get
            {
                return mIsInitHandlerCalled;
            }
            set
            {
                mIsInitHandlerCalled = value;
            }
        }

        public LoopListView2 ParentListView
        {
            get
            {
                return mParentListView;
            }
            set
            {
                mParentListView = value;
            }
        }

        public float TopY
        {
            get
            {
                ListItemArrangeType arrageType = ParentListView.ArrangeType;
                if (arrageType == ListItemArrangeType.TopToBottom)
                {
                    return CachedRectTransform.anchoredPosition3D.y;
                }
                else if(arrageType == ListItemArrangeType.BottomToTop)
                {
                    return CachedRectTransform.anchoredPosition3D.y + CachedRectTransform.rect.height;
                }
                return 0;
            }
        }

        public float BottomY
        {
            get
            {
                ListItemArrangeType arrageType = ParentListView.ArrangeType;
                if (arrageType == ListItemArrangeType.TopToBottom)
                {
                    return CachedRectTransform.anchoredPosition3D.y - CachedRectTransform.rect.height;
                }
                else if (arrageType == ListItemArrangeType.BottomToTop)
                {
                    return CachedRectTransform.anchoredPosition3D.y;
                }
                return 0;
            }
        }


        public float LeftX
        {
            get
            {
                ListItemArrangeType arrageType = ParentListView.ArrangeType;
                if (arrageType == ListItemArrangeType.LeftToRight)
                {
                    return CachedRectTransform.anchoredPosition3D.x;
                }
                else if (arrageType == ListItemArrangeType.RightToLeft)
                {
                    return CachedRectTransform.anchoredPosition3D.x - CachedRectTransform.rect.width;
                }
                return 0;
            }
        }

        public float RightX
        {
            get
            {
                ListItemArrangeType arrageType = ParentListView.ArrangeType;
                if (arrageType == ListItemArrangeType.LeftToRight)
                {
                    return CachedRectTransform.anchoredPosition3D.x + CachedRectTransform.rect.width;
                }
                else if (arrageType == ListItemArrangeType.RightToLeft)
                {
                    return CachedRectTransform.anchoredPosition3D.x;
                }
                return 0;
            }
        }

        public float ItemSize
        {
            get
            {
                if (ParentListView.IsVertList)
                {
                    return  CachedRectTransform.rect.height;
                }
                else
                {
                    return CachedRectTransform.rect.width;
                }
            }
        }

        public float ItemSizeWithPadding
        {
            get
            {
                return ItemSize + mPadding;
            }
        }

    }
}