using System;

namespace GameLogic
{
    /// <summary>
    /// 命令绑定标记。将按钮绑定到 ViewModel 的 BindableCommand。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class BindCommandAttribute : Attribute
    {
        public string CommandName { get; }
        public BindCommandAttribute(string commandName) => CommandName = commandName;
    }
}
