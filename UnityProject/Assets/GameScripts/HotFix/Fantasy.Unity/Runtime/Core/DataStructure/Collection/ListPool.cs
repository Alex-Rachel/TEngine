using System;
using System.Collections.Generic;
using Fantasy.Pool;

// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract

namespace Fantasy.DataStructure.Collection
{
    /// <summary>
    /// 可释放的列表（List）对象池。
    /// </summary>
    /// <typeparam name="T">列表中元素的类型。</typeparam>
    public sealed class ListPool<T> : List<T>, IDisposable, IPool
    {
        private bool _isPool;
        private bool _isDispose;

        /// <summary>
        /// 释放实例所占用的资源，并将实例返回到对象池中，以便重用。
        /// </summary>
        public void Dispose()
        {
            if (_isDispose)
            {
                return;
            }

            _isDispose = true;
            Clear();
            Pool<ListPool<T>>.Return(this);
        }

        /// <summary>
        /// 使用指定的元素创建一个 <see cref="ListPool{T}"/> 列表（List）对象池的实例。
        /// </summary>
        /// <param name="args">要添加到列表的元素。</param>
        /// <returns>创建的实例。</returns>
        public static ListPool<T> Create(params T[] args)
        {
            var list = Pool<ListPool<T>>.Rent();
            list._isDispose = false;
            list._isPool = true;

            if (args != null)
            {
                list.AddRange(args);
            }

            return list;
        }

        /// <summary>
        /// 使用指定的列表创建一个 <see cref="ListPool{T}"/> 列表（List）对象池的实例。
        /// </summary>
        /// <param name="args">要添加到列表的元素列表。</param>
        /// <returns>创建的实例。</returns>
        public static ListPool<T> Create(List<T> args)
        {
            var list = Pool<ListPool<T>>.Rent();
            list._isDispose = false;
            list._isPool = true;

            if (args != null)
            {
                list.AddRange(args);
            }

            return list;
        }

        /// <summary>
        /// 获取一个值，该值指示当前实例是否为对象池中的实例。
        /// </summary>
        /// <returns></returns>
        public bool IsPool()
        {
            return _isPool;
        }

        /// <summary>
        /// 设置一个值，该值指示当前实例是否为对象池中的实例。
        /// </summary>
        /// <param name="isPool"></param>
        public void SetIsPool(bool isPool)
        {
            _isPool = isPool;
        }
    }
}