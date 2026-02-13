using System;
using System.Collections.Generic;
using Fantasy.DataStructure.Dictionary;
using Fantasy.Entitas;
using Fantasy.Network;
using Fantasy.Network.Interface;
using Fantasy.Async;

namespace Fantasy.Assembly
{
    /// <summary>
    /// 消息处理器解析器接口
    /// 由 Source Generator 自动生成实现类，用于在程序集加载时注册网络消息处理器
    /// </summary>
    public interface IMessageHandlerResolver
    {
        /// <summary>
        /// 获取所有消息处理器的操作码数组
        /// </summary>
        uint[] MessageHandlerOpCodes();

        /// <summary>
        /// 获取所有消息处理器委托数组
        /// </summary>
        Func<Session, uint, object, FTask>[] MessageHandlers();
#if FANTASY_NET
        /// <summary>
        /// 获取所有可寻址消息处理器的操作码数组
        /// </summary>
        uint[] AddressMessageHandlerOpCodes();

        /// <summary>
        /// 获取所有可寻址消息处理器委托数组
        /// </summary>
        Func<Session, Entity, uint, object, FTask>[] AddressMessageHandler();
#endif
    }
}