#if UNITY_2019_4_OR_NEWER
using System;
using UnityEditor;
using UnityEngine;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace YooAsset.Editor
{
    public class ResizeHandle : VisualElement
    {
        public new class UxmlFactory : UxmlFactory<ResizeHandle, UxmlTraits>
        {
        }

        private bool _isResizing = false;
        private float _initialWidth;
        private Vector2 _initialMousePos;

        /// <summary>
        /// 控制的UI元素
        /// </summary>
        public VisualElement ControlTarget { get; set; }

        /// <summary>
        /// 控制元素的最小宽度
        /// </summary>
        public int ControlMinWidth { get; set; }

        /// <summary>
        /// 控制元素的最大宽度
        /// </summary>
        public int ControlMaxWidth { get; set; }

        /// <summary>
        /// 尺寸发生变化
        /// </summary>
        public Action<float> ResizeChanged { get; set; }

        public ResizeHandle()
        {
            int defaultWidth = 5;
            this.style.width = defaultWidth;
            this.style.minWidth = defaultWidth;
            this.style.maxWidth = defaultWidth;
            this.style.opacity = 0;
            this.style.cursor = UIElementsCursor.CreateCursor(MouseCursor.ResizeHorizontal);

            this.RegisterCallback<MouseDownEvent>(OnMouseDown);
            this.RegisterCallback<MouseMoveEvent>(OnMouseMove);
            this.RegisterCallback<MouseUpEvent>(OnMouseUp);
        }
        public ResizeHandle(int handleWidth, VisualElement controlTarget, int controlMinWidth, int controlMaxWidth)
        {
            ControlTarget = controlTarget;
            ControlMinWidth = controlMinWidth;
            ControlMaxWidth = controlMaxWidth;

            this.style.width = handleWidth;
            this.style.minWidth = handleWidth;
            this.style.maxWidth = handleWidth;
            this.style.opacity = 0;
            this.style.cursor = UIElementsCursor.CreateCursor(MouseCursor.ResizeHorizontal);

            this.RegisterCallback<MouseDownEvent>(OnMouseDown);
            this.RegisterCallback<MouseMoveEvent>(OnMouseMove);
            this.RegisterCallback<MouseUpEvent>(OnMouseUp);
        }

        private void OnMouseDown(MouseDownEvent evt)
        {
            // 鼠标左键按下
            if (ControlTarget != null && evt.button == 0)
            {
                _isResizing = true;
                _initialWidth = ControlTarget.resolvedStyle.width;
                _initialMousePos = evt.mousePosition;
                this.CaptureMouse();
            }
        }
        private void OnMouseMove(MouseMoveEvent evt)
        {
            if (ControlTarget != null && _isResizing)
            {
                // 计算鼠标移动距离
                float deltaX = evt.mousePosition.x - _initialMousePos.x;

                // 更新控制元素尺寸
                float newWidth = _initialWidth + deltaX;
                float width = Mathf.Clamp(newWidth, ControlMinWidth, ControlMaxWidth);
                ControlTarget.style.width = width;
                ControlTarget.style.minWidth = width;
                ControlTarget.style.maxWidth = width;
                ResizeChanged?.Invoke(width);
            }
        }
        private void OnMouseUp(MouseUpEvent evt)
        {
            // 鼠标左键释放
            if (ControlTarget != null && evt.button == 0)
            {
                _isResizing = false;
                this.ReleaseMouse();
            }
        }
    }
}
#endif