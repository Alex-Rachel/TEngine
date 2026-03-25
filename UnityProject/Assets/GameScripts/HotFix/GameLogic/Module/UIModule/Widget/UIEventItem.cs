using System;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GameLogic
{
    public class UIEventItem<T> : UIWidget where T : UIEventItem<T>
    {
        private object m_eventParam1;
        private object m_eventParam2;
        private object m_eventParam3;
        /// <summary>
        /// 事件参数1
        /// </summary>
        public object EventParam1 => m_eventParam1;
        /// <summary>
        /// 事件参数2
        /// </summary>
        public object EventParam2 => m_eventParam2;
        /// <summary>
        /// 事件参数3
        /// </summary>
        public object EventParam3 => m_eventParam3;
        private Action<T> m_clickAction;
        private Action<T, bool> m_pressAction;
        private Action<T, PointerEventData> m_beginDragAction;
        private Action<T, PointerEventData> m_dragAction;
        private Action<T, PointerEventData> m_endDragAction;

        /// <summary>
        /// 绑定点击事件（扩展版）
        /// </summary>
        /// <param name="clickAction">点击回调</param>
        /// <param name="eParam1">事件参数1</param>
        /// <param name="eParam2">事件参数2</param>
        /// <param name="eParam3">事件参数3</param>
        /// <param name="transition">按钮过渡效果</param>
        public void BindClickEventEx(Action<T> clickAction, object eParam1 = null, object eParam2 = null,
            object eParam3 = null, Selectable.Transition transition = Selectable.Transition.None)
        {
            if (m_clickAction == null)
            {
                var button = TEngine.DUnityUtil.AddMonoBehaviour<Button>(gameObject);
                button.transition = transition;
                button.onClick.AddListener(() => { m_clickAction?.Invoke(this as T); });
            }

            m_clickAction = clickAction;
            SetEventParam(eParam1, eParam2, eParam3);
        }

        /// <summary>
        /// 绑定点击事件
        /// </summary>
        /// <param name="clickAction">点击回调</param>
        /// <param name="eParam1">事件参数1</param>
        /// <param name="eParam2">事件参数2</param>
        /// <param name="eParam3">事件参数3</param>
        /// <param name="needEmptyImg">是否需要添加空Image组件用于射线检测</param>
        public void BindClickEvent(Action<T> clickAction, object eParam1 = null, object eParam2 = null,
            object eParam3 = null, bool needEmptyImg = false)
        {
            if (m_clickAction == null)
            {
                var button = TEngine.DUnityUtil.AddMonoBehaviour<Button>(gameObject);
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => { m_clickAction?.Invoke(this as T); });

                if (needEmptyImg)
                {
                    TEngine.DUnityUtil.AddMonoBehaviour<EmptyGraph>(gameObject);
                }
            }

            m_clickAction = clickAction;
            SetEventParam(eParam1, eParam2, eParam3);
        }

        /// <summary>
        /// 绑定开始拖拽事件
        /// </summary>
        /// <param name="dragAction">拖拽回调</param>
        /// <param name="eParam1">事件参数1</param>
        /// <param name="eParam2">事件参数2</param>
        /// <param name="eParam3">事件参数3</param>
        public void BindBeginDragEvent(Action<T, PointerEventData> dragAction, object eParam1 = null,
            object eParam2 = null, object eParam3 = null)
        {
            if (m_beginDragAction == null)
            {
                var trigger = TEngine.DUnityUtil.AddMonoBehaviour<EventTrigger>(gameObject);
                EventTrigger.Entry entry = new EventTrigger.Entry
                {
                    eventID = EventTriggerType.BeginDrag,
                    callback = new EventTrigger.TriggerEvent()
                };
                entry.callback.AddListener(data => { m_beginDragAction?.Invoke(this as T, (PointerEventData)data); });
                trigger.triggers.Add(entry);
            }

            m_beginDragAction = dragAction;
            SetEventParam(eParam1, eParam2, eParam3);
        }

        /// <summary>
        /// 绑定拖拽事件
        /// </summary>
        /// <param name="dragAction">拖拽回调</param>
        /// <param name="eParam1">事件参数1</param>
        /// <param name="eParam2">事件参数2</param>
        /// <param name="eParam3">事件参数3</param>
        public void BindDragEvent(Action<T, PointerEventData> dragAction, object eParam1 = null, object eParam2 = null,
            object eParam3 = null)
        {
            if (m_dragAction == null)
            {
                var trigger = TEngine.DUnityUtil.AddMonoBehaviour<EventTrigger>(gameObject);
                EventTrigger.Entry entry = new EventTrigger.Entry();
                entry.eventID = EventTriggerType.Drag;
                entry.callback = new EventTrigger.TriggerEvent();
                entry.callback.AddListener(data => { m_dragAction?.Invoke(this as T, (PointerEventData)data); });
                trigger.triggers.Add(entry);
            }

            m_dragAction = dragAction;
            SetEventParam(eParam1, eParam2, eParam3);
        }

        /// <summary>
        /// 绑定结束拖拽事件
        /// </summary>
        /// <param name="dragendAction">拖拽结束回调</param>
        /// <param name="eParam1">事件参数1</param>
        /// <param name="eParam2">事件参数2</param>
        /// <param name="eParam3">事件参数3</param>
        public void BindEndDragEvent(Action<T, PointerEventData> dragendAction, object eParam1 = null,
            object eParam2 = null, object eParam3 = null)
        {
            if (m_endDragAction == null)
            {
                m_endDragAction = dragendAction;
                var trigger = TEngine.DUnityUtil.AddMonoBehaviour<EventTrigger>(gameObject);
                EventTrigger.Entry entry = new EventTrigger.Entry();
                entry.eventID = EventTriggerType.EndDrag;
                entry.callback = new EventTrigger.TriggerEvent();
                entry.callback.AddListener(data => { m_endDragAction?.Invoke(this as T, (PointerEventData)data); });
                trigger.triggers.Add(entry);
            }

            m_endDragAction = dragendAction;
            SetEventParam(eParam1, eParam2, eParam3);
        }

        /// <summary>
        /// 绑定按下事件
        /// </summary>
        /// <param name="pressAction">按下回调，参数表示是否按下</param>
        /// <param name="eParam1">事件参数1</param>
        /// <param name="eParam2">事件参数2</param>
        /// <param name="eParam3">事件参数3</param>
        public void BindPressEvent(Action<T, bool> pressAction, object eParam1 = null, object eParam2 = null,
            object eParam3 = null)
        {
            if (m_pressAction == null)
            {
                var trigger = TEngine.DUnityUtil.AddMonoBehaviour<EventTrigger>(gameObject);
                EventTrigger.Entry entry = new EventTrigger.Entry();
                entry.eventID = EventTriggerType.PointerDown;
                entry.callback = new EventTrigger.TriggerEvent();
                entry.callback.AddListener(_ => { m_pressAction?.Invoke(this as T, true); });
                trigger.triggers.Add(entry);
                entry = new EventTrigger.Entry();
                entry.eventID = EventTriggerType.PointerUp;
                entry.callback = new EventTrigger.TriggerEvent();
                entry.callback.AddListener(_ => { m_pressAction?.Invoke(this as T, false); });
                trigger.triggers.Add(entry);
            }

            m_pressAction = pressAction;
            SetEventParam(eParam1, eParam2, eParam3);
        }

        /// <summary>
        /// 设置事件参数
        /// </summary>
        public void SetEventParam(object eParam1, object eParam2 = null, object eParam3 = null)
        {
            m_eventParam1 = eParam1;
            m_eventParam2 = eParam2;
            m_eventParam3 = eParam3;
        }

        /// <summary>
        /// 触发按钮点击事件
        /// </summary>
        public void OnTriggerBtnEvent()
        {
            if (m_clickAction != null)
            {
                m_clickAction(this as T);
            }
        }
    }
}