using System;
using System.Collections.Generic;
using TEngine;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace GameLogic
{
    public interface ISingleton
    {
        /// <summary>
        /// 激活接口，通常用于在某个时机手动实例化
        /// </summary>
        void Active();

        /// <summary>
        /// 释放接口
        /// </summary>
        void Release();
    }

    public interface IUpdate
    {
        /// <summary>
        /// 游戏框架模块轮询。
        /// </summary>
        void OnUpdate();
    }

    public interface IFixedUpdate
    {
        /// <summary>
        /// 游戏框架模块轮询。
        /// </summary>
        void OnFixedUpdate();
    }

    public interface ILateUpdate
    {
        /// <summary>
        /// 游戏框架模块轮询。
        /// </summary>
        void OnLateUpdate();
    }

    public interface IDrawGizmos
    {
        void OnDrawGizmos();
    }
    
    public interface IDrawGizmosSelected
    {
        void OnDrawGizmosSelected();
    }

    /// <summary>
    /// 框架中的全局对象与Unity场景依赖相关的DontDestroyOnLoad需要统一管理，方便重启游戏时清除工作
    /// </summary>
    public static class SingletonSystem
    {
        private static IUpdateDriver _updateDriver;
        private static readonly List<ISingleton> _singletons = new List<ISingleton>();
        private static readonly List<IUpdate> _updates = new List<IUpdate>();
        private static readonly List<IFixedUpdate> _fixedUpdates = new List<IFixedUpdate>();
        private static readonly List<ILateUpdate> _lateUpdates = new List<ILateUpdate>();
#if UNITY_EDITOR
        private static readonly List<IDrawGizmos> _drawGizmos = new List<IDrawGizmos>();
        private static readonly List<IDrawGizmosSelected> _drawGizmosSelecteds = new List<IDrawGizmosSelected>();
#endif
        
        private static readonly Dictionary<string, GameObject> _gameObjects = new Dictionary<string, GameObject>();

        public static void Retain(ISingleton singleton)
        {
            CheckInit();

            _singletons.Add(singleton);

            BuildLifeCycle(singleton);
        }

        public static void Retain(GameObject go, object singleton)
        {
            CheckInit();

            if (_gameObjects.TryAdd(go.name, go))
            {
                if (Application.isPlaying)
                {
                    Object.DontDestroyOnLoad(go);
                }

                BuildLifeCycle(singleton);
            }
        }

        private static void BuildLifeCycle(object singleton)
        {
            Type iUpdate = typeof(IUpdate);
            bool needUpdate = iUpdate.IsInstanceOfType(singleton);
            if (needUpdate && singleton is IUpdate update)
            {
                _updates.Add(update);
            }

            Type iFixedUpdate = typeof(IFixedUpdate);
            bool needFixedUpdate = iFixedUpdate.IsInstanceOfType(singleton);
            if (needFixedUpdate && singleton is IFixedUpdate fixedUpdate)
            {
                _fixedUpdates.Add(fixedUpdate);
            }

            Type iLateUpdate = typeof(ILateUpdate);
            bool needLateUpdate = iLateUpdate.IsInstanceOfType(singleton);
            if (needLateUpdate && singleton is ILateUpdate lateUpdate)
            {
                _lateUpdates.Add(lateUpdate);
            }

#if UNITY_EDITOR
            Type iDrawGizmos = typeof(IDrawGizmos);
            bool needDrawGizmos = iDrawGizmos.IsInstanceOfType(singleton);
            if (needDrawGizmos && singleton is IDrawGizmos drawGizmos)
            {
                _drawGizmos.Add(drawGizmos);
            }
            
            Type iDrawGizmosSelected = typeof(IDrawGizmosSelected);
            bool needDrawGizmosSelected = iDrawGizmosSelected.IsInstanceOfType(singleton);
            if (needDrawGizmosSelected && singleton is IDrawGizmosSelected drawGizmosSelected)
            {
                _drawGizmosSelecteds.Add(drawGizmosSelected);
            }
#endif
        }

        public static void Release(GameObject go, object singleton)
        {
            if (_gameObjects != null && _gameObjects.ContainsKey(go.name))
            {
                _gameObjects.Remove(go.name);
                Object.Destroy(go);
                ReleaseLifeCycle(singleton);
            }
        }

        public static void Release(ISingleton singleton)
        {
            if (_singletons != null && _singletons.Contains(singleton))
            {
                _singletons.Remove(singleton);
                ReleaseLifeCycle(singleton);
            }
        }
        
        private static void ReleaseLifeCycle(object singleton)
        {
            Type iUpdate = typeof(IUpdate);
            bool needUpdate = iUpdate.IsInstanceOfType(singleton);
            if (needUpdate && singleton is IUpdate update)
            {
                if (_updates.Contains(update))
                {
                    _updates.Remove(update);
                }
            }

            Type iFixedUpdate = typeof(IFixedUpdate);
            bool needFixedUpdate = iFixedUpdate.IsInstanceOfType(singleton);
            if (needFixedUpdate && singleton is IFixedUpdate fixedUpdate)
            {
                if (_fixedUpdates.Contains(fixedUpdate))
                {
                    _fixedUpdates.Remove(fixedUpdate);
                }
            }

            Type iLateUpdate = typeof(ILateUpdate);
            bool needLateUpdate = iLateUpdate.IsInstanceOfType(singleton);
            if (needLateUpdate && singleton is ILateUpdate lateUpdate)
            {
                if (_lateUpdates.Contains(lateUpdate))
                {
                    _lateUpdates.Remove(lateUpdate);
                }
            }

#if UNITY_EDITOR
            Type iDrawGizmos = typeof(IDrawGizmos);
            bool needDrawGizmos = iDrawGizmos.IsInstanceOfType(singleton);
            if (needDrawGizmos && singleton is IDrawGizmos drawGizmos)
            {
                if (_drawGizmos.Contains(drawGizmos))
                {
                    _drawGizmos.Remove(drawGizmos);
                }
            }
            
            Type iDrawGizmosSelected = typeof(IDrawGizmosSelected);
            bool needDrawGizmosSelected = iDrawGizmosSelected.IsInstanceOfType(singleton);
            if (needDrawGizmosSelected && singleton is IDrawGizmosSelected drawGizmosSelected)
            {
                if (_drawGizmosSelecteds.Contains(drawGizmosSelected))
                {
                    _drawGizmosSelecteds.Remove(drawGizmosSelected);
                }
            }
#endif
        }

        public static void Release()
        {
            if (_gameObjects != null)
            {
                foreach (var item in _gameObjects)
                {
                    Object.Destroy(item.Value);
                }

                _gameObjects.Clear();
            }

            if (_singletons != null)
            {
                for (int i = _singletons.Count - 1; i >= 0; i--)
                {
                    _singletons[i].Release();
                }

                _singletons.Clear();
            }

            Resources.UnloadUnusedAssets();
        }

        public static GameObject GetGameObject(string name)
        {
            GameObject go = null;
            if (_gameObjects != null)
            {
                _gameObjects.TryGetValue(name, out go);
            }

            return go;
        }

        internal static bool ContainsKey(string name)
        {
            if (_gameObjects != null)
            {
                return _gameObjects.ContainsKey(name);
            }

            return false;
        }

        public static void Restart()
        {
            if (Camera.main != null)
            {
                Camera.main.gameObject.SetActive(false);
            }

            Release();
            SceneManager.LoadScene(0);
        }

        internal static ISingleton GetSingleton(string name)
        {
            for (int i = 0; i < _singletons.Count; ++i)
            {
                if (_singletons[i].ToString() == name)
                {
                    return _singletons[i];
                }
            }

            return null;
        }

        #region 生命周期

        private static bool _isInit = false;

        private static void CheckInit()
        {
            if (_isInit == true)
            {
                return;
            }
            
            _isInit = true;

            _updateDriver ??= ModuleSystem.GetModule<IUpdateDriver>();
            _updateDriver.AddUpdateListener(OnUpdate);
            _updateDriver.AddFixedUpdateListener(OnFixedUpdate);
            _updateDriver.AddLateUpdateListener(OnLateUpdate);
#if UNITY_EDITOR
            _updateDriver.AddOnDrawGizmosListener(OnDrawGizmos);
            _updateDriver.AddOnDrawGizmosSelectedListener(OnDrawGizmosSelected);
#endif
        }
        
        private static void DeInit()
        {
            if (_isInit == false)
            {
                return;
            }

            _isInit = false;

            _updateDriver ??= ModuleSystem.GetModule<IUpdateDriver>();
            _updateDriver.RemoveUpdateListener(OnUpdate);
            _updateDriver.RemoveFixedUpdateListener(OnFixedUpdate);
            _updateDriver.RemoveLateUpdateListener(OnLateUpdate);
#if UNITY_EDITOR
            _updateDriver.RemoveOnDrawGizmosListener(OnDrawGizmos);
            _updateDriver.RemoveOnDrawGizmosSelectedListener(OnDrawGizmosSelected);
#endif
        }

        private static void OnUpdate()
        {
            foreach (var update in _updates)
            {
                update.OnUpdate();
            }
        }

        private static void OnFixedUpdate()
        {
            foreach (var fixedUpdate in _fixedUpdates)
            {
                fixedUpdate.OnFixedUpdate();
            }
        }

        private static void OnLateUpdate()
        {
            foreach (var lateUpdate in _lateUpdates)
            {
                lateUpdate.OnLateUpdate();
            }
        }

        private static void OnDrawGizmos()
        {
#if UNITY_EDITOR
            foreach (var drawGizmo in _drawGizmos)
            {
                drawGizmo.OnDrawGizmos();
            }
#endif
        }
            
        private static void OnDrawGizmosSelected()
        {
#if UNITY_EDITOR
            foreach (var drawGizmosSelected in _drawGizmosSelecteds)
            {
                drawGizmosSelected.OnDrawGizmosSelected();
            }
#endif
        }
        #endregion
    }
}