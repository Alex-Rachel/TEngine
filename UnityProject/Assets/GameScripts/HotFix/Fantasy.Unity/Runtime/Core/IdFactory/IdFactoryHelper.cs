using System;
using System.Runtime.CompilerServices;
using Fantasy.Helper;
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

namespace Fantasy.IdFactory
{
    /// <summary>
    /// Id生成器帮助类
    /// </summary>
    public static class IdFactoryHelper
    {
        private static IdFactoryType _idFactoryType = IdFactoryType.Default;
        
        /// <summary>
        /// EntityId工具
        /// </summary>
        public static IIdFactoryTool EntityIdTool { get; private set; } = new WorldEntityIdFactoryTool();

        /// <summary>
        /// RuntimeId工具
        /// </summary>
        public static IIdFactoryTool RuntimeIdTool { get; private set; } = new WorldRuntimeIdFactoryTool();

        /// <summary>
        /// 初始化
        /// </summary>
        /// <param name="idFactoryType"></param>
        public static void Initialize(IdFactoryType idFactoryType)
        {
            _idFactoryType = idFactoryType;

            switch (_idFactoryType)
            {
                case IdFactoryType.Default:
                {
                    EntityIdTool = new EntityIdFactoryTool();
                    RuntimeIdTool = new RuntimeIdFactoryTool();
                    return;
                }
                case IdFactoryType.World:
                {
                    EntityIdTool = new WorldEntityIdFactoryTool();
                    RuntimeIdTool = new WorldRuntimeIdFactoryTool();
                    return;
                }
            }
        }

        /// <summary>
        /// 获得当前的IdFactoryType
        /// </summary>
        /// <returns></returns>
        public static IdFactoryType GetIdFactoryType()
        {
            return _idFactoryType;
        }

        internal static IEntityIdFactory EntityIdFactory(uint sceneId, byte worldId)
        {
            switch (_idFactoryType)
            {
                case IdFactoryType.Default:
                {
                    return new EntityIdFactory(sceneId);
                }
                case IdFactoryType.World:
                {
                    return new WorldEntityIdFactory(sceneId, worldId);
                }
                default:
                {
                    throw new NotSupportedException($"IdFactoryType {_idFactoryType} is not supported.");
                }
            }
        }

        internal static IRuntimeIdFactory RuntimeIdFactory(long epochNow, uint sceneId, byte worldId)
        {
            switch (_idFactoryType)
            {
                case IdFactoryType.Default:
                {
                    return new RuntimeIdFactory(sceneId);
                }
                case IdFactoryType.World:
                {
                    if (epochNow == 0)
                    {
                        epochNow = TimeHelper.Now;
                    }

                    return new WorldRuntimeIdFactory(epochNow, sceneId, worldId);
                }
                default:
                {
                    throw new NotSupportedException($"IdFactoryType {_idFactoryType} is not supported.");
                }
            }
        }

        internal static long EntityId(uint time, uint sceneId, byte wordId, uint sequence)
        {
            switch (_idFactoryType)
            {
                case IdFactoryType.Default:
                {
                    return new EntityIdStruct(time, sceneId, sequence);
                }
                case IdFactoryType.World:
                {
                    return new WorldEntityIdStruct(time, sceneId, wordId, sequence);
                }
                default:
                {
                    throw new NotSupportedException($"IdFactoryType {_idFactoryType} is not supported.");
                }
            }
        }

        internal static long RuntimeId(bool isPool, uint time, uint sceneId, byte wordId, uint sequence)
        {
            switch (_idFactoryType)
            {
                case IdFactoryType.Default:
                {
                    return new RuntimeIdStruct(isPool, time, sceneId, sequence);
                }
                case IdFactoryType.World:
                {
                    return new WorldRuntimeIdStruct(isPool, time, sceneId, wordId, sequence);
                }
                default:
                {
                    throw new NotSupportedException($"IdFactoryType {_idFactoryType} is not supported.");
                }
            }
        }
    }
}

