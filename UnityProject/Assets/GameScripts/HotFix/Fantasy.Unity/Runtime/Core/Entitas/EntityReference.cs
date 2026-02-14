// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS8603 // Possible null reference return.
namespace Fantasy.Entitas
{
    /// <summary>
    /// 实体引用检查组件
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public struct EntityReference<T> where T : Entity
    {
        private T _entity;
        private long _runTimeId;

        private EntityReference(T t)
        {
            if (t == null)
            {
                _entity = null;
                _runTimeId = 0;
                return;
            }
            
            _entity = t;
            _runTimeId = t.RuntimeId;
        }

        /// <summary>
        /// 获取实体引用
        /// </summary>
        public T Value => _entity?.RuntimeId != _runTimeId ? null : _entity;

        /// <summary>
        /// 检查引用是否有效
        /// </summary>
        public bool IsValid => _entity != null && _entity.RuntimeId == _runTimeId;

        /// <summary>
        /// 清空实体引用，用于对象池回收时重置
        /// </summary>
        public void Clear()
        {
            _entity = null;
            _runTimeId = 0;
        }

        /// <summary>
        /// 将一个实体转换为EntityReference
        /// </summary>
        /// <param name="t">实体泛型类型</param>
        /// <returns>返回一个EntityReference</returns>
        public static implicit operator EntityReference<T>(T t)
        {
            return new EntityReference<T>(t);
        }

        /// <summary>
        /// 将一个EntityReference转换为实体
        /// </summary>
        /// <param name="v">实体泛型类型</param>
        /// <returns>当实体已经被销毁过会返回null</returns>
        public static implicit operator T(EntityReference<T> v)
        {
            if (v._entity == null)
            {
                return null;
            }

            if (v._entity.RuntimeId != v._runTimeId)
            {
                v._entity = null;
            }

            return v._entity;
        }
    }
}