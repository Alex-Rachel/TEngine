using System;
using System.Collections;
using System.Diagnostics;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Internal;
using Object = UnityEngine.Object;

namespace TEngine
{
    internal class UpdateDriver : Module, IUpdateDriver
    {
        private GameObject _entity;
        private MainBehaviour _behaviour;

        public override void OnInit()
        {
            _MakeEntity();
        }
        
        /// <summary>
        /// 释放Behaviour生命周期。
        /// </summary>
        public override void Shutdown()
        {
            if (_behaviour != null)
            {
                _behaviour.Release();
            }

            if (_entity != null)
            {
                Object.Destroy(_entity);
            }

            _entity = null;
        }

        #region 控制协程Coroutine

        public Coroutine StartCoroutine(string methodName)
        {
            if (string.IsNullOrEmpty(methodName))
            {
                return null;
            }

            _MakeEntity();
            return _behaviour.StartCoroutine(methodName);
        }

        public Coroutine StartCoroutine(IEnumerator routine)
        {
            if (routine == null)
            {
                return null;
            }

            _MakeEntity();
            return _behaviour.StartCoroutine(routine);
        }

        public Coroutine StartCoroutine(string methodName, [DefaultValue("null")] object value)
        {
            if (string.IsNullOrEmpty(methodName))
            {
                return null;
            }

            _MakeEntity();
            return _behaviour.StartCoroutine(methodName, value);
        }

        public void StopCoroutine(string methodName)
        {
            if (string.IsNullOrEmpty(methodName))
            {
                return;
            }

            if (_entity != null)
            {
                _behaviour.StopCoroutine(methodName);
            }
        }

        public void StopCoroutine(IEnumerator routine)
        {
            if (routine == null)
            {
                return;
            }

            if (_entity != null)
            {
                _behaviour.StopCoroutine(routine);
            }
        }

        public void StopCoroutine(Coroutine routine)
        {
            if (routine == null)
                return;

            if (_entity != null)
            {
                _behaviour.StopCoroutine(routine);
                routine = null;
            }
        }

        public void StopAllCoroutines()
        {
            if (_entity != null)
            {
                _behaviour.StopAllCoroutines();
            }
        }

        #endregion

        #region 注入UnityUpdate/FixedUpdate/LateUpdate

        /// <summary>
        /// 为给外部提供的 添加帧更新事件。
        /// </summary>
        /// <param name="action"></param>
        public void AddUpdateListener(Action action)
        {
            _MakeEntity();
            AddUpdateListenerImp(action).Forget();
        }

        private async UniTaskVoid AddUpdateListenerImp(Action action)
        {
            await UniTask.Yield();
            _behaviour.AddUpdateListener(action);
        }

        /// <summary>
        /// 为给外部提供的 添加物理帧更新事件。
        /// </summary>
        /// <param name="action"></param>
        public void AddFixedUpdateListener(Action action)
        {
            _MakeEntity();
            AddFixedUpdateListenerImp(action).Forget();
        }

        private async UniTaskVoid AddFixedUpdateListenerImp(Action action)
        {
            await UniTask.Yield(PlayerLoopTiming.LastEarlyUpdate);
            _behaviour.AddFixedUpdateListener(action);
        }

        /// <summary>
        /// 为给外部提供的 添加Late帧更新事件。
        /// </summary>
        /// <param name="action"></param>
        public void AddLateUpdateListener(Action action)
        {
            _MakeEntity();
            AddLateUpdateListenerImp(action).Forget();
        }

        private async UniTaskVoid AddLateUpdateListenerImp(Action action)
        {
            await UniTask.Yield();
            _behaviour.AddLateUpdateListener(action);
        }

        /// <summary>
        /// 移除帧更新事件。
        /// </summary>
        /// <param name="action"></param>
        public void RemoveUpdateListener(Action action)
        {
            _MakeEntity();
            _behaviour.RemoveUpdateListener(action);
        }

        /// <summary>
        /// 移除物理帧更新事件。
        /// </summary>
        /// <param name="action"></param>
        public void RemoveFixedUpdateListener(Action action)
        {
            _MakeEntity();
            _behaviour.RemoveFixedUpdateListener(action);
        }

        /// <summary>
        /// 移除Late帧更新事件。
        /// </summary>
        /// <param name="action"></param>
        public void RemoveLateUpdateListener(Action action)
        {
            _MakeEntity();
            _behaviour.RemoveLateUpdateListener(action);
        }

        #endregion

        #region Unity Events 注入

        /// <summary>
        /// 为给外部提供的Destroy注册事件。
        /// </summary>
        /// <param name="action"></param>
        public void AddDestroyListener(Action action)
        {
            _MakeEntity();
            _behaviour.AddDestroyListener(action);
        }

        /// <summary>
        /// 为给外部提供的Destroy反注册事件。
        /// </summary>
        /// <param name="action"></param>
        public void RemoveDestroyListener(Action action)
        {
            _MakeEntity();
            _behaviour.RemoveDestroyListener(action);
        }

        /// <summary>
        /// 为给外部提供的OnDrawGizmos注册事件。
        /// </summary>
        /// <param name="action"></param>
        public void AddOnDrawGizmosListener(Action action)
        {
            _MakeEntity();
            _behaviour.AddOnDrawGizmosListener(action);
        }

