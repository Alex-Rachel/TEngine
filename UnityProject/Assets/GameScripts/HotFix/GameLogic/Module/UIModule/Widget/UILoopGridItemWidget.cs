using SuperScrollView;

namespace GameLogic
{
    public class UILoopGridItemWidget : SelectItemBase
    {
        /// <summary>
        /// Loop GridView Item组件
        /// </summary>
        public LoopGridViewItem LoopItem { set; get; }

        /// <summary>
        /// 索引
        /// </summary>
        public int Index { private set; get; }

        /// <summary>
        /// 更新Item数据
        /// </summary>
        /// <param name="index">索引</param>
        public virtual void UpdateItem(int index)
        {
            Index = index;
        }
    }
}