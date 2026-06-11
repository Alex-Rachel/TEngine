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

        /// <summary>
        /// 音效名。null=使用默认音效，""=静音，其他=指定音效。
        /// </summary>
        public string Sound { get; }

        public OnClickAttribute() { }
        public OnClickAttribute(string target) => Target = target;
        public OnClickAttribute(string target, string sound) { Target = target; Sound = sound; }
}
