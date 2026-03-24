namespace GameLogic
{
    /// <summary>
    /// 子页面共享数据类
    /// </summary>
    public class ChildPageShareData
    {
        private object[] m_shareDatas = new object[3];

        /// <summary>
        /// 获取共享数据1
        /// </summary>
        public object ShareData1 => m_shareDatas[0];

        /// <summary>
        /// 获取共享数据2
        /// </summary>
        public object ShareData2 => m_shareDatas[1];

        /// <summary>
        /// 获取共享数据3
        /// </summary>
        public object ShareData3 => m_shareDatas[2];

        /// <summary>
        /// 设置共享数据
        /// </summary>
        /// <param name="index">数据索引(0-2)</param>
        /// <param name="shareData">共享数据</param>
        public void SetShareData(int index, object shareData)
        {
            if (index < 0 || index >= m_shareDatas.Length)
            {
                return;
            }
            m_shareDatas[index] = shareData;
        }
    }

    /// <summary>
    /// 子页面基类
    /// </summary>
    public class BaseChildPage : UIWidget
    {
        protected ChildPageShareData m_shareData;
        protected SwitchPageMgr m_switchPageMgr;

        /// <summary>
        /// 初始化子页面
        /// </summary>
        /// <param name="shareData">共享数据</param>
        /// <param name="switchPageMgr">页签切换管理器</param>
        public void Init(ChildPageShareData shareData, SwitchPageMgr switchPageMgr)
        {
            m_shareData = shareData;
            m_switchPageMgr = switchPageMgr;
        }

        /// <summary>
        /// 当页面显示时调用
        /// </summary>
        /// <param name="oldShowType">旧的页面类型</param>
        /// <param name="newShowType">新的页面类型</param>
        public virtual void OnPageShowed(int oldShowType, int newShowType)
        { }

        /// <summary>
        /// 刷新当前子页面
        /// </summary>
        public virtual void RefreshCurrentChildPage()
        { }

        /// <summary>
        /// 获取或设置共享数据1
        /// </summary>
        public object ShareData1 {get => m_shareData.ShareData1; set => m_shareData.SetShareData(0, value); }

        /// <summary>
        /// 获取或设置共享数据2
        /// </summary>
        public object ShareData2 {get => m_shareData.ShareData2; set => m_shareData.SetShareData(1, value); }

        /// <summary>
        /// 获取或设置共享数据3
        /// </summary>
        public object ShareData3 {get => m_shareData.ShareData3; set => m_shareData.SetShareData(2, value); }
    }
}