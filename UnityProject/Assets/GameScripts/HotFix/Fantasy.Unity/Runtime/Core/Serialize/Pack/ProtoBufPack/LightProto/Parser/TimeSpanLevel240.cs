using System;

namespace LightProto.Parser
{
    [ProtoContract]
    [ProtoSurrogateFor(typeof(TimeSpan))]
    public sealed partial class TimeSpan240ProtoParser
    {
        [ProtoMember(1)]
        internal long Seconds { get; set; }

        [ProtoMember(2)]
        internal int Nanos { get; set; }

        /// <summary>
        /// The number of nanoseconds in a second.
        /// </summary>
        const int NanosecondsPerSecond = 1000000000;

        /// <summary>
        /// The number of nanoseconds in a BCL tick (as used by <see cref="TimeSpan"/> and <see cref="TimeSpan"/>).
        /// </summary>
        const int NanosecondsPerTick = 100;

        /// <summary>
        /// The maximum permitted number of seconds.
        /// </summary>
        const long MaxSeconds = 315576000000L;

        /// <summary>
        /// The minimum permitted number of seconds.
        /// </summary>
        const long MinSeconds = -315576000000L;

        internal const int MaxNanoseconds = NanosecondsPerSecond - 1;
        internal const int MinNanoseconds = -NanosecondsPerSecond + 1;

        public static implicit operator TimeSpan(TimeSpan240ProtoParser protoParser)
        {
            checked
            {
                long ticks =
                    protoParser.Seconds * TimeSpan.TicksPerSecond
                    + protoParser.Nanos / NanosecondsPerTick;
                return TimeSpan.FromTicks(ticks);
            }
        }

        public static implicit operator TimeSpan240ProtoParser(TimeSpan timeSpan)
        {
            checked
            {
                long ticks = timeSpan.Ticks;
                long seconds = ticks / TimeSpan.TicksPerSecond;
                int nanos = (int)(ticks % TimeSpan.TicksPerSecond) * NanosecondsPerTick;
                return new TimeSpan240ProtoParser { Seconds = seconds, Nanos = nanos };
            }
        }
    }
}
