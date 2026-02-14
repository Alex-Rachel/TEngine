using System;
using System.Collections.Generic;

namespace TEngine
{
    /// <summary>
    /// 有限状态机管理器。
    /// </summary>
    internal sealed class FsmModule : Module, IFsmModule, IUpdateModule
    {
        private readonly Dictionary<TypeNamePair, FsmBase> _fsmMap;
        private readonly List<FsmBase> _tempFsmList;

        /// <summary>
        /// 初始化有限状态机管理器的新实例。
        /// </summary>
        public FsmModule()
        {
            _fsmMap = new Dictionary<TypeNamePair, FsmBase>();
            _tempFsmList = new List<FsmBase>();
        }

        /// <summary>
        /// 获取游戏框架模块优先级。
        /// </summary>
        /// <remarks>优先级较高的模块会优先轮询，并且关闭操作会后进行。</remarks>
        public override int Priority => 1;

        /// <summary>
        /// 获取有限状态机数量。
        /// </summary>
        public int Count => _fsmMap.Count;

        /// <summary>
        /// 有限状态机管理器轮询。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间，以秒为单位。</param>
        /// <param name="realElapseSeconds">真实流逝时间，以秒为单位。</param>
        public void Update(float elapseSeconds, float realElapseSeconds)
        {
            _tempFsmList.Clear();
            if (_fsmMap.Count <= 0)
            {
                return;
            }

            foreach (KeyValuePair<TypeNamePair, FsmBase> fsm in _fsmMap)
            {
                _tempFsmList.Add(fsm.Value);
            }

            foreach (FsmBase fsm in _tempFsmList)
            {
                if (fsm.IsDestroyed)
                {
                    continue;
                }

                fsm.Update(elapseSeconds, realElapseSeconds);
            }
        }

        public override void OnInit()
        {
        }

        /// <summary>
        /// 关闭并清理有限状态机管理器。
        /// </summary>
        public override void Shutdown()
        {
            foreach (KeyValuePair<TypeNamePair, FsmBase> fsm in _fsmMap)
            {
                fsm.Value.Shutdown();
            }

            _fsmMap.Clear();
            _tempFsmList.Clear();
        }

        /// <summary>
        /// 检查是否存在有限状态机。
        /// </summary>
        /// <typeparam name="T">有限状态机持有者类型。</typeparam>
        /// <returns>是否存在有限状态机。</returns>
        public bool HasFsm<T>() where T : class
        {
            return InternalHasFsm(new TypeNamePair(typeof(T)));
        }

        /// <summary>
        /// 检查是否存在有限状态机。
        /// </summary>
        /// <param name="ownerType">有限状态机持有者类型。</param>
        /// <returns>是否存在有限状态机。</returns>
        public bool HasFsm(Type ownerType)
        {
            if (ownerType == null)
            {
                throw new GameFrameworkException("Owner type is invalid.");
            }

            return InternalHasFsm(new TypeNamePair(ownerType));
        }

        /// <summary>
        /// 检查是否存在有限状态机。
        /// </summary>
        /// <typeparam name="T">有限状态机持有者类型。</typeparam>
        /// <param name="name">有限状态机名称。</param>
        /// <returns>是否存在有限状态机。</returns>
        public bool HasFsm<T>(string name) where T : class
        {
            return InternalHasFsm(new TypeNamePair(typeof(T), name));
        }

        /// <summary>
        /// 检查是否存在有限状态机。
        /// </summary>
        /// <param name="ownerType">有限状态机持有者类型。</param>
        /// <param name="name">有限状态机名称。</param>
        /// <returns>是否存在有限状态机。</returns>
        public bool HasFsm(Type ownerType, string name)
        {
            if (ownerType == null)
            {
                throw new GameFrameworkException("Owner type is invalid.");
            }

            return InternalHasFsm(new TypeNamePair(ownerType, name));
        }

        /// <summary>
        /// 获取有限状态机。
        /// </summary>
        /// <typeparam name="T">有限状态机持有者类型。</typeparam>
        /// <returns>要获取的有限状态机。</returns>
        public IFsm<T> GetFsm<T>() where T : class
        {
            return (IFsm<T>)InternalGetFsm(new TypeNamePair(typeof(T)));
        }