        /// <summary>
        /// 为给外部提供的OnDrawGizmos反注册事件。
        /// </summary>
        /// <param name="action"></param>
        public void RemoveOnDrawGizmosListener(Action action)
        {
            _MakeEntity();
            _behaviour.RemoveOnDrawGizmosListener(action);
        }
        
        /// <summary>
        /// 为给外部提供的OnDrawGizmosSelected注册事件。
        /// </summary>
        /// <param name="action"></param>
        public void AddOnDrawGizmosSelectedListener(Action action)
        {
            _MakeEntity();
            _behaviour.AddOnDrawGizmosSelectedListener(action);
        }

        /// <summary>
        /// 为给外部提供的OnDrawGizmosSelected反注册事件。
        /// </summary>
        /// <param name="action"></param>
        public void RemoveOnDrawGizmosSelectedListener(Action action)
        {
            _MakeEntity();
            _behaviour.RemoveOnDrawGizmosSelectedListener(action);
        }

        /// <summary>
        /// 为给外部提供的OnApplicationPause注册事件。
        /// </summary>
        /// <param name="action"></param>
        public void AddOnApplicationPauseListener(Action<bool> action)
        {
            _MakeEntity();
            _behaviour.AddOnApplicationPauseListener(action);
        }

        /// <summary>
        /// 为给外部提供的OnApplicationPause反注册事件。
        /// </summary>
        /// <param name="action"></param>
        public void RemoveOnApplicationPauseListener(Action<bool> action)
        {
            _MakeEntity();
            _behaviour.RemoveOnApplicationPauseListener(action);
        }

        #endregion

        private void _MakeEntity()
        {
            if (_entity != null)
            {
                return;
            }

            _entity = new GameObject("[UpdateDriver]");
            _entity.SetActive(true);
            Object.DontDestroyOnLoad(_entity);
            _behaviour = _entity.AddComponent<MainBehaviour>();
        }

        private class MainBehaviour : MonoBehaviour
        {
            private event Action UpdateEvent;
            private event Action FixedUpdateEvent;
            private event Action LateUpdateEvent;
            private event Action DestroyEvent;
            private event Action OnDrawGizmosEvent;
            private event Action OnDrawGizmosSelectedEvent;
            private event Action<bool> OnApplicationPauseEvent;

            void Update()
            {
                if (UpdateEvent != null)
                {
                    UpdateEvent();
                }
            }

            void FixedUpdate()
            {
                if (FixedUpdateEvent != null)
                {
                    FixedUpdateEvent();
                }
            }

            void LateUpdate()
            {
                if (LateUpdateEvent != null)
                {
                    LateUpdateEvent();
                }
            }

            private void OnDestroy()
            {
                if (DestroyEvent != null)
                {
                    DestroyEvent();
                }
            }

            [Conditional("UNITY_EDITOR")]
            private void OnDrawGizmos()
            {
                if (OnDrawGizmosEvent != null)
                {
                    OnDrawGizmosEvent();
                }
            }
            
            [Conditional("UNITY_EDITOR")]
            private void OnDrawGizmosSelected()
            {
                if (OnDrawGizmosSelectedEvent != null)
                {
                    OnDrawGizmosSelectedEvent();
                }
            }

            private void OnApplicationPause(bool pauseStatus)
            {
                if (OnApplicationPauseEvent != null)
                {
                    OnApplicationPauseEvent(pauseStatus);
                }
            }

            public void AddLateUpdateListener(Action action)
            {
                LateUpdateEvent += action;
            }

            public void RemoveLateUpdateListener(Action action)
            {
                LateUpdateEvent -= action;
            }

            public void AddFixedUpdateListener(Action action)
            {
                FixedUpdateEvent += action;
            }

            public void RemoveFixedUpdateListener(Action action)
            {
                FixedUpdateEvent -= action;
            }

            public void AddUpdateListener(Action action)
            {
                UpdateEvent += action;
            }

            public void RemoveUpdateListener(Action action)
            {
                UpdateEvent -= action;
            }

            public void AddDestroyListener(Action action)
            {
                DestroyEvent += action;
            }

            public void RemoveDestroyListener(Action action)
            {
                DestroyEvent -= action;
            }

            [Conditional("UNITY_EDITOR")]
            public void AddOnDrawGizmosListener(Action action)
            {
                OnDrawGizmosEvent += action;
            }

            [Conditional("UNITY_EDITOR")]
            public void RemoveOnDrawGizmosListener(Action action)
            {
                OnDrawGizmosEvent -= action;
            }
            
            [Conditional("UNITY_EDITOR")]
            public void AddOnDrawGizmosSelectedListener(Action action)
            {
                OnDrawGizmosSelectedEvent += action;
            }

            [Conditional("UNITY_EDITOR")]
            public void RemoveOnDrawGizmosSelectedListener(Action action)
            {
                OnDrawGizmosSelectedEvent -= action;
            }

            public void AddOnApplicationPauseListener(Action<bool> action)
            {
                OnApplicationPauseEvent += action;
            }

            public void RemoveOnApplicationPauseListener(Action<bool> action)
            {
                OnApplicationPauseEvent -= action;
            }

            public void Release()
            {
                UpdateEvent = null;
                FixedUpdateEvent = null;
                LateUpdateEvent = null;
                OnDrawGizmosEvent = null;
                OnDrawGizmosSelectedEvent = null;
                DestroyEvent = null;
                OnApplicationPauseEvent = null;
            }
        }
    }
}