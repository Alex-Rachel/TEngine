using System;
using System.Collections.Generic;

namespace TEngine
{
    /// <summary>
    /// 游戏框架模块实现类管理系统。
    /// </summary>
    public static class ModuleSystem
    {
        /// <summary>
        /// 默认设计的模块数量。
        /// <remarks>有增删可以自行修改减少内存分配与GCAlloc。</remarks>
        /// </summary>
        internal const int DESIGN_MODULE_COUNT = 16;

        private static readonly Dictionary<Type, Module> _moduleMaps = new Dictionary<Type, Module>(DESIGN_MODULE_COUNT);
        private static readonly LinkedList<Module> _modules = new LinkedList<Module>();
        private static readonly LinkedList<Module> _updateModules = new LinkedList<Module>();
        private static readonly List<IUpdateModule> _updateExecuteList = new List<IUpdateModule>(DESIGN_MODULE_COUNT);

        private static bool _isExecuteListDirty;

        /// <summary>
        /// 所有游戏框架模块轮询。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间，以秒为单位。</param>
        /// <param name="realElapseSeconds">真实流逝时间，以秒为单位。</param>
        public static void Update(float elapseSeconds, float realElapseSeconds)
        {
            if (_isExecuteListDirty)
            {
                _isExecuteListDirty = false;
                BuildExecuteList();
            }

            int executeCount = _updateExecuteList.Count;
            for (int i = 0; i < executeCount; i++)
            {
                _updateExecuteList[i].Update(elapseSeconds, realElapseSeconds);
            }
        }

        /// <summary>
        /// 关闭并清理所有游戏框架模块。
        /// </summary>
        public static void Shutdown()
        {
            for (LinkedListNode<Module> current = _modules.Last; current != null; current = current.Previous)
            {
                current.Value.Shutdown();
            }

            _modules.Clear();
            _moduleMaps.Clear();
            _updateModules.Clear();
            _updateExecuteList.Clear();
            MemoryPool.ClearAll();
            Utility.Marshal.FreeCachedHGlobal();
        }

        /// <summary>
        /// 获取游戏框架模块。
        /// </summary>
        /// <typeparam name="T">要获取的游戏框架模块类型。</typeparam>
        /// <returns>要获取的游戏框架模块。</returns>
        /// <remarks>如果要获取的游戏框架模块不存在，则自动创建该游戏框架模块。</remarks>
        public static T GetModule<T>() where T : class
        {
            Type interfaceType = typeof(T);
            if (!interfaceType.IsInterface)
            {
                throw new GameFrameworkException(Utility.Text.Format("You must get module by interface, but '{0}' is not.", interfaceType.FullName));
            }

            if (_moduleMaps.TryGetValue(interfaceType, out Module module))
            {
                return module as T;
            }

            string moduleName = Utility.Text.Format("{0}.{1}", interfaceType.Namespace, interfaceType.Name.Substring(1));
            Type moduleType = Type.GetType(moduleName);
            if (moduleType == null)
            {
                throw new GameFrameworkException(Utility.Text.Format("Can not find Game Framework module type '{0}'.", moduleName));
            }

            return GetModule(moduleType) as T;
        }

        /// <summary>
        /// 获取游戏框架模块。
        /// </summary>
        /// <param name="moduleType">要获取的游戏框架模块类型。</param>
        /// <returns>要获取的游戏框架模块。</returns>
        /// <remarks>如果要获取的游戏框架模块不存在，则自动创建该游戏框架模块。</remarks>
        private static Module GetModule(Type moduleType)
        {
            return _moduleMaps.TryGetValue(moduleType, out Module module) ? module : CreateModule(moduleType);
        }

        /// <summary>
        /// 创建游戏框架模块。
        /// </summary>
        /// <param name="moduleType">要创建的游戏框架模块类型。</param>
        /// <returns>要创建的游戏框架模块。</returns>
        private static Module CreateModule(Type moduleType)
        {
            Module module = (Module)Activator.CreateInstance(moduleType);
            if (module == null)
            {
                throw new GameFrameworkException(Utility.Text.Format("Can not create module '{0}'.", moduleType.FullName));
            }

            _moduleMaps[moduleType] = module;

            RegisterUpdate(module);

            return module;
        }
        
        /// <summary>
        /// 注册自定义Module。
        /// </summary>
        /// <param name="module">Module。</param>
        /// <returns>Module实例。</returns>
        /// <exception cref="GameFrameworkException">框架异常。</exception>
        public static T RegisterModule<T>(Module module) where T : class
        {
            Type interfaceType = typeof(T);
            if (!interfaceType.IsInterface)
            {
                throw new GameFrameworkException(Utility.Text.Format("You must get module by interface, but '{0}' is not.", interfaceType.FullName));
            }

            _moduleMaps[interfaceType] = module;

            RegisterUpdate(module);
            
            return module as T;
        }

        private static void RegisterUpdate(Module module)
        {
            LinkedListNode<Module> current = _modules.First;
            while (current != null)
            {
                if (module.Priority > current.Value.Priority)
                {
                    break;
                }

                current = current.Next;
            }

            if (current != null)
            {
                _modules.AddBefore(current, module);
            }
            else
            {
                _modules.AddLast(module);
            }

            Type interfaceType = typeof(IUpdateModule);
            bool implementsInterface = interfaceType.IsInstanceOfType(module);

            if (implementsInterface)
            {
                LinkedListNode<Module> currentUpdate = _updateModules.First;
                while (currentUpdate != null)
                {
                    if (module.Priority > currentUpdate.Value.Priority)
                    {
                        break;
                    }

                    currentUpdate = currentUpdate.Next;
                }

                if (currentUpdate != null)
                {
                    _updateModules.AddBefore(currentUpdate, module);
                }
                else
                {
                    _updateModules.AddLast(module);
                }

                _isExecuteListDirty = true;
            }

            module.OnInit();
        }

        /// <summary>
        /// 构造执行队列。
        /// </summary>
        private static void BuildExecuteList()
        {
            _updateExecuteList.Clear();
            foreach (var updateModule in _updateModules)
            {
                _updateExecuteList.Add(updateModule as IUpdateModule);
            }
        }
    }
}