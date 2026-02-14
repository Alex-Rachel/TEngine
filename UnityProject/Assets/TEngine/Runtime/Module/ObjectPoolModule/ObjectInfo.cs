using System;
using System.Runtime.InteropServices;

namespace TEngine
{
    /// <summary>
    /// 对象信息。
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    public struct ObjectInfo
    {
        private readonly string _name;
        private readonly bool _locked;
        private readonly bool _customCanReleaseFlag;
        private readonly int _priority;
        private readonly DateTime _lastUseTime;
        private readonly int _spawnCount;

        /// <summary>
        /// 初始化对象信息的新实例。
        /// </summary>
        /// <param name="name">对象名称。</param>
        /// <param name="locked">对象是否被加锁。</param>
        /// <param name="customCanReleaseFlag">对象自定义释放检查标记。</param>
        /// <param name="priority">对象的优先级。</param>
        /// <param name="lastUseTime">对象上次使用时间。</param>
        /// <param name="spawnCount">对象的获取计数。</param>
        public ObjectInfo(string name, bool locked, bool customCanReleaseFlag, int priority, DateTime lastUseTime, int spawnCount)
        {
            _name = name;
            _locked = locked;
            _customCanReleaseFlag = customCanReleaseFlag;
            _priority = priority;
            _lastUseTime = lastUseTime;
            _spawnCount = spawnCount;
        }

        /// <summary>
        /// 获取对象名称。
        /// </summary>
        public string Name => _name;

        /// <summary>
        /// 获取对象是否被加锁。
        /// </summary>
        public bool Locked => _locked;

        /// <summary>
        /// 获取对象自定义释放检查标记。
        /// </summary>
        public bool CustomCanReleaseFlag => _customCanReleaseFlag;

        /// <summary>
        /// 获取对象的优先级。
        /// </summary>
        public int Priority => _priority;

        /// <summary>
        /// 获取对象上次使用时间。
        /// </summary>
        public DateTime LastUseTime => _lastUseTime;

        /// <summary>
        /// 获取对象是否正在使用。
        /// </summary>
        public bool IsInUse => _spawnCount > 0;

        /// <summary>
        /// 获取对象的获取计数。
        /// </summary>
        public int SpawnCount => _spawnCount;
    }
}