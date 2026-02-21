using UnityEngine;
using UnityEngine.UI;
using System;

namespace Launcher
{
    public class LoadTipsUI : UIBase
    {
        #region 脚本工具生成的代码

        private Text m_textDesc;
        private Button m_btnConfirm;
        private Text m_textConfirm;
        private Button m_btnUpdate;
        private Text m_textUpdate;
        private Button m_btnCancel;
        private Text m_textCancel;

        protected override void ScriptGenerator()
        {
            m_textDesc = FindChildComponent<Text>("BgImage/m_textDesc");
            m_btnConfirm = FindChildComponent<Button>("BgImage/ButtonGroup/m_btnConfirm");
            m_textConfirm = FindChildComponent<Text>("BgImage/ButtonGroup/m_btnConfirm/m_textConfirm");
            m_btnUpdate = FindChildComponent<Button>("BgImage/ButtonGroup/m_btnUpdate");
            m_textUpdate = FindChildComponent<Text>("BgImage/ButtonGroup/m_btnUpdate/m_textUpdate");
            m_btnCancel = FindChildComponent<Button>("BgImage/ButtonGroup/m_btnCancel");
            m_textCancel = FindChildComponent<Text>("BgImage/ButtonGroup/m_btnCancel/m_textCancel");
            m_btnConfirm.onClick.AddListener(OnClickConfirmBtn);
            m_btnUpdate.onClick.AddListener(OnClickUpdateBtn);
            m_btnCancel.onClick.AddListener(OnClickCancelBtn);
        }

        #endregion

        private const string m_cancelText = "Cancel";
        private const string m_confirmText = "Confirm";
        private const string m_updateText = "Update";

        public Action OnConfirmClick { get; set; }
        public Action OnUpdateClick { get; set; }
        public Action OnCancelClick { get; set; }

        public override void OnInit(object data)
        {
            base.OnInit(data);
            m_textCancel.text = m_cancelText;
            m_textUpdate.text = m_updateText;
            m_textConfirm.text = m_confirmText;

            m_btnUpdate.gameObject.SetActive(false);
            m_btnCancel.gameObject.SetActive(false);
            m_btnConfirm.gameObject.SetActive(false);

            m_textDesc.text = data?.ToString();
        }

        public void SetAllCallback(Action onConfirm, Action onUpdate, Action onCancel)
        {
            m_btnUpdate.gameObject.SetActive(false);
            m_btnCancel.gameObject.SetActive(false);
            m_btnConfirm.gameObject.SetActive(false);
            if (onConfirm != null)
            {
                OnConfirmClick = onConfirm;
                m_btnConfirm.gameObject.SetActive(true);
            }
            if (onUpdate != null)
            {
                OnUpdateClick = onUpdate;
                m_btnUpdate.gameObject.SetActive(true);
            }
            if (onCancel != null)
            {
                OnCancelClick = onCancel;
                m_btnCancel.gameObject.SetActive(true);
            }
        }

        private void OnClickUpdateBtn()
        {
            OnUpdateClick?.Invoke();
            Close();
        }

        private void OnClickCancelBtn()
        {
            OnCancelClick?.Invoke();
            Close();
        }

        private void OnClickConfirmBtn()
        {
            OnConfirmClick?.Invoke();
            Close();
        }
    }
}