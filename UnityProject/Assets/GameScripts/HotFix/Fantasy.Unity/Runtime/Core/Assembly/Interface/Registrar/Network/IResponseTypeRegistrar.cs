using System;

namespace Fantasy.Assembly
{
    /// <summary>
    /// 网络协议响应类型注册器接口
    /// 用于根据 OpCode 解析对应的响应消息类型
    /// 此接口通常由 NetworkProtocol SourceGenerator 自动生成实现
    /// </summary>
    public interface IResponseTypeRegistrar
    {
        /// <summary>
        /// OpCodes
        /// </summary>
        /// <returns></returns>
        uint[] OpCodes();

        /// <summary>
        /// Types
        /// </summary>
        /// <returns></returns>
        Type[] Types();
    }
}