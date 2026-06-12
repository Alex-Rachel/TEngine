using Cysharp.Threading.Tasks;

namespace GameLogic
{
    public sealed partial class UITKModule
    {
        public async UniTask CloseUIWithAnimation<T>() where T : UITKWindow
        {
            string windowName = typeof(T).FullName;
            UITKWindow window = GetWindow(windowName);
            if (window == null || window.IsClosing) return;

            window.IsClosing = true;
            window.RootElement?.SetEnabled(false); // 动画期间屏蔽输入，避免二次触发

            await window.OnHideAnimation();

            // 动画期间窗口可能被重新 Show（TryInvoke 会把 IsClosing 复位为 false），
            // 此时不能再销毁，否则会销毁刚显示出来的窗口。
            if (!window.IsClosing) return;

            window.InternalDestroy();
            Pop(window);
            OnSetWindowVisible();
        }

        public async UniTask HideUIWithAnimation<T>() where T : UITKWindow
        {
            string windowName = typeof(T).FullName;
            UITKWindow window = GetWindow(windowName);
            if (window == null || window.IsClosing) return;

            if (window.HideTimeToClose <= 0)
            {
                await CloseUIWithAnimation<T>();
                return;
            }

            window.IsClosing = true;
            window.RootElement?.SetEnabled(false);

            await window.OnHideAnimation();

            // 动画期间被重新 Show（IsClosing 复位）则中止隐藏，避免把刚显示的窗口又隐藏并挂上关闭定时器。
            if (!window.IsClosing) return;

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
