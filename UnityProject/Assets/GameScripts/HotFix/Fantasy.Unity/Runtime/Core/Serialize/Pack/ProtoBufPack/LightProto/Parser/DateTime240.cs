using System;

namespace LightProto.Parser
{
    [ProtoContract]
    [ProtoSurrogateFor(typeof(DateTime))]
    public partial struct DateTime240ProtoParser
    {
        [ProtoMember(1)] internal long Seconds { get; set; }

        [ProtoMember(2)] internal int Nanos { get; set; }

        private static readonly DateTime UnixEpoch = new DateTime(
            1970,
            1,
            1,
            0,
            0,
            0,
            DateTimeKind.Utc
        );

        // Constants determined programmatically, but then hard-coded so they can be constant expressions.
        private const long BclSecondsAtUnixEpoch = 62135596800;
        internal const long UnixSecondsAtBclMaxValue = 253402300799;
        internal const long UnixSecondsAtBclMinValue = -BclSecondsAtUnixEpoch;

        /// <summary>
        /// The number of nanoseconds in a second.
        /// </summary>
        const int NanosecondsPerSecond = 1000000000;

        /// <summary>
        /// The number of nanoseconds in a BCL tick (as used by <see cref="TimeSpan"/> and <see cref="DateTime"/>).
        /// </summary>
        const int NanosecondsPerTick = 100;

        internal const int MaxNanos = NanosecondsPerSecond - 1;

        public static implicit operator DateTime(DateTime240ProtoParser protoParser)
        {
            if (!IsNormalized(protoParser.Seconds, protoParser.Nanos))
            {
                throw new InvalidOperationException(
                    @"Timestamp contains invalid values: Seconds={Seconds}; Nanos={Nanos}"
                );
            }

            return UnixEpoch
                .AddSeconds(protoParser.Seconds)
                .AddTicks(protoParser.Nanos / NanosecondsPerTick);
        }

        public static implicit operator DateTime240ProtoParser(DateTime dateTime)
        {
            // Do the arithmetic using DateTime.Ticks, which is always non-negative, making things simpler.
            long secondsSinceBclEpoch = dateTime.Ticks / TimeSpan.TicksPerSecond;
            int nanoseconds = (int)(dateTime.Ticks % TimeSpan.TicksPerSecond) * NanosecondsPerTick;
            return new DateTime240ProtoParser
            {
                Seconds = secondsSinceBclEpoch - BclSecondsAtUnixEpoch,
                Nanos = nanoseconds,
            };
        }

        private static bool IsNormalized(long seconds, int nanoseconds) =>
            nanoseconds >= 0
            && nanoseconds <= MaxNanos
            && seconds >= UnixSecondsAtBclMinValue
            && seconds <= UnixSecondsAtBclMaxValue;
    }
}