using UnityEngine;

namespace GameLogic
{
    public class SetUISafeFitHelper
    {
        /// <summary>
        /// 是否适配刘海屏
        /// </summary>
        public bool LiuHaiFit { get; set; } = false;

        /// <summary>
        /// 顶部适配偏移高度
        /// </summary>
        public float TopSpacing { get; set; } = 0;

        /// <summary>
        /// 是否底部适配
        /// </summary>
        public bool BottomFit { get; set; } = false;

        /// <summary>
        /// 底部适配偏移高度
        /// </summary>
        public float BottomSpacing { get; set; } = 0;

        private readonly RectTransform m_curFitRect;

        /// <summary>
        /// 移动设备屏幕适配
        /// </summary>
        /// <param name="fitRect">适配的RectTransform对象</param>
        /// <param name="liuHaiFit">是否开启刘海屏顶部适配</param>
        /// <param name="topSpacing">刘海屏顶部适配偏移高度</param>
        /// <param name="bottomFit">是否开启刘海屏底部适配</param>
        /// <param name="bottomSpacing">刘海屏底部适配偏移高度</param>
        public SetUISafeFitHelper(RectTransform fitRect, bool liuHaiFit = true, float topSpacing = 0, bool bottomFit = true, float bottomSpacing = 0)
        {
            LiuHaiFit = liuHaiFit;
            TopSpacing = topSpacing;
            BottomFit = bottomFit;
            BottomSpacing = bottomSpacing;
            m_curFitRect = fitRect;
        }

        public SetUISafeFitHelper() { }

        public void SetUIFit()
        {
            if (m_curFitRect == null)
            {
                return;
            }

            Vector3 offsetMax = new Vector2(0f, 0f);
            Vector3 offsetMin = new Vector2(0f, 0f);

            // 挖孔屏
            Rect[] cutouts = Screen.cutouts;

            switch (Application.platform)
            {
                case RuntimePlatform.WindowsPlayer:
                case RuntimePlatform.WindowsEditor:
                    TopSpacing = 70;
                    BottomSpacing = 80;
                    break;

                case RuntimePlatform.Android:
                    break;

                case RuntimePlatform.IPhonePlayer:
                    var phoneType = SystemInfo.deviceModel;
                    TopSpacing = 70;
                    BottomSpacing = 80;

                    if (phoneType == "iPhone12,1" || phoneType == "iPhone11,8")
                    {
                        //特定机型做下特点的偏移
                        TopSpacing = 30;
                        BottomSpacing = 70;
                    }

                    break;
            }

            //启动刘海适配
            if (LiuHaiFit)
            {
                if (cutouts != null && cutouts.Length > 0 && Application.platform != RuntimePlatform.IPhonePlayer)
                {
                    offsetMax = new Vector3(m_curFitRect.offsetMax.x, (cutouts[0].height));
                }
                else if (Screen.safeArea.yMax > 0 && Screen.height - Screen.safeArea.yMax > 0)
                {
                    offsetMax = new Vector3(Screen.width - Screen.safeArea.xMax, Screen.height - (Screen.safeArea.yMax + TopSpacing));
                }
                //刘海屏适配
                m_curFitRect.offsetMax = new Vector2(offsetMax.x, -offsetMax.y);
            }
            else
            {
                //非刘海屏适配
                m_curFitRect.offsetMax = offsetMax;
            }

            //启动底部适配
            if (BottomFit)
            {
                if (Screen.safeArea.y > 0)
                {
                    offsetMin = new Vector2(Screen.safeArea.x, Mathf.Abs(Screen.safeArea.y - BottomSpacing));
                }

                if (Mathf.Abs(offsetMin.y) > 0)
                {
                    m_curFitRect.offsetMin = new Vector2(m_curFitRect.offsetMin.x, Mathf.Abs(offsetMin.y));
                }
                else
                {
                    m_curFitRect.offsetMin = offsetMin;
                }
            }
            else
            {
                m_curFitRect.offsetMin = offsetMin;
            }
        }

        /// <summary>
        /// 设置某一个节点不受m_curRect影响
        /// </summary>
        /// <param name="rect"></param>
        public void SetUINotFit(RectTransform rect)
        {
            if (m_curFitRect == null || rect == null)
            {
                return;
            }
            // 获取当前适配 Rect 的局部位置
            Vector3 localPos = rect.localPosition;

            // 计算需要补偿的偏移量
            // offsetMax.y 通常是负值（顶部被裁切），所以需要加上这个值来补偿
            // offsetMin.y 通常是正值（底部被裁切），所以需要减去这个值来补偿
            float compensationY = -m_curFitRect.offsetMax.y; // - m_curFitRect.offsetMin.y;

            // 应用补偿
            rect.localPosition = new Vector3(localPos.x, localPos.y + compensationY, localPos.z);
        }

        /// <summary>
        /// 设置某一个节点不受指定RectTransform的影响
        /// </summary>
        /// <param name="rect">设置的RectTransform</param>
        /// <param name="refRect">依赖的RectTransform</param>
        public void SetUINotFit(RectTransform rect, RectTransform refRect)
        {
            if (rect == null || refRect == null)
            {
                return;
            }

            // 获取当前适配 Rect 的局部位置
            Vector3 localPos = rect.localPosition;

            // 计算需要补偿的偏移量
            // offsetMax.y 通常是负值（顶部被裁切），所以需要加上这个值来补偿
            // offsetMin.y 通常是正值（底部被裁切），所以需要减去这个值来补偿(实测不减去这个值会更接近)
            float compensationY = -refRect.offsetMax.y; // - m_curFitRect.offsetMin.y;

            // 应用补偿
            rect.localPosition = new Vector3(localPos.x, localPos.y + compensationY, localPos.z);
        }
    }
}