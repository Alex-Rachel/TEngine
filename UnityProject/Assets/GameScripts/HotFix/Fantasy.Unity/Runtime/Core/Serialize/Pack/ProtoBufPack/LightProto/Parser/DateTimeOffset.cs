using System;

namespace LightProto.Parser
{
    [ProtoContract]
    [ProtoSurrogateFor(typeof(DateTimeOffset))]
    public partial struct DateTimeOffsetProtoParser
    {
        [ProtoMember(1)]
        internal long DateTimeTicks { get; set; }

        [ProtoMember(2)]
        internal long OffsetTicks { get; set; }

        public static implicit operator DateTimeOffset(DateTimeOffsetProtoParser proxy)
        {
            return new DateTimeOffset(proxy.DateTimeTicks, TimeSpan.Zero).ToOffset(
                new TimeSpan(proxy.OffsetTicks)
            );
        }

        public static implicit operator DateTimeOffsetProtoParser(DateTimeOffset dt)
        {
            return new DateTimeOffsetProtoParser
            {
                DateTimeTicks = dt.UtcTicks,
                OffsetTicks = dt.Offset.Ticks,
            };
        }
    }
}
