using System;
using System.Collections.Generic;

namespace SuperScrollView
{

    public enum SnapStatus
    {
        NoTargetSet = 0,
        TargetHasSet = 1,
        SnapMoving = 2,
        SnapMoveFinish = 3
    }


    public enum ItemCornerEnum
    {
        LeftBottom = 0,
        LeftTop,
        RightTop,
        RightBottom,
    }


    public enum ListItemArrangeType
    {
        TopToBottom = 0,
        BottomToTop,
        LeftToRight,
        RightToLeft,
    }

    public enum GridItemArrangeType
    {
        TopLeftToBottomRight = 0,
        BottomLeftToTopRight,
        TopRightToBottomLeft,
        BottomRightToTopLeft,
    }
    public enum GridFixedType
    {
        ColumnCountFixed = 0,
        RowCountFixed,
    }

    public struct RowColumnPair
    {
        public RowColumnPair(int row1, int column1)
        {
            mRow = row1;
            mColumn = column1;
        }

        public bool Equals(RowColumnPair other)
        {
            return this.mRow == other.mRow && this.mColumn == other.mColumn;
        }

        public static bool operator ==(RowColumnPair a, RowColumnPair b)
        {
            return (a.mRow == b.mRow)&&(a.mColumn == b.mColumn);
        }
        public static bool operator !=(RowColumnPair a, RowColumnPair b)
        {
            return (a.mRow != b.mRow) || (a.mColumn != b.mColumn); ;
        }

        public override int GetHashCode()
        {
            return 0;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }
            return (obj is RowColumnPair) && Equals((RowColumnPair)obj);
        }


        public int mRow;
        public int mColumn;
    }
}
