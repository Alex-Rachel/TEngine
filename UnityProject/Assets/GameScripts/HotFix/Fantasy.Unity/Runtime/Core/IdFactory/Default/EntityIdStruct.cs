using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fantasy.Helper;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace Fantasy.IdFactory
{
    /// <summary>
    /// 表示一个唯一实体的ID。
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct EntityIdStruct
    {
        // EntityId:39 + 16 + 18 =  64
        // +-------------------+-----------------------------+------------------------------------+
        // |  time(30) 最大34年 | SceneId(16) 最多65535个Scene | sequence(18) 每秒每个进程能生产262143个
        // +-------------------+-----------------------------+------------------------------------+
        public uint Time { get; set; }
        public uint SceneId { get; set; }
        public uint Sequence { get; set; }
        
        public const uint MaskSequence = 0x3FFFF;
        public const uint MaskSceneId = 0xFFFF;
        public const uint MaskTime = 0x3FFFFFFF;

        /// <summary>
        /// WorldEntityIdStruct（如果超过下面参数的设定该ID会失效）。
        /// </summary>
        /// <param name="time">time不能超过1073741823</param>
        /// <param name="sceneId">sceneId不能超过65535</param>
        /// <param name="sequence">sequence不能超过262143</param>
        public EntityIdStruct(uint time, uint sceneId, uint sequence)
        {
            // 因为都是在配置表里拿到参数、所以这个不做边界判定、能节省一点点性能。
            Time = time;
            SceneId = sceneId;
            Sequence = sequence;
        }

        public static implicit operator long(EntityIdStruct entityIdStruct)
        {
            ulong result = 0;
            result |= entityIdStruct.Sequence;
            result |= (ulong)entityIdStruct.SceneId << 18;
            result |= (ulong)entityIdStruct.Time << 34;
            return (long)result;
        }
        
        public static implicit operator EntityIdStruct(long entityId)
        {
            var result = (ulong) entityId;
            var entityIdStruct = new EntityIdStruct
            {
                Sequence = (uint)(result & MaskSequence)
            };
            result >>= 18;
            entityIdStruct.SceneId = (uint)(result & MaskSceneId);
            result >>= 16;
            entityIdStruct.Time = (uint)(result & MaskTime);
            return entityIdStruct;
        }
    }

    public sealed class EntityIdFactory : IEntityIdFactory
    {
        private readonly ushort _sceneId;
        
        private uint _lastTime;
        private uint _lastSequence;
        private static readonly long Epoch1970 = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks / 10000;
        private static readonly long EpochThisYear = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks / 10000 - Epoch1970;
    
        private EntityIdFactory() { }
    
        public EntityIdFactory(uint sceneId)
        {
            switch (sceneId)
            {
                case > 65535:
                {
                    throw new NotSupportedException($"sceneId:{sceneId} cannot be greater than 65535");
                }
                default:
                {
                    _sceneId = (ushort)sceneId;
                    break;
                }
            }
        }
        
        public long Create
        {
            get
            {
                var time = (uint)((TimeHelper.Now - EpochThisYear) / 1000);
                
                if (time > _lastTime)
                {
                    _lastTime = time;
                    _lastSequence = 0;
                }
                else if (++_lastSequence > EntityIdStruct.MaskSequence - 1)
                {
                    _lastTime++;
                    _lastSequence = 0;
                }

                return new EntityIdStruct(time, _sceneId, _lastSequence);
            }
        }
    }

    public sealed class EntityIdFactoryTool : IIdFactoryTool
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool GetIsPool(ref long entityId)
        {
            throw new NotImplementedException();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool GetIsPool(long runtimeId)
        {
            throw new NotImplementedException();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetTime(ref long entityId)
        {
            var result = (ulong)entityId >> 34;
            return (uint)(result & EntityIdStruct.MaskTime);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetTime(long entityId)
        {
            return GetTime(ref entityId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetSceneId(ref long entityId)
        {
            var result = (ulong)entityId >> 18;
            return (uint)(result & EntityIdStruct.MaskSceneId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetSceneId(long entityId)
        {
            return GetSceneId(ref entityId);
        }

        public byte GetWorldId(ref long entityId)
        {
            throw new NotImplementedException();
        }

        public byte GetWorldId(long entityId)
        {
            throw new NotImplementedException();
        }
    }
}