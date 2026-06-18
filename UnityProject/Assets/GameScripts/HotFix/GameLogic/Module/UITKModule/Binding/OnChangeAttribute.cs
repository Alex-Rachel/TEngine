using System;

namespace GameLogic
{
    /// <summary>
    /// 值变化事件绑定标记。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class OnChangeAttribute : Attribute
    {
        public string Target { get; }
        public OnChangeAttribute(string target) => Target = target;
    }
}
