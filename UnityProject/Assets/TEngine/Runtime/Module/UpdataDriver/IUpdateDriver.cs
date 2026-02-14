using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Internal;

namespace TEngine
{
    public interface IUpdateDriver
    {
        #region 控制协程Coroutine

        public Coroutine StartCoroutine(string methodName);

        public Coroutine StartCoroutine(IEnumerator routine);

        public Coroutine StartCoroutine(string methodName, [DefaultValue("null")] object value);

        public void StopCoroutine(string methodName);

        public void StopCoroutine(IEnumerator routine);

        public void StopCoroutine(Coroutine routine);

        public void StopAllCoroutines();

        #endregion

        #region 注入UnityUpdate/FixedUpdate/LateUpdate

        /// <summary>
        /// 为给外部提供的 添加帧更新事件。
        /// </summary>
        /// <param name="action"></param>
        public void AddUpdateListener(Action action);

        /// <summary>
        /// 为给外部提供的 添加物理帧更新事件。
        /// </summary>
        /// <param name="action"></param>
        public void AddFixedUpdateListener(Action action);

        /// <summary>
        /// 为给外部提供的 添加Late帧更新事件。
        /// </summary>
        /// <param name="action"></param>
        public void AddLateUpdateListener(Action action);

        /// <summary>
        /// 移除帧更新事件。
        /// </summary>
        /// <param name="action"></param>
        public void RemoveUpdateListener(Action action);

        /// <summary>
        /// 移除物理帧更新事件。
        /// </summary>
        /// <param name="action"></param>
        public void RemoveFixedUpdateListener(Action action);

        /// <summary>
        /// 移除Late帧更新事件。
        /// </summary>
        /// <param name="action"></param>
        public void RemoveLateUpdateListener(Action action);

        #endregion

        #region Unity Events 注入

        /// <summary>
        /// 为给外部提供的Destroy注册事件。
        /// </summary>
        /// <param name="action"></param>
        public void AddDestroyListener(Action action);

        /// <summary>
        /// 为给外部提供的Destroy反注册事件。
        /// </summary>
        /// <param name="action"></param>
        public void RemoveDestroyListener(Action action);

        /// <summary>
        /// 为给外部提供的OnDrawGizmos注册事件。
        /// </summary>
        /// <param name="action"></param>
        public void AddOnDrawGizmosListener(Action action);

        /// <summary>
        /// 为给外部提供的OnDrawGizmos反注册事件。
        /// </summary>
        /// <param name="action"></param>
        public void RemoveOnDrawGizmosListener(Action action);

        /// <summary>
        /// 为给外部提供的OnDrawGizmosSelected注册事件。
        /// </summary>
        /// <param name="action"></param>
        public void AddOnDrawGizmosSelectedListener(Action action);

        /// <summary>
        /// 为给外部提供的OnDrawGizmosSelected反注册事件。
        /// </summary>
        /// <param name="action"></param>
        public void RemoveOnDrawGizmosSelectedListener(Action action);

        /// <summary>
        /// 为给外部提供的OnApplicationPause注册事件。
        /// </summary>
        /// <param name="action"></param>
        public void AddOnApplicationPauseListener(Action<bool> action);

        /// <summary>
        /// 为给外部提供的OnApplicationPause反注册事件。
        /// </summary>
        /// <param name="action"></param>
        public void RemoveOnApplicationPauseListener(Action<bool> action);

        #endregion
    }
}