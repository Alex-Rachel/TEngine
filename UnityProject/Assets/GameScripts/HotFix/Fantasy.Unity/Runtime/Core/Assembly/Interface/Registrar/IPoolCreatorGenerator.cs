using System;
using Fantasy.Entitas.Interface;
using Fantasy.Pool;

namespace Fantasy.Assembly
{
    /// <summary>
    /// 对象池创建器生成器接口。此接口由 Source Generator 自动生成的类实现，
    /// 用于在编译时注册实现了 <see cref="IPool"/> 接口的类型的对象池创建器。
    /// </summary>
    /// <remarks>
    /// 此接口支持 Native AOT 编译，通过消除基于反射的对象创建来提高性能。
    /// Roslyn Source Generator (PoolCreatorGenerator) 会自动扫描所有 IPool 实现，
    /// 并在编译时生成注册代码。
    /// </remarks>
    public interface IPoolCreatorGenerator
    {
        /// <summary>
        /// 获取所有已注册对象池类型的RuntimeTypeHandle数组。
        /// </summary>
        /// <returns>
        /// 包含实现了 <see cref="IPool"/> 接口的类型哈希码的 <see cref="long"/> 数组。
        /// 每个哈希码对应 <see cref="Generators"/> 数组中相同索引位置的工厂函数。
        /// </returns>
        RuntimeTypeHandle[] RuntimeTypeHandles();

        /// <summary>
        /// 获取用于创建对象池实例的工厂函数数组。
        /// </summary>
        /// <returns>
        /// <see cref="Func{IPool}"/> 委托数组，每个委托能够创建对应类型的实例。
        /// 数组顺序与 <see cref="RuntimeTypeHandles"/> 数组保持一致。
        /// </returns>
        /// <remarks>
        /// 这些工厂函数在编译时生成，不使用反射，因此兼容 IL2CPP 和 Native AOT 编译。
        /// </remarks>
        Func<IPool>[] Generators();
    }
}