
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning disable CS8604 // Possible null reference argument.
#pragma warning disable CS8602 // Dereference of a possibly null reference.
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS8601 // Possible null reference assignment.
namespace Fantasy.DataStructure.SkipTable
{
    /// <summary>
    /// 跳表降序版，用于存储降序排列的数据。
    /// </summary>
    /// <typeparam name="TValue">存储的值的类型。</typeparam>
    public class SkipTableDesc<TValue> : SkipTableBase<TValue>
    {
        /// <summary>
        /// 初始化跳表降序版的新实例。
        /// </summary>
        /// <param name="maxLayer">跳表的最大层数，默认为 8。</param>
        public SkipTableDesc(int maxLayer = 8) : base(maxLayer) { }

        /// <summary>
        /// 向跳表中添加一个节点，根据降序规则进行插入。
        /// </summary>
        /// <param name="sortKey">排序主键。</param>
        /// <param name="viceKey">副键。</param>
        /// <param name="key">键。</param>
        /// <param name="value">值。</param>
        public override void Add(long sortKey, long viceKey, long key, TValue value)
        {
            var rLevel = 1;

            while (rLevel <= MaxLayer && Random.Next(3) == 0)
            {
                ++rLevel;
            }

            SkipTableNode<TValue> cur = TopHeader, last = null;

            for (var layer = MaxLayer; layer >= 1; --layer)
            {
                // 节点有next节点，且 （next主键 > 插入主键） 或 （next主键 == 插入主键 且 next副键 > 插入副键） 
                while (cur.Right != null && ((cur.Right.SortKey > sortKey) ||
                                             (cur.Right.SortKey == sortKey && cur.Right.ViceKey > viceKey)))
                {
                    cur = cur.Right;
                }

                if (layer <= rLevel)
                {
                    var currentRight = cur.Right;
                    cur.Right = new SkipTableNode<TValue>(sortKey, viceKey, key, value,
                        layer == 1 ? cur.Index + 1 : 0, cur, cur.Right, null);

                    if (currentRight != null)
                    {
                        currentRight.Left = cur.Right;
                    }

                    if (last != null)
                    {
                        last.Down = cur.Right;
                    }

                    if (layer == 1)
                    {
                        cur.Right.Index = cur.Index + 1;
                        Node.Add(key, cur.Right);

                        SkipTableNode<TValue> v = cur.Right.Right;

                        while (v != null)
                        {
                            v.Index++;
                            v = v.Right;
                        }
                    }

                    last = cur.Right;
                }

                cur = cur.Down;
            }
        }

        /// <summary>
        /// 从跳表中移除一个节点，根据降序规则进行移除。
        /// </summary>
        /// <param name="sortKey">排序主键。</param>
        /// <param name="viceKey">副键。</param>
        /// <param name="key">键。</param>
        /// <param name="value">移除的节点值。</param>
        /// <returns>如果成功移除节点，则返回 true，否则返回 false。</returns>
        public override bool Remove(long sortKey, long viceKey, long key, out TValue value)
        {
            value = default;
            var seen = false;
            var cur = TopHeader;

            for (var layer = MaxLayer; layer >= 1; --layer)
            {
                // 先按照主键查找 再 按副键查找
                while (cur.Right != null && cur.Right.SortKey > sortKey && cur.Right.Key != key) cur = cur.Right;
                while (cur.Right != null && (cur.Right.SortKey == sortKey && cur.Right.ViceKey >= viceKey) &&
                       cur.Right.Key != key) cur = cur.Right;

                var isFind = false;
                var currentCur = cur;
                SkipTableNode<TValue> removeCur = null;
                // 如果当前不是要删除的节点、但主键和副键都一样、需要特殊处理下。
                if (cur.Right != null && cur.Right.Key == key)
                {
                    isFind = true;
                    removeCur = cur.Right;
                    currentCur = cur;
                }
                else
                {
                    // 先向左查找下
                    var currentNode = cur.Left;
                    while (currentNode != null && currentNode.SortKey == sortKey && currentNode.ViceKey == viceKey)
                    {
                        if (currentNode.Key == key)
                        {
                            isFind = true;
                            removeCur = currentNode;
                            currentCur = currentNode.Left;
                            break;
                        }

                        currentNode = currentNode.Left;
                    }

                    // 再向右查找下
                    if (!isFind)
                    {
                        currentNode = cur.Right;
                        while (currentNode != null && currentNode.SortKey == sortKey && currentNode.ViceKey == viceKey)
                        {
                            if (currentNode.Key == key)
                            {
                                isFind = true;
                                removeCur = currentNode;
                                currentCur = currentNode.Left;
                                break;
                            }

                            currentNode = currentNode.Right;
                        }
                    }
                }

                if (isFind && currentCur != null)
                {
                    value = removeCur.Value;
                    currentCur.Right = removeCur.Right;

                    if (removeCur.Right != null)
                    {
                        removeCur.Right.Left = currentCur;
                        removeCur.Right = null;
                    }

                    removeCur.Left = null;
                    removeCur.Down = null;
                    removeCur.Value = default;

                    if (layer == 1)
                    {
                        var tempCur = currentCur.Right;
                        while (tempCur != null)
                        {
                            tempCur.Index--;
                            tempCur = tempCur.Right;
                        }

                        Node.Remove(removeCur.Key);
                    }

                    seen = true;
                }

                cur = cur.Down;
            }

            return seen;
        }
    }
}