        /// <summary>
        /// 获取有限状态机。
        /// </summary>
        /// <param name="ownerType">有限状态机持有者类型。</param>
        /// <returns>要获取的有限状态机。</returns>
        public FsmBase GetFsm(Type ownerType)
        {
            if (ownerType == null)
            {
                throw new GameFrameworkException("Owner type is invalid.");
            }

            return InternalGetFsm(new TypeNamePair(ownerType));
        }

        /// <summary>
        /// 获取有限状态机。
        /// </summary>
        /// <typeparam name="T">有限状态机持有者类型。</typeparam>
        /// <param name="name">有限状态机名称。</param>
        /// <returns>要获取的有限状态机。</returns>
        public IFsm<T> GetFsm<T>(string name) where T : class
        {
            return (IFsm<T>)InternalGetFsm(new TypeNamePair(typeof(T), name));
        }

        /// <summary>
        /// 获取有限状态机。
        /// </summary>
        /// <param name="ownerType">有限状态机持有者类型。</param>
        /// <param name="name">有限状态机名称。</param>
        /// <returns>要获取的有限状态机。</returns>
        public FsmBase GetFsm(Type ownerType, string name)
        {
            if (ownerType == null)
            {
                throw new GameFrameworkException("Owner type is invalid.");
            }

            return InternalGetFsm(new TypeNamePair(ownerType, name));
        }

        /// <summary>
        /// 获取所有有限状态机。
        /// </summary>
        /// <returns>所有有限状态机。</returns>
        public FsmBase[] GetAllFsms()
        {
            int index = 0;
            FsmBase[] results = new FsmBase[_fsmMap.Count];
            foreach (KeyValuePair<TypeNamePair, FsmBase> fsm in _fsmMap)
            {
                results[index++] = fsm.Value;
            }

            return results;
        }

        /// <summary>
        /// 获取所有有限状态机。
        /// </summary>
        /// <param name="results">所有有限状态机。</param>
        public void GetAllFsms(List<FsmBase> results)
        {
            if (results == null)
            {
                throw new GameFrameworkException("Results is invalid.");
            }

            results.Clear();
            foreach (KeyValuePair<TypeNamePair, FsmBase> fsm in _fsmMap)
            {
                results.Add(fsm.Value);
            }
        }

        /// <summary>
        /// 创建有限状态机。
        /// </summary>
        /// <typeparam name="T">有限状态机持有者类型。</typeparam>
        /// <param name="owner">有限状态机持有者。</param>
        /// <param name="states">有限状态机状态集合。</param>
        /// <returns>要创建的有限状态机。</returns>
        public IFsm<T> CreateFsm<T>(T owner, params FsmState<T>[] states) where T : class
        {
            return CreateFsm(string.Empty, owner, states);
        }

        /// <summary>
        /// 创建有限状态机。
        /// </summary>
        /// <typeparam name="T">有限状态机持有者类型。</typeparam>
        /// <param name="name">有限状态机名称。</param>
        /// <param name="owner">有限状态机持有者。</param>
        /// <param name="states">有限状态机状态集合。</param>
        /// <returns>要创建的有限状态机。</returns>
        public IFsm<T> CreateFsm<T>(string name, T owner, params FsmState<T>[] states) where T : class
        {
            TypeNamePair typeNamePair = new TypeNamePair(typeof(T), name);
            if (HasFsm<T>(name))
            {
                throw new GameFrameworkException(Utility.Text.Format("Already exist FSM '{0}'.", typeNamePair));
            }

            Fsm<T> fsm = Fsm<T>.Create(name, owner, states);
            _fsmMap.Add(typeNamePair, fsm);
            return fsm;
        }

        /// <summary>
        /// 创建有限状态机。
        /// </summary>
        /// <typeparam name="T">有限状态机持有者类型。</typeparam>
        /// <param name="owner">有限状态机持有者。</param>
        /// <param name="states">有限状态机状态集合。</param>
        /// <returns>要创建的有限状态机。</returns>
        public IFsm<T> CreateFsm<T>(T owner, List<FsmState<T>> states) where T : class
        {
            return CreateFsm(string.Empty, owner, states);
        }

