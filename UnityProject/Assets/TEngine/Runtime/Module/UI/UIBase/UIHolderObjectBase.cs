using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace TEngine
{
    [DisallowMultipleComponent]
    public abstract class UIHolderObjectBase : UnityEngine.MonoBehaviour
    {
        public Action OnWindowInitEvent;
        public Action OnWindowShowEvent;
        public Action OnWindowClosedEvent;
        public Action OnWindowDestroyEvent;
        


        private GameObject _target;

        /// <summary>
        /// UI实例资源对象。
        /// </summary>
        public GameObject Target => _target ??= gameObject;


        private RectTransform _rectTransform;

        /// <summary>
        /// 窗口矩阵位置组件。
        /// </summary>
        public RectTransform RectTransform => _rectTransform ??= _target.transform as RectTransform;

        /// <summary>
        /// 可见性
        /// </summary>
        public bool Visible
        {
            get => Target.activeSelf;

            internal set { _target.SetActive(value); }
        }
        
        private void Awake()
        {
            _target = gameObject;
        }

        private bool IsAlive = true;

        public static implicit operator bool(UIHolderObjectBase exists)
        {
            // 先检查Unity对象是否被销毁
            if (exists == null) return false;
            // 再返回自定义的生命状态
            return exists.IsAlive;
        }

        private void OnDestroy()
        {
            IsAlive = false;
        }
    }
}
