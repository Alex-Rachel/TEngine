using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SuperScrollView
{
    //if GridFixedType is GridFixedType.ColumnCountFixed, then the GridItemGroup is one row of the gridview
    //if GridFixedType is GridFixedType.RowCountFixed, then the GridItemGroup is one column of the gridview
    public class GridItemGroup
    {
        int mCount = 0;
        int mGroupIndex = -1;//the row index or the column index of this group
        LoopGridViewItem mFirst = null;
        LoopGridViewItem mLast = null;
        public int Count
        {
            get { return mCount; }
        }

        public LoopGridViewItem First
        {
            get { return mFirst; }
        }

        public LoopGridViewItem Last
        {
            get { return mLast; }
        }

        public int GroupIndex
        {
            get { return mGroupIndex; }
            set { mGroupIndex = value; }
        }


        public LoopGridViewItem GetItemByColumn(int column)
        {
            LoopGridViewItem cur = mFirst;
            while(cur != null)
            {
                if(cur.Column == column)
                {
                    return cur;
                }
                cur = cur.NextItem;
            }
            return null;
        }
        public LoopGridViewItem GetItemByRow(int row)
        {
            LoopGridViewItem cur = mFirst;
            while (cur != null)
            {
                if (cur.Row == row)
                {
                    return cur;
                }
                cur = cur.NextItem;
            }
            return null;
        }


        public void ReplaceItem(LoopGridViewItem curItem,LoopGridViewItem newItem)
        {
            newItem.PrevItem = curItem.PrevItem;
            newItem.NextItem = curItem.NextItem;
            if(newItem.PrevItem != null)
            {
                newItem.PrevItem.NextItem = newItem;
            }
            if(newItem.NextItem != null)
            {
                newItem.NextItem.PrevItem = newItem;
            }
            if(mFirst == curItem)
            {
                mFirst = newItem;
            }
            if(mLast == curItem)
            {
                mLast = newItem;
            }
        }

        public void AddFirst(LoopGridViewItem newItem)
        {
            newItem.PrevItem = null;
            newItem.NextItem = null;
            if (mFirst == null)
            {
                mFirst = newItem;
                mLast = newItem;
                mFirst.PrevItem = null;
                mFirst.NextItem = null;
                mCount++;
            }
            else
            {
                mFirst.PrevItem = newItem;
                newItem.PrevItem = null;
                newItem.NextItem = mFirst;
                mFirst = newItem;
                mCount++;
            }
        }

        public void AddLast(LoopGridViewItem newItem)
        {
            newItem.PrevItem = null;
            newItem.NextItem = null;
            if (mFirst == null)
            {
                mFirst = newItem;
                mLast = newItem;
                mFirst.PrevItem = null;
                mFirst.NextItem = null;
                mCount++;
            }
            else
            {
                mLast.NextItem = newItem;
                newItem.PrevItem = mLast;
                newItem.NextItem = null;
                mLast = newItem;
                mCount++;
            }
        }

        public LoopGridViewItem RemoveFirst()
        {
            LoopGridViewItem ret = mFirst;
            if (mFirst == null)
            {
                return ret;
            }
            if(mFirst == mLast)
            {
                mFirst = null;
                mLast = null;
                --mCount;
                return ret;
            }
            mFirst = mFirst.NextItem;
            mFirst.PrevItem = null;
            --mCount;
            return ret;
        }
        public LoopGridViewItem RemoveLast()
        {
            LoopGridViewItem ret = mLast;
            if (mFirst == null)
            {
                return ret;
            }
            if (mFirst == mLast)
            {
                mFirst = null;
                mLast = null;
                --mCount;
                return ret;
            }
            mLast = mLast.PrevItem;
            mLast.NextItem = null;
            --mCount;
            return ret;
        }


        public void Clear()
        {
            LoopGridViewItem current = mFirst;
            while (current != null)
            {
                current.PrevItem = null;
                current.NextItem = null;
                current = current.NextItem;
            }
            mFirst = null;
            mLast = null;
            mCount = 0;
        }

    }
}
