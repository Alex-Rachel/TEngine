using System;
using System.Collections.Generic;
using Fantasy.Pool;

namespace Fantasy.DataStructure.Dictionary
{
    /// <summary>
    /// 提供一个可以使用对象池管理的排序字典类。
    /// </summary>
    /// <typeparam name="TM"></typeparam>
    /// <typeparam name="TN"></typeparam>
    public sealed class SortedDictionaryPool<TM, TN> : SortedDictionary<TM, TN>, IDisposable, IPool where TM : notnull
    {
        private bool _isPool;
        private bool _isDispose;

        /// <summary>
        /// 释放实例占用的资源。
        /// </summary>
        public void Dispose()
        {
            if (_isDispose)
            {
                return;
            }

            _isDispose = true;
            Clear();
            Pool<SortedDictionaryPool<TM, TN>>.Return(this);
        }

        /// <summary>
        /// 创建一个新的 <see cref="SortedDictionaryPool{TM, TN}"/> 实例。
        /// </summary>
        /// <returns>新创建的实例。</returns>
        public static SortedDictionaryPool<TM, TN> Create()
        {
            var dictionary = Pool<SortedDictionaryPool<TM, TN>>.Rent();
            dictionary._isDispose = false;
            dictionary._isPool = true;
            return dictionary;
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