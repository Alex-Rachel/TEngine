using Cysharp.Threading.Tasks;

namespace GameLogic
{
    public sealed partial class UITKModule
    {
        public async UniTask CloseUIWithAnimation<T>() where T : UITKWindow
        {
            string windowName = typeof(T).FullName;
            UITKWindow window = GetWindow(windowName);
            if (window == null) return;

            await window.OnHideAnimation();
            window.InternalDestroy();
            Pop(window);
            OnSetWindowVisible();
        }

        public async UniTask HideUIWithAnimation<T>() where T : UITKWindow
        {
            string windowName = typeof(T).FullName;
            UITKWindow window = GetWindow(windowName);
            if (window == null) return;

            if (window.HideTimeToClose <= 0)
            {
                await CloseUIWithAnimation<T>();
                return;
            }

            await window.OnHideAnimation();
            window.Visible = false;
            window.IsHide = true;
            window.HideTimerId = GameModule.Timer.AddTimer((arg) =>
            {
                CloseUI(typeof(T));
            }, window.HideTimeToClose);

            if (window.FullScreen)
            {
                OnSetWindowVisible();
            }
        }
    }
}
