using System;
using System.Collections;
using System.Collections.Generic;
using Fantasy.DataStructure.Collection;

#pragma warning disable CS8601
#pragma warning disable CS8603
#pragma warning disable CS8625
#pragma warning disable CS8604

namespace Fantasy.DataStructure.SkipTable
{
    /// <summary>
    /// 抽象的跳表基类，提供跳表的基本功能和操作。
    /// </summary>
    /// <typeparam name="TValue">跳表中存储的值的类型。</typeparam>
    public abstract class SkipTableBase<TValue> : IEnumerable<SkipTableNode<TValue>>
    {
        /// <summary>
        /// 跳表的最大层数
        /// </summary>
        public readonly int MaxLayer;
        /// <summary>
        /// 跳表的顶部头节点
        /// </summary>
        public readonly SkipTableNode<TValue> TopHeader;
        /// <summary>
        /// 跳表的底部头节点
        /// </summary>
        public SkipTableNode<TValue> BottomHeader;
        /// <summary>
        /// 跳表中节点的数量，使用了 Node 字典的计数
        /// </summary>
        public int Count => Node.Count;
        /// <summary>
        /// 用于生成随机数的随机数生成器
        /// </summary>
        protected readonly Random Random = new Random();
        /// <summary>
        /// 存储跳表节点的字典
        /// </summary>
        protected readonly Dictionary<long, SkipTableNode<TValue>> Node = new();
        /// <summary>
        /// 用于辅助反向查找的栈
        /// </summary>
        protected readonly Stack<SkipTableNode<TValue>> AntiFindStack = new Stack<SkipTableNode<TValue>>();

        /// <summary>
        /// 初始化一个新的跳表实例。
        /// </summary>
        /// <param name="maxLayer">跳表的最大层数，默认为 8。</param>
        protected SkipTableBase(int maxLayer = 8)
        {
            MaxLayer = maxLayer;
            var cur = TopHeader = new SkipTableNode<TValue>(long.MinValue, 0, 0, default, 0, null, null, null);

            for (var layer = MaxLayer - 1; layer >= 1; --layer)
            {
                cur.Down = new SkipTableNode<TValue>(long.MinValue, 0, 0, default, 0, null, null, null);
                cur = cur.Down;
            }

            BottomHeader = cur;
        }

        /// <summary>
        /// 获取指定键的节点的值，若不存在则返回默认值。
        /// </summary>
        /// <param name="key">要查找的键。</param>
        public TValue this[long key] => !TryGetValueByKey(key, out TValue value) ? default : value;

        /// <summary>
        /// 获取指定键的节点在跳表中的排名。
        /// </summary>
        /// <param name="key">要查找的键。</param>
        /// <returns>节点的排名。</returns>
        public int GetRanking(long key)
        {
            if (!Node.TryGetValue(key, out var node))
            {
                return 0;
            }

            return node.Index;
        }

        /// <summary>
        /// 获取指定键的反向排名，即在比该键更大的节点中的排名。
        /// </summary>
        /// <param name="key">要查找的键。</param>
        /// <returns>反向排名。</returns>
        public int GetAntiRanking(long key)
        {
            var ranking = GetRanking(key);

            if (ranking == 0)
            {
                return 0;
            }

            return Count + 1 - ranking;
        }

        /// <summary>
        /// 尝试通过键获取节点的值。
        /// </summary>
        /// <param name="key">要查找的键。</param>
        /// <param name="value">获取到的节点的值，如果键不存在则为默认值。</param>
        /// <returns>是否成功获取节点的值。</returns>
        public bool TryGetValueByKey(long key, out TValue value)
        {
            if (!Node.TryGetValue(key, out var node))
            {
                value = default;
                return false;
            }

            value = node.Value;
            return true;
        }

