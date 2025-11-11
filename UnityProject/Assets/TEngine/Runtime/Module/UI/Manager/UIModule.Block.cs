using System.Collections.Generic;
using UnityEngine;

namespace TEngine
{
    internal sealed partial class UIModule
    {
        private GameObject m_LayerBlock; //内部屏蔽对象 显示时之下的所有UI将不可操作

        private int m_LastCountDownGuid; //倒计时的唯一ID

        private void InitUIBlock()
        {
            m_LayerBlock = new GameObject("LayerBlock");
            var rect = m_LayerBlock.AddComponent<RectTransform>();
            m_LayerBlock.AddComponent<CanvasRenderer>();
            m_LayerBlock.AddComponent<UIBlock>();
            rect.SetParent(UICanvasRoot);
            rect.SetAsLastSibling();
            // rect.ResetToFullScreen();
            SetLayerBlockOption(false);
        }

        /// <summary>
        /// 设置UI遮挡
        /// </summary>
        /// <param name="timeDuration">倒计时/s</param>
        public void SetUIBlock(float timeDuration)
        {
            if (m_LastCountDownGuid != 0)
            {
                _timerModule.RemoveTimer(m_LastCountDownGuid);
            }

            SetLayerBlockOption(true);
            m_LastCountDownGuid = _timerModule.AddTimer(OnBlockCountDown, timeDuration);
        }

        /// <summary>
        /// 强制退出UI遮挡
        /// </summary>
        public void ForceExitBlock()
        {
            if (m_LastCountDownGuid != 0)
            {
                _timerModule.RemoveTimer(m_LastCountDownGuid);
            }

            RecoverLayerOptionAll();
        }

        private void OnBlockCountDown(object[] args)
        {
            RecoverLayerOptionAll();
        }

        /// <summary>
        /// 设置UI是否可以操作
        /// 不能提供此API对外操作
        /// 因为有人设置过后就会忘记恢复
        /// 如果你确实需要你可以设置 禁止无限时间
        /// 之后调用恢复操作也可以做到
        /// </summary>
        /// <param name="value">true = 可以操作 = 屏蔽层会被隐藏</param>
        private void SetLayerBlockOption(bool value)
        {
            m_LayerBlock.SetActive(value);
        }

        /// <summary>
        /// 强制恢复层级到可操作状态
        /// 此方法会强制打断倒计时 根据需求调用
        /// </summary>
        public void RecoverLayerOptionAll()
        {
            SetLayerBlockOption(false);
            m_LastCountDownGuid = 0;
        }
    }
}
