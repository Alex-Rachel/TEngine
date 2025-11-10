using System;
using Cysharp.Threading.Tasks;
using TEngine;
using UnityEngine;

namespace TEngine
{
    public interface IUIModule : IUpdateModule
    {
        void Initlize(Transform root,bool isOrthographic);
        Camera UICamera { get; set; }
        Transform UICanvasRoot { get; set; }
        UniTask<UIBase> ShowUI<T>(params System.Object[] userDatas) where T : UIBase;
        UniTask<UIBase>? ShowUI(string type, params object[] userDatas);
        UniTask<T> ShowUIAsync<T>(params System.Object[] userDatas) where T : UIBase;
        void CloseUI<T>(bool force = false) where T : UIBase;
        T GetUI<T>() where T : UIBase;

        void CloseUI(RuntimeTypeHandle handle, bool force = false);
        protected internal void SetTimerManager(ITimerModule timerModule);
    }
}
