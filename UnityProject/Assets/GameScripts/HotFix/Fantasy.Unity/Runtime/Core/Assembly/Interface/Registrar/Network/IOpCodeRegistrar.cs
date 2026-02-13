using System;

namespace Fantasy.Assembly
{
    /// <summary>
    /// 网络协议 OpCode 解析器接口
    /// </summary>
    /// <remarks>
    /// 此接口由 SourceGenerator 自动生成的类实现。
    /// 每个包含网络协议的程序集都会生成自己的解析器实现。
    /// </remarks>
    public interface IOpCodeRegistrar
    {
        /// <summary>
        /// 获取所有已注册对象的TypeOpCode数组。
        /// </summary>
        /// <returns></returns>
        uint[] TypeOpCodes();

        /// <summary>
        /// 获取所有已注册对象的OpCodeType数组。
        /// </summary>
        /// <returns></returns>
        Type[] OpCodeTypes();
        
        /// <summary>
        /// 获取所有已注册对象的CustomRouteTypeOpCode数组。
        /// </summary>
        /// <returns></returns>
        uint[] CustomRouteTypeOpCodes();
        
        /// <summary>
        /// 获取所有已注册对象的CustomRouteType数组。
        /// </summary>
        /// <returns></returns>
        int[] CustomRouteTypes();
    }
}