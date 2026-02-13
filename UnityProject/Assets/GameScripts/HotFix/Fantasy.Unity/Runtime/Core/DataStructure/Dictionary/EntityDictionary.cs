using System;
using System.Collections.Generic;
using Fantasy.Pool;

namespace Fantasy.DataStructure.Dictionary
{
    /// <summary>
    /// 提供一个带资源释放功能的实体字典类，支持使用对象池管理。
    /// </summary>
    /// <typeparam name="TM">字典中键的类型。</typeparam>
    /// <typeparam name="TN">字典中值的类型，必须实现 IDisposable 接口。</typeparam>
    public sealed class EntityDictionary<TM, TN> : Dictionary<TM, TN>, IDisposable, IPool where TN : IDisposable where TM : notnull
    {
        private bool _isPool;
        private bool _isDispose;

        /// <summary>
        /// 创建一个新的 <see cref="EntityDictionary{TM, TN}"/> 实例。
        /// </summary>
        /// <returns>新创建的实例。</returns>
        public static EntityDictionary<TM, TN> Create()
        {
            var entityDictionary = Pool<EntityDictionary<TM, TN>>.Rent();
            entityDictionary._isDispose = false;
            entityDictionary._isPool = true;
            return entityDictionary;
        }

        /// <summary>
        /// 清空字典中的所有键值对，并释放值的资源。
        /// </summary>
        public new void Clear()
        {
            foreach (var keyValuePair in this)
            {
                keyValuePair.Value.Dispose();
            }

            base.Clear();
        }

        /// <summary>
        /// 清空字典中的所有键值对，但不释放值的资源。
        /// </summary>
        public void ClearNotDispose()
        {
            base.Clear();
        }

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
            Pool<EntityDictionary<TM, TN>>.Return(this);
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