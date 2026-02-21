using UnityEngine;

namespace Launcher
{
    /// <summary>
    /// 热更UI基类。
    /// </summary>
    public class UIBase
    {
        public GameObject gameObject;

        public Transform transform
        {
            get
            {
                if (gameObject != null)
                {
                    return gameObject.transform;
                }

                return null;
            }
        }

        public RectTransform rectTransform => transform as RectTransform;

        protected virtual bool FullScreen => false;

        protected object m_param;

        protected virtual void ScriptGenerator() { }

        public void CallScriptGenerator()
        {
            ScriptGenerator();
        }

        public virtual void OnInit(object param)
        {
            m_param = param;
        }

        public void Show()
        {
            gameObject?.SetActive(true);
        }

        public void Hide()
        {
            gameObject?.SetActive(false);
        }

        public void Close()
        {
            LauncherMgr.CloseUI(this);
        }

        #region FindChildComponent

        public Transform FindChild(string path)
        {
            return FindChildImp(rectTransform, path);
        }

        public Transform FindChild(Transform trans, string path)
        {
            return FindChildImp(trans, path);
        }

        public T FindChildComponent<T>(string path) where T : Component
        {
            return FindChildComponentImp<T>(rectTransform, path);
        }

        public T FindChildComponent<T>(Transform trans, string path) where T : Component
        {
            return FindChildComponentImp<T>(trans, path);
        }

        private static Transform FindChildImp(Transform trans, string path)
        {
            var findTrans = trans.Find(path);
            return findTrans == null ? null : findTrans;
        }

        private static T FindChildComponentImp<T>(Transform trans, string path) where T : Component
        {
            var findTrans = trans.Find(path);
            return findTrans == null ? null : findTrans.gameObject.GetComponent<T>();
        }

        #endregion
    }
}