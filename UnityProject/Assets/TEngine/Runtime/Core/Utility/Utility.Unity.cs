using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Internal;

namespace TEngine
{
    public static partial class Utility
    {
        /// <summary>
        /// Unity相关的实用函数。
        /// </summary>
        public static partial class Unity
        {
            private static IUpdateDriver _updateDriver;

            #region 控制协程Coroutine

            public static GameCoroutine StartCoroutine(string name, IEnumerator routine, MonoBehaviour bindBehaviour)
            {
                if (bindBehaviour == null)
                {
                    Log.Error("StartCoroutine {0} failed, bindBehaviour is null", name);
                    return null;
                }

                var behaviour = bindBehaviour;
                return StartCoroutine(behaviour, name, routine);
            }

            public static GameCoroutine StartCoroutine(string name, IEnumerator routine, GameObject bindGo)
            {
                if (bindGo == null)
                {
                    Log.Error("StartCoroutine {0} failed, BindGo is null", name);
                    return null;
                }

                var behaviour = GetDefaultBehaviour(bindGo);
                return StartCoroutine(behaviour, name, routine);
            }

            public static GameCoroutine StartGlobalCoroutine(string name, IEnumerator routine)
            {
                var coroutine = StartCoroutine(routine);
                var gameCoroutine = new GameCoroutine();
                gameCoroutine.Coroutine = coroutine;
                gameCoroutine.Name = name;
                gameCoroutine.BindBehaviour = null;
                return gameCoroutine;
            }

            public static void StopCoroutine(GameCoroutine coroutine)
            {
                if (coroutine.Coroutine != null)
                {
                    var behaviour = coroutine.BindBehaviour;
                    if (behaviour != null)
                    {
                        behaviour.StopCoroutine(coroutine.Coroutine);
                    }

                    coroutine.Coroutine = null;
                    coroutine.BindBehaviour = null;
                }
            }

            private static GameCoroutine StartCoroutine(MonoBehaviour behaviour, string name, IEnumerator routine)
            {
                var coroutine = behaviour.StartCoroutine(routine);
                var gameCoroutine = new GameCoroutine();
                gameCoroutine.Coroutine = coroutine;
                gameCoroutine.Name = name;
                gameCoroutine.BindBehaviour = behaviour;
                return gameCoroutine;
            }

            private static GameCoroutineAgent GetDefaultBehaviour(GameObject bindGameObject)
            {
                if (bindGameObject != null)
                {
                    if (bindGameObject.TryGetComponent(out GameCoroutineAgent coroutineBehaviour))
                    {
                        return coroutineBehaviour;
                    }

                    return bindGameObject.AddComponent<GameCoroutineAgent>();
                }

                return null;
            }


            public static Coroutine StartCoroutine(string methodName)
            {
                if (string.IsNullOrEmpty(methodName))
                {
                    return null;
                }

                _MakeEntity();
                return _updateDriver.StartCoroutine(methodName);
            }

            public static Coroutine StartCoroutine(IEnumerator routine)
            {
                if (routine == null)
                {
                    return null;
                }

                _MakeEntity();
                return _updateDriver.StartCoroutine(routine);
            }

            public static Coroutine StartCoroutine(string methodName, [DefaultValue("null")] object value)
            {
                if (string.IsNullOrEmpty(methodName))
                {
                    return null;
                }

                _MakeEntity();
                return _updateDriver.StartCoroutine(methodName, value);
            }

            public static void StopCoroutine(string methodName)
            {
                if (string.IsNullOrEmpty(methodName))
                {
                    return;
                }

                _MakeEntity();
                _updateDriver.StopCoroutine(methodName);
            }

            public static void StopCoroutine(IEnumerator routine)
            {
                if (routine == null)
                {
                    return;
                }

                _MakeEntity();
                _updateDriver.StopCoroutine(routine);
            }

            public static void StopCoroutine(Coroutine routine)
            {
                if (routine == null)
                {
                    return;
                }

                _MakeEntity();
                _updateDriver.StopCoroutine(routine);
                routine = null;
            }

            public static void StopAllCoroutines()
            {
                _MakeEntity();
                _updateDriver.StopAllCoroutines();
            }

            #endregion

            #region 注入UnityUpdate/FixedUpdate/LateUpdate

            /// <summary>
            /// 为给外部提供的 添加帧更新事件。
            /// </summary>
            /// <param name="fun"></param>
            public static void AddUpdateListener(Action fun)
            {
                _MakeEntity();
                AddUpdateListenerImp(fun).Forget();
            }

