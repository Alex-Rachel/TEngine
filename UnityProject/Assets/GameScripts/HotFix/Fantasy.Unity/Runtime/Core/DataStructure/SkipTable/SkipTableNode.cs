namespace Fantasy.DataStructure.SkipTable
{
    /// <summary>
    /// 跳跃表节点。
    /// </summary>
    /// <typeparam name="TValue">节点的值的类型。</typeparam>
    public class SkipTableNode<TValue>
    {
        /// <summary>
        /// 节点在跳跃表中的索引。
        /// </summary>
        public int Index;
        /// <summary>
        /// 节点的主键。
        /// </summary>
        public long Key;
        /// <summary>
        /// 节点的排序键。
        /// </summary>
        public long SortKey;
        /// <summary>
        /// 节点的副键。
        /// </summary>
        public long ViceKey;
        /// <summary>
        /// 节点存储的值。
        /// </summary>
        public TValue Value;
        /// <summary>
        /// 指向左侧节点的引用。
        /// </summary>
        public SkipTableNode<TValue> Left;
        /// <summary>
        /// 指向右侧节点的引用。
        /// </summary>
        public SkipTableNode<TValue> Right;
        /// <summary>
        /// 指向下一层节点的引用。
        /// </summary>
        public SkipTableNode<TValue> Down;

        /// <summary>
        /// 初始化跳跃表节点的新实例。
        /// </summary>
        /// <param name="sortKey">节点的排序键。</param>
        /// <param name="viceKey">节点的副键。</param>
        /// <param name="key">节点的主键。</param>
        /// <param name="value">节点存储的值。</param>
        /// <param name="index">节点在跳跃表中的索引。</param>
        /// <param name="l">指向左侧节点的引用。</param>
        /// <param name="r">指向右侧节点的引用。</param>
        /// <param name="d">指向下一层节点的引用。</param>
        public SkipTableNode(long sortKey, long viceKey, long key, TValue value, int index,
            SkipTableNode<TValue> l,
            SkipTableNode<TValue> r,
            SkipTableNode<TValue> d)
        {
            Left = l;
            Right = r;
            Down = d;
            Value = value;
            Key = key;
            Index = index;
            SortKey = sortKey;
            ViceKey = viceKey;
        }
    }
}