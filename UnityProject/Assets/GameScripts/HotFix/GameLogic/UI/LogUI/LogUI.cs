using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Game.UI;
using TEngine;
using UnityEngine.UI;

namespace GameLogic
{
    [Window(UILayer.Top, true, -1)]
    class LogUI : UIWindow<gen_LogUI>
    {
        private readonly Stack<string> _errorTextString = new Stack<string>();


        #region 事件

        private void OnClickCloseBtn()
        {
            PopErrorLog().Forget();
        }

        #endregion

        protected override void OnOpen()
        {
            _errorTextString.Push(UserData.ToString());
            baseui.TextError.text = UserData.ToString();
        }

        private async UniTaskVoid PopErrorLog()
        {
            if (_errorTextString.Count <= 0)
            {
                await UniTask.Yield();
                CloseSelf();
                return;
            }

            string error = _errorTextString.Pop();
            baseui.TextError.text = error;
        }
    }
}