        /// <summary>
        /// 创建有限状态机。
        /// </summary>
        /// <typeparam name="T">有限状态机持有者类型。</typeparam>
        /// <param name="name">有限状态机名称。</param>
        /// <param name="owner">有限状态机持有者。</param>
        /// <param name="states">有限状态机状态集合。</param>
        /// <returns>要创建的有限状态机。</returns>
        public IFsm<T> CreateFsm<T>(string name, T owner, List<FsmState<T>> states) where T : class
        {
            TypeNamePair typeNamePair = new TypeNamePair(typeof(T), name);
            if (HasFsm<T>(name))
            {
                throw new GameFrameworkException(Utility.Text.Format("Already exist FSM '{0}'.", typeNamePair));
            }

            Fsm<T> fsm = Fsm<T>.Create(name, owner, states);
            _fsmMap.Add(typeNamePair, fsm);
            return fsm;
        }

        /// <summary>
        /// 销毁有限状态机。
        /// </summary>
        /// <typeparam name="T">有限状态机持有者类型。</typeparam>
        /// <returns>是否销毁有限状态机成功。</returns>
        public bool DestroyFsm<T>() where T : class
        {
            return InternalDestroyFsm(new TypeNamePair(typeof(T)));
        }

        /// <summary>
        /// 销毁有限状态机。
        /// </summary>
        /// <param name="ownerType">有限状态机持有者类型。</param>
        /// <returns>是否销毁有限状态机成功。</returns>
        public bool DestroyFsm(Type ownerType)
        {
            if (ownerType == null)
            {
                throw new GameFrameworkException("Owner type is invalid.");
            }

            return InternalDestroyFsm(new TypeNamePair(ownerType));
        }

        /// <summary>
        /// 销毁有限状态机。
        /// </summary>
        /// <typeparam name="T">有限状态机持有者类型。</typeparam>
        /// <param name="name">要销毁的有限状态机名称。</param>
        /// <returns>是否销毁有限状态机成功。</returns>
        public bool DestroyFsm<T>(string name) where T : class
        {
            return InternalDestroyFsm(new TypeNamePair(typeof(T), name));
        }

        /// <summary>
        /// 销毁有限状态机。
        /// </summary>
        /// <param name="ownerType">有限状态机持有者类型。</param>
        /// <param name="name">要销毁的有限状态机名称。</param>
        /// <returns>是否销毁有限状态机成功。</returns>
        public bool DestroyFsm(Type ownerType, string name)
        {
            if (ownerType == null)
            {
                throw new GameFrameworkException("Owner type is invalid.");
            }

            return InternalDestroyFsm(new TypeNamePair(ownerType, name));
        }

        /// <summary>
        /// 销毁有限状态机。
        /// </summary>
        /// <typeparam name="T">有限状态机持有者类型。</typeparam>
        /// <param name="fsm">要销毁的有限状态机。</param>
        /// <returns>是否销毁有限状态机成功。</returns>
        public bool DestroyFsm<T>(IFsm<T> fsm) where T : class
        {
            if (fsm == null)
            {
                throw new GameFrameworkException("FSM is invalid.");
            }

            return InternalDestroyFsm(new TypeNamePair(typeof(T), fsm.Name));
        }

        /// <summary>
        /// 销毁有限状态机。
        /// </summary>
        /// <param name="fsm">要销毁的有限状态机。</param>
        /// <returns>是否销毁有限状态机成功。</returns>
        public bool DestroyFsm(FsmBase fsm)
        {
            if (fsm == null)
            {
                throw new GameFrameworkException("FSM is invalid.");
            }

            return InternalDestroyFsm(new TypeNamePair(fsm.OwnerType, fsm.Name));
        }

        private bool InternalHasFsm(TypeNamePair typeNamePair)
        {
            return _fsmMap.ContainsKey(typeNamePair);
        }

        private FsmBase InternalGetFsm(TypeNamePair typeNamePair)
        {
            FsmBase fsm = null;
            if (_fsmMap.TryGetValue(typeNamePair, out fsm))
            {
                return fsm;
            }

            return null;
        }

        private bool InternalDestroyFsm(TypeNamePair typeNamePair)
        {
            FsmBase fsm = null;
            if (_fsmMap.TryGetValue(typeNamePair, out fsm))
            {
                fsm.Shutdown();
                return _fsmMap.Remove(typeNamePair);
            }

            return false;
        }
    }
}