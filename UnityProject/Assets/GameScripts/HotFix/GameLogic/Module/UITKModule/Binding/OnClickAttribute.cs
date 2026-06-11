using System;

namespace GameLogic
{
    /// <summary>
    /// 点击事件绑定标记。方法名自动推导目标元素。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class OnClickAttribute : Attribute
    {
        public string Target { get; }
        public OnClickAttribute() { }
        public OnClickAttribute(string target) => Target = target;
    }
}
