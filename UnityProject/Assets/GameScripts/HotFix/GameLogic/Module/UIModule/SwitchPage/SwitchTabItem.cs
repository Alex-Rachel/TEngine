using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>
    /// 页签项，用于显示页签按钮的选中/未选中状态。
    /// </summary>
    public class SwitchTabItem : UIEventItem<SwitchTabItem>
    {
        #region 脚本工具生成的代码

        private Transform m_tfNoSelectNode;
        private Image m_imgNoSelectBg;
        private Image m_imgNoSelectIcon;
        private Text m_textNoSelectText;
        private Transform m_tfSelectedNode;
        private Image m_imgSelectedBg;
        private Image m_imgSelectedIcon;
        private Text m_textSelectedText;
        private Transform m_tfRedNode;

        protected override void ScriptGenerator()
        {
            m_tfNoSelectNode = FindChild("m_tfNoSelectNode");
            m_imgNoSelectBg = FindChildComponent<Image>("m_tfNoSelectNode/m_imgNoSelectBg");
            m_imgNoSelectIcon = FindChildComponent<Image>("m_tfNoSelectNode/m_imgNoSelectIcon");
            m_textNoSelectText = FindChildComponent<Text>("m_tfNoSelectNode/m_textNoSelectText");
            m_tfSelectedNode = FindChild("m_tfSelectedNode");
            m_imgSelectedBg = FindChildComponent<Image>("m_tfSelectedNode/m_imgSelectedBg");
            m_imgSelectedIcon = FindChildComponent<Image>("m_tfSelectedNode/m_imgSelectedIcon");
            m_textSelectedText = FindChildComponent<Text>("m_tfSelectedNode/m_textSelectedText");
            m_tfRedNode = FindChild("m_tfRedNode");
        }

        #endregion

        #region Properties

        protected bool m_selected;

        /// <summary>
        /// 获取或设置选中状态。
        /// </summary>
        public bool Selected { get => m_selected; set => SetSelectedState(value); }

        #endregion

        #region 函数

        /// <summary>
        /// 设置 Tab 图标。
        /// </summary>
        /// <param name="selectedIconPath">选中状态图标路径</param>
        /// <param name="noSelectIconPath">未选中状态图标路径</param>
        public void SetTabIcon(string selectedIconPath, string noSelectIconPath)
        {
            m_imgSelectedIcon?.SetSprite(selectedIconPath, true);
            m_imgNoSelectIcon?.SetSprite(noSelectIconPath, true);
        }

        /// <summary>
        /// 设置 Tab 图标位置。
        /// </summary>
        /// <param name="selectedIconPos">选中状态图标位置</param>
        /// <param name="noSelectIconPos">未选中状态图标位置</param>
        public void SetTabIconPos(Vector2 selectedIconPos, Vector2 noSelectIconPos)
        {
            if (m_imgSelectedIcon != null && m_imgSelectedIcon.rectTransform != null)
            {
                m_imgSelectedIcon.rectTransform.localPosition = selectedIconPos;
            }
            if (m_imgNoSelectIcon != null && m_imgNoSelectIcon.rectTransform != null)
            {
                m_imgNoSelectIcon.rectTransform.localPosition = noSelectIconPos;
            }
        }

        /// <summary>
        /// 更新 Tab 名称。
        /// </summary>
        /// <param name="tabName">Tab 名称</param>
        public void UpdateTabName(string tabName)
        {
            if (m_textSelectedText != null)
            {
                m_textSelectedText.text = tabName;
            }
            if (m_textNoSelectText != null)
            {
                m_textNoSelectText.text = tabName;
            }
        }

        /// <summary>
        /// 更新 Tab 名称并调整大小。
        /// </summary>
        /// <param name="tabName">Tab 名称</param>
        /// <param name="isChangeSize">是否调整大小</param>
        public void UpdateTabNameChangeSize(string tabName, bool isChangeSize = true)
        {
            if (m_textSelectedText != null)
            {
                m_textSelectedText.text = tabName;

                if (isChangeSize)
                {
                    m_textSelectedText.rectTransform.sizeDelta = new Vector2(m_textSelectedText.preferredWidth,
                        m_textSelectedText.rectTransform.sizeDelta.y);
                }
            }
            if (m_textNoSelectText != null)
            {
                m_textNoSelectText.text = tabName;
                if (isChangeSize)
                {
                    m_textNoSelectText.rectTransform.sizeDelta = new Vector2(m_textNoSelectText.preferredWidth,
                        m_textNoSelectText.rectTransform.sizeDelta.y);
                }
            }
        }

        /// <summary>
        /// 设置 Tab 文本字体大小。
        /// </summary>
        /// <param name="fontSize">字体大小</param>
        public void SetTabTextFontSize(int fontSize)
        {
            if (m_textSelectedText != null)
            {
                m_textSelectedText.fontSize = fontSize;
            }

            if (m_textNoSelectText != null)
            {
                m_textNoSelectText.fontSize = fontSize;
            }
        }

        /// <summary>
        /// 设置 Tab 文本颜色。
        /// </summary>
        /// <param name="selectedTextColor">选中状态文本颜色（十六进制）</param>
        /// <param name="noSelectTextColor">未选中状态文本颜色（十六进制）</param>
        public void SetTabTextColor(Color selectedTextColor, Color noSelectTextColor)
        {
            if (m_textSelectedText != null)
            {
                m_textSelectedText.color = selectedTextColor;
            }

            if (m_textNoSelectText != null)
            {
                m_textNoSelectText.color = noSelectTextColor;
            }
        }

        /// <summary>
        /// 设置 Tab 背景图。
        /// </summary>
        /// <param name="selectedBgPath">选中状态背景路径</param>
        /// <param name="noSelectBgPath">未选中状态背景路径</param>
        public void SetTabBg(string selectedBgPath, string noSelectBgPath)
        {
            if (!string.IsNullOrEmpty(selectedBgPath))
            {
                m_imgSelectedBg?.SetSprite(selectedBgPath);
            }
            if (!string.IsNullOrEmpty(noSelectBgPath))
            {
                m_imgNoSelectBg?.SetSprite(noSelectBgPath);
            }
        }

        /// <summary>
        /// 设置选中状态。
        /// </summary>
        /// <param name="isSelected">是否选中</param>
        public virtual void SetSelectedState(bool isSelected)
        {
            m_selected = isSelected;
            m_tfSelectedNode?.gameObject.SetActive(isSelected);
            m_tfNoSelectNode?.gameObject.SetActive(!isSelected);
        }

        /// <summary>
        /// 设置红点显示状态。
        /// </summary>
        /// <param name="isActive">是否显示红点</param>
        public virtual void SetRedNodeActive(bool isActive)
        {
            m_tfRedNode?.gameObject.SetActive(isActive);
        }

        #endregion
    }
}