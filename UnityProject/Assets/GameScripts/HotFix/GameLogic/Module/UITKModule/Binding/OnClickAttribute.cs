using System;

namespace GameLogic
{
    /// <summary>
    /// 点击事件绑定标记。方法名自动推导目标元素。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class OnClickAttribute : Attribute
    {
        /// <summary>目标元素名。null 则从方法名推导。</summary>
        public string Target { get; }

        public OnClickAttribute() { }
        public OnClickAttribute(string target) => Target = target;
    }
}