            private static async UniTaskVoid AddUpdateListenerImp(Action fun)
            {
                await UniTask.Yield( /*PlayerLoopTiming.LastPreUpdate*/);
                _updateDriver.AddUpdateListener(fun);
            }

            /// <summary>
            /// 为给外部提供的 添加物理帧更新事件。
            /// </summary>
            /// <param name="fun"></param>
            public static void AddFixedUpdateListener(Action fun)
            {
                _MakeEntity();
                AddFixedUpdateListenerImp(fun).Forget();
            }

            private static async UniTaskVoid AddFixedUpdateListenerImp(Action fun)
            {
                await UniTask.Yield(PlayerLoopTiming.LastEarlyUpdate);
                _updateDriver.AddFixedUpdateListener(fun);
            }

            /// <summary>
            /// 为给外部提供的 添加Late帧更新事件。
            /// </summary>
            /// <param name="fun"></param>
            public static void AddLateUpdateListener(Action fun)
            {
                _MakeEntity();
                AddLateUpdateListenerImp(fun).Forget();
            }

            private static async UniTaskVoid AddLateUpdateListenerImp(Action fun)
            {
                await UniTask.Yield( /*PlayerLoopTiming.LastPreLateUpdate*/);
                _updateDriver.AddLateUpdateListener(fun);
            }

            /// <summary>
            /// 移除帧更新事件。
            /// </summary>
            /// <param name="fun"></param>
            public static void RemoveUpdateListener(Action fun)
            {
                _MakeEntity();
                _updateDriver.RemoveUpdateListener(fun);
            }

            /// <summary>
            /// 移除物理帧更新事件。
            /// </summary>
            /// <param name="fun"></param>
            public static void RemoveFixedUpdateListener(Action fun)
            {
                _MakeEntity();
                _updateDriver.RemoveFixedUpdateListener(fun);
            }

            /// <summary>
            /// 移除Late帧更新事件。
            /// </summary>
            /// <param name="fun"></param>
            public static void RemoveLateUpdateListener(Action fun)
            {
                _MakeEntity();
                _updateDriver.RemoveLateUpdateListener(fun);
            }

            #endregion

            #region Unity Events 注入

            /// <summary>
            /// 为给外部提供的Destroy注册事件。
            /// </summary>
            /// <param name="fun"></param>
            public static void AddDestroyListener(Action fun)
            {
                _MakeEntity();
                _updateDriver.AddDestroyListener(fun);
            }

            /// <summary>
            /// 为给外部提供的Destroy反注册事件。
            /// </summary>
            /// <param name="fun"></param>
            public static void RemoveDestroyListener(Action fun)
            {
                _MakeEntity();
                _updateDriver.RemoveDestroyListener(fun);
            }

            /// <summary>
            /// 为给外部提供的OnDrawGizmos注册事件。
            /// </summary>
            /// <param name="fun"></param>
            public static void AddOnDrawGizmosListener(Action fun)
            {
                _MakeEntity();
                _updateDriver.AddOnDrawGizmosListener(fun);
            }

            /// <summary>
            /// 为给外部提供的OnDrawGizmos反注册事件。
            /// </summary>
            /// <param name="fun"></param>
            public static void RemoveOnDrawGizmosListener(Action fun)
            {
                _MakeEntity();
                _updateDriver.RemoveOnDrawGizmosListener(fun);
            }

            /// <summary>
            /// 为给外部提供的OnApplicationPause注册事件。
            /// </summary>
            /// <param name="fun"></param>
            public static void AddOnApplicationPauseListener(Action<bool> fun)
            {
                _MakeEntity();
                _updateDriver.AddOnApplicationPauseListener(fun);
            }

            /// <summary>
            /// 为给外部提供的OnApplicationPause反注册事件。
            /// </summary>
            /// <param name="fun"></param>
            public static void RemoveOnApplicationPauseListener(Action<bool> fun)
            {
                _MakeEntity();
                _updateDriver.RemoveOnApplicationPauseListener(fun);
            }

            #endregion

            private static void _MakeEntity()
            {
                if (_updateDriver != null)
                {
                    return;
                }

                _updateDriver = ModuleSystem.GetModule<IUpdateDriver>();
            }

            #region FindObjectOfType
            public static T FindObjectOfType<T>() where T : UnityEngine.Object
            {
#if UNITY_6000_0_OR_NEWER
                return UnityEngine.Object.FindFirstObjectByType<T>();
#else
                return UnityEngine.Object.FindObjectOfType<T>();

#endif
            }

            #endregion
        }

        public class GameCoroutine
        {
            public string Name;
            public Coroutine Coroutine;
            public MonoBehaviour BindBehaviour;
        }

        class GameCoroutineAgent : MonoBehaviour
        {
        }
    }
}