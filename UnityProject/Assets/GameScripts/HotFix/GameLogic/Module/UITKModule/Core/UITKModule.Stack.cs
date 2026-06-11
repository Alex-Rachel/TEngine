using Cysharp.Threading.Tasks;

namespace GameLogic
{
    public sealed partial class UITKModule
    {
        private UITKWindow GetWindow(string windowName)
        {
            for (int i = 0; i < _windowStack.Count; i++)
            {
                if (_windowStack[i].WindowName == windowName)
                    return _windowStack[i];
            }
            return null;
        }

        private void Push(UITKWindow window)
        {
            int insertIndex = -1;
            for (int i = 0; i < _windowStack.Count; i++)
            {
                if (window.WindowLayer == _windowStack[i].WindowLayer)
                {
                    insertIndex = i + 1;
                }
            }

            if (insertIndex == -1)
            {
                for (int i = 0; i < _windowStack.Count; i++)
                {
                    if (window.WindowLayer > _windowStack[i].WindowLayer)
                    {
                        insertIndex = i + 1;
                    }
                }
            }

            if (insertIndex == -1)
            {
                insertIndex = 0;
            }

            _windowStack.Insert(insertIndex, window);
        }

        private void Pop(UITKWindow window)
        {
            _windowStack.Remove(window);
        }

        private void OnWindowPrepare(UITKWindow window)
        {
            window.InternalCreate();
            window.InternalRefresh();
            OnSetWindowVisible();
            window.OnShowAnimation().Forget();
        }

        private void OnSetWindowVisible()
        {
            bool isHideNext = false;
            for (int i = _windowStack.Count - 1; i >= 0; i--)
            {
                UITKWindow window = _windowStack[i];
                if (!isHideNext)
                {
                    if (window.IsHide) continue;
                    window.Visible = true;
                    if (window.IsPrepare && window.FullScreen)
                    {
                        isHideNext = true;
                    }
                }
                else
                {
                    window.Visible = false;
                }
            }
        }
    }
}
