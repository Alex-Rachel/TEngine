using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine.UI;

namespace GameLogic
{
    [Window(UILayer.System, fromResources: true)]
    class LogUI : UIWindow
    {
        private readonly Stack<string> _errorTextString = new Stack<string>();

        #region 脚本工具生成的代码

        private Text m_textError;
        private Button m_btnClose;

        protected override void ScriptGenerator()
        {
            m_textError = FindChildComponent<Text>("m_textError");
            m_btnClose = FindChildComponent<Button>("m_btnClose");
            m_btnClose.onClick.AddListener(OnClickCloseBtn);
        }

        #endregion

        #region 事件

        private void OnClickCloseBtn()
        {
            PopErrorLog().Forget();
        }

        #endregion

        protected override void OnRefresh()
        {
            _errorTextString.Push(UserData.ToString());
            m_textError.text = UserData.ToString();
        }

        private async UniTaskVoid PopErrorLog()
        {
            if (_errorTextString.Count <= 0)
            {
                await UniTask.Yield();
                Close();
                return;
            }

            string error = _errorTextString.Pop();
            m_textError.text = error;
        }
    }
}