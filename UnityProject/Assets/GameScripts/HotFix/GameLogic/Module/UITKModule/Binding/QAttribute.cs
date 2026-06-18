using System;

namespace GameLogic
{
    /// <summary>
    /// 元素查询绑定标记。字段名自动转为 kebab-case 匹配 UXML name。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class QAttribute : Attribute
    {
        public string Name { get; }
        public QAttribute() { }
        public QAttribute(string name) => Name = name;
    }
}