        /// <summary>
        /// 尝试通过键获取节点。
        /// </summary>
        /// <param name="key">要查找的键。</param>
        /// <param name="node">获取到的节点，如果键不存在则为 <c>null</c>。</param>
        /// <returns>是否成功获取节点。</returns>
        public bool TryGetNodeByKey(long key, out SkipTableNode<TValue> node)
        {
            if (Node.TryGetValue(key, out node))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 在跳表中查找节点，返回从起始位置到结束位置的节点列表。
        /// </summary>
        /// <param name="start">起始位置的排名。</param>
        /// <param name="end">结束位置的排名。</param>
        /// <param name="list">用于存储节点列表的 <see cref="ListPool{T}"/> 实例。</param>
        public void Find(int start, int end, ListPool<SkipTableNode<TValue>> list)
        {
            var cur = BottomHeader;
            var count = end - start;

            for (var i = 0; i < start; i++)
            {
                cur = cur.Right;
            }

            for (var i = 0; i <= count; i++)
            {
                if (cur == null)
                {
                    break;
                }

                list.Add(cur);
                cur = cur.Right;
            }
        }

        /// <summary>
        /// 在跳表中进行反向查找节点，返回从结束位置到起始位置的节点列表。
        /// </summary>
        /// <param name="start">结束位置的排名。</param>
        /// <param name="end">起始位置的排名。</param>
        /// <param name="list">用于存储节点列表的 <see cref="ListPool{T}"/> 实例。</param>
        public void AntiFind(int start, int end, ListPool<SkipTableNode<TValue>> list)
        {
            var cur = BottomHeader;
            start = Count + 1 - start;
            end = start - end;

            for (var i = 0; i < start; i++)
            {
                cur = cur.Right;

                if (cur == null)
                {
                    break;
                }

                if (i < end)
                {
                    continue;
                }

                AntiFindStack.Push(cur);
            }

            while (AntiFindStack.TryPop(out var node))
            {
                list.Add(node);
            }
        }

        /// <summary>
        /// 获取跳表中最后一个节点的值。
        /// </summary>
        /// <returns>最后一个节点的值。</returns>
        public TValue GetLastValue()
        {
            var cur = TopHeader;

            while (cur.Right != null || cur.Down != null)
            {
                while (cur.Right != null)
                {
                    cur = cur.Right;
                }

                if (cur.Down != null)
                {
                    cur = cur.Down;
                }
            }

            return cur.Value;
        }

        /// <summary>
        /// 移除跳表中指定键的节点。
        /// </summary>
        /// <param name="key">要移除的节点的键。</param>
        /// <returns>移除是否成功。</returns>
        public bool Remove(long key)
        {
            if (!Node.TryGetValue(key, out var node))
            {
                return false;
            }

            return Remove(node.SortKey, node.ViceKey, key, out _);
        }

        /// <summary>
        /// 向跳表中添加节点。
        /// </summary>
        /// <param name="sortKey">节点的排序键。</param>
        /// <param name="viceKey">节点的副键。</param>
        /// <param name="key">节点的键。</param>
        /// <param name="value">节点的值。</param>
        public abstract void Add(long sortKey, long viceKey, long key, TValue value);

        /// <summary>
        /// 从跳表中移除指定键的节点。
        /// </summary>
        /// <param name="sortKey">节点的排序键。</param>
        /// <param name="viceKey">节点的副键。</param>
        /// <param name="key">节点的键。</param>
        /// <param name="value">被移除的节点的值。</param>
        /// <returns>移除是否成功。</returns>
        public abstract bool Remove(long sortKey, long viceKey, long key, out TValue value);

        /// <summary>
        /// 返回一个枚举器，用于遍历跳表中的节点。
        /// </summary>
        /// <returns>一个可用于遍历跳表节点的枚举器。</returns>
        public IEnumerator<SkipTableNode<TValue>> GetEnumerator()
        {
            var cur = BottomHeader.Right;
            while (cur != null)
            {
                yield return cur;
                cur = cur.Right;
            }
        }

        /// <summary>
        /// 返回一个非泛型枚举器，用于遍历跳表中的节点。
        /// </summary>
        /// <returns>一个非泛型枚举器，可用于遍历跳表节点。</returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}