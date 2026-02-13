using System;

namespace Fantasy.Assembly
{
    /// <summary>
    /// MemoryPack Entity 序列化生成器接口。此接口由 Source Generator 自动生成的类实现，
    /// 用于在编译时注册标记了 [MemoryPackable] 的 Entity 子类的类型信息。
    /// </summary>
    /// <remarks>
    /// 此接口支持 Native AOT 编译和程序集热重载，通过提供类型哈希和类型数组，
    /// 实现 Entity 的多态序列化。Roslyn Source Generator (MemoryPackGenerator)
    /// 会自动扫描所有标记了 [MemoryPackable] 的 Entity 子类，并在编译时生成注册代码。
    /// </remarks>
    public interface IMemoryPackEntityGenerator
    {
        /// <summary>
        /// 初始化 MemoryPack 序列化器，触发所有 MemoryPackable 类型的静态构造函数。
        /// </summary>
        /// <remarks>
        /// 此方法会在程序集加载时自动调用（通过 ModuleInitializer 或 RuntimeInitializeOnLoadMethod），
        /// 确保所有 MemoryPack formatter 在 AOT 环境下正确注册。
        /// </remarks>
        void Initialize();

        /// <summary>
        /// 获取程序集中所有标记了 [MemoryPackable] 的 Entity 子类的 TypeHashCode 数组。
        /// </summary>
        /// <returns>
        /// 包含 Entity 类型哈希码的 <see cref="long"/> 数组。
        /// 每个哈希码对应 <see cref="EntityTypes"/> 数组中相同索引位置的类型。
        /// </returns>
        long[] EntityTypeHashCodes();

        /// <summary>
        /// 获取程序集中所有标记了 [MemoryPackable] 的 Entity 子类的 Type 数组。
        /// </summary>
        /// <returns>
        /// <see cref="Type"/> 数组，每个 Type 对应 Entity 的具体子类。
        /// 数组顺序与 <see cref="EntityTypeHashCodes"/> 数组保持一致。
        /// </returns>
        /// <remarks>
        /// 这些类型信息在编译时生成，框架会将所有程序集的类型信息合并，
        /// 用于构建 Int64FrozenDictionary 以支持高性能的多态反序列化。
        /// </remarks>
        Type[] EntityTypes();
    }
}
