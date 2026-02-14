// ReSharper disable StaticMemberInGenericType
namespace Fantasy.Entitas.Interface
{
    /// <summary>
    /// 实体接口支持性的编译时检查器。
    /// </summary>
    /// <typeparam name="T">要检查的实体类型，必须继承自 <see cref="Entity"/></typeparam>
    /// <remarks>
    /// 性能优势：
    /// <list type="bullet">
    /// <item><description>静态字段在每个具体类型实例化时仅初始化一次</description></item>
    /// <item><description>JIT编译器会将静态布尔值内联为常量，实现分支消除优化</description></item>
    /// <item><description>避免重复的运行时类型检查开销</description></item>
    /// <item><description>多个相关检查集中在同一个静态类，提高CPU缓存局部性</description></item>
    /// </list>
    /// </remarks>
    public static class EntitySupportedChecker<T> where T : Entity
    {
        /// <summary>
        /// 获取实体类型是否实现了 <see cref="ISupportedMultiEntity"/> 接口。
        /// 实现该接口的实体支持在父实体中添加多个同类型的组件实例。
        /// </summary>
        /// <value>
        /// 如果实体类型实现了 <see cref="ISupportedMultiEntity"/> 接口，则为 <c>true</c>；否则为 <c>false</c>。
        /// </value>
        public static bool IsMulti { get; }
#if FANTASY_NET
        /// <summary>
        /// 获取实体类型是否实现了 <see cref="ISupportedSerialize"/> 接口。
        /// 实现该接口的实体支持数据库持久化存储和序列化。
        /// </summary>
        /// <value>
        /// 如果实体类型实现了 <see cref="ISupportedSerialize"/> 接口，则为 <c>true</c>；否则为 <c>false</c>。
        /// </value>
        public static bool IsSerialize { get; }

        /// <summary>
        /// 获取实体类型是否实现了 <see cref="ISupportedTransfer"/> 接口。
        /// 实现该接口的实体支持跨进程传输（如服务器间传送）。
        /// </summary>
        /// <value>
        /// 如果实体类型实现了 <see cref="ISupportedTransfer"/> 接口，则为 <c>true</c>；否则为 <c>false</c>。
        /// </value>
        public static bool IsTransfer { get; }
#endif
        /// <summary>
        /// 静态构造函数，在首次访问该泛型类型时执行一次，缓存所有接口检查结果。
        /// </summary>
        static EntitySupportedChecker()
        {
            var type = typeof(T);
            IsMulti = typeof(ISupportedMultiEntity).IsAssignableFrom(type);
#if FANTASY_NET
            IsSerialize = typeof(ISupportedSerialize).IsAssignableFrom(type);
            IsTransfer = typeof(ISupportedTransfer).IsAssignableFrom(type);
#endif
        }
    }
}