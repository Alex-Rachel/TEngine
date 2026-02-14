using System;

namespace TEngine
{
    /// <summary>
    /// 对象基类。
    /// </summary>
    public abstract class ObjectBase : IMemory
    {
        private string _name;
        private object _target;
        private bool _locked;
        private int _priority;
        private DateTime _lastUseTime;

        /// <summary>
        /// 初始化对象基类的新实例。
        /// </summary>
        public ObjectBase()
        {
            _name = null;
            _target = null;
            _locked = false;
            _priority = 0;
            _lastUseTime = default(DateTime);
        }

        /// <summary>
        /// 获取对象名称。
        /// </summary>
        public string Name => _name;

        /// <summary>
        /// 获取对象。
        /// </summary>
        public object Target => _target;

        /// <summary>
        /// 获取或设置对象是否被加锁。
        /// </summary>
        public bool Locked
        {
            get => _locked;
            set => _locked = value;
        }

        /// <summary>
        /// 获取或设置对象的优先级。
        /// </summary>
        public int Priority
        {
            get => _priority;
            set => _priority = value;
        }

        /// <summary>
        /// 获取自定义释放检查标记。
        /// </summary>
        public virtual bool CustomCanReleaseFlag => true;

        /// <summary>
        /// 获取对象上次使用时间。
        /// </summary>
        public DateTime LastUseTime
        {
            get => _lastUseTime;
            internal set => _lastUseTime = value;
        }

        /// <summary>
        /// 初始化对象基类。
        /// </summary>
        /// <param name="target">对象。</param>
        protected void Initialize(object target)
        {
            Initialize(null, target, false, 0);
        }

        /// <summary>
        /// 初始化对象基类。
        /// </summary>
        /// <param name="name">对象名称。</param>
        /// <param name="target">对象。</param>
        protected void Initialize(string name, object target)
        {
            Initialize(name, target, false, 0);
        }

        /// <summary>
        /// 初始化对象基类。
        /// </summary>
        /// <param name="name">对象名称。</param>
        /// <param name="target">对象。</param>
        /// <param name="locked">对象是否被加锁。</param>
        protected void Initialize(string name, object target, bool locked)
        {
            Initialize(name, target, locked, 0);
        }

        /// <summary>
        /// 初始化对象基类。
        /// </summary>
        /// <param name="name">对象名称。</param>
        /// <param name="target">对象。</param>
        /// <param name="priority">对象的优先级。</param>
        protected void Initialize(string name, object target, int priority)
        {
            Initialize(name, target, false, priority);
        }

        /// <summary>
        /// 初始化对象基类。
        /// </summary>
        /// <param name="name">对象名称。</param>
        /// <param name="target">对象。</param>
        /// <param name="locked">对象是否被加锁。</param>
        /// <param name="priority">对象的优先级。</param>
        protected void Initialize(string name, object target, bool locked, int priority)
        {
            if (target == null)
            {
                throw new GameFrameworkException(Utility.Text.Format("Target '{0}' is invalid.", name));
            }

            _name = name ?? string.Empty;
            _target = target;
            _locked = locked;
            _priority = priority;
            _lastUseTime = DateTime.UtcNow;
        }

        /// <summary>
        /// 清理对象基类。
        /// </summary>
        public virtual void Clear()
        {
            _name = null;
            _target = null;
            _locked = false;
            _priority = 0;
            _lastUseTime = default(DateTime);
        }

        /// <summary>
        /// 获取对象时的事件。
        /// </summary>
        protected internal virtual void OnSpawn()
        {
        }

        /// <summary>
        /// 回收对象时的事件。
        /// </summary>
        protected internal virtual void OnUnspawn()
        {
        }

        /// <summary>
        /// 释放对象。
        /// </summary>
        /// <param name="isShutdown">是否是关闭对象池时触发。</param>
        protected internal abstract void Release(bool isShutdown);
    }
}