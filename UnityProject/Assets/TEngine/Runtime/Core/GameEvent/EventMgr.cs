using System;
using System.Collections.Generic;

namespace TEngine
{
    /// <summary>
    /// 事件管理器。
    /// </summary>
    public class EventMgr
    {
        /// <summary>
        /// 事件实体数据。
        /// </summary>
        private class EventEntryData
        {
            public object InterfaceWrap;
        };

        // private readonly Dictionary<string, EventEntryData> _eventEntryMap = new Dictionary<string, EventEntryData>();
        /// <summary>
        /// 总事件实体数据。
        /// </summary>
        private readonly Dictionary<System.Type, EventEntryData> _eventEntryMap = new Dictionary<System.Type, EventEntryData>();

        /// <summary>
        /// 事件管理器获取接口。
        /// </summary>
        /// <typeparam name="T">接口类型。</typeparam>
        /// <returns>接口实例。</returns>
        public T GetInterface<T>()
        {
            // string typeName = typeof(T).FullName;
            // if (typeName != null && _eventEntryMap.TryGetValue(typeName, out var entry))
            if (_eventEntryMap.TryGetValue(typeof(T), out var entry))
            {
                return (T)entry.InterfaceWrap;
            }

            return default(T);
        }

        /// <summary>
        /// 注册wrap的函数。
        /// </summary>
        /// <typeparam name="T">Wrap接口类型。</typeparam>
        /// <param name="callerWrap">callerWrap接口名字。</param>
        public void RegWrapInterface<T>(T callerWrap)
        {
            if (callerWrap != null)
            {
                var entry = new EventEntryData
                {
                    InterfaceWrap = callerWrap
                };
                _eventEntryMap.Add(typeof(T), entry);
            }
        }

        /// <summary>
        /// 注册wrap的函数。
        /// </summary>
        /// <param name="typeName">类型名称。</param>
        /// <param name="callerWrap">调用接口名。</param>
        [Obsolete("过时的类型名参数方法，使用泛型，减少GC，完美适配混淆")]
        public void RegWrapInterface(string typeName, object callerWrap)
        {
            throw new NotSupportedException("请使用泛型方法 RegWrapInterface<T>(T callerWrap)");
        }

        /// <summary>
        /// 分发注册器。
        /// </summary>
        public EventDispatcher Dispatcher { get; private set; } = new EventDispatcher();
        
        public EventDispatcher GetDispatcher()
        {
            return Dispatcher;
        }
        
        /// <summary>
        /// 清除事件。
        /// </summary>
        public void Init()
        {
            _eventEntryMap.Clear();
            Dispatcher.ClearEventTable();
        }
    }
}