using System;

namespace LightProto.Parser
{
    //
//
// message DateTime {
// sint64 value = 1; // the offset (in units of the selected scale) from 1970/01/01
// TimeSpanScale scale = 2; // the scale of the timespan [default = DAYS]
// DateTimeKind kind = 3; // the kind of date/time being represented [default = UNSPECIFIED]
// enum TimeSpanScale {
//     DAYS = 0;
//     HOURS = 1;
//     MINUTES = 2;
//     SECONDS = 3;
//     MILLISECONDS = 4;
//     TICKS = 5;
//
//     MINMAX = 15; // dubious
// }
// enum DateTimeKind
// {
//     // The time represented is not specified as either local time or Coordinated Universal Time (UTC).
//     UNSPECIFIED = 0;
//     // The time represented is UTC.
//     UTC = 1;
//     // The time represented is local time.
//     LOCAL = 2;
// }
// }

    [ProtoContract]
    [ProtoSurrogateFor(typeof(DateTime))]
    public partial struct DateTimeProtoParser
    {
        [ProtoMember(1, DataFormat = DataFormat.ZigZag)]
        internal long Ticks { get; set; }

        [ProtoMember(2)] internal TimeSpanScale Scale { get; set; }

        [ProtoMember(3)] internal DateTimeKind Kind { get; set; }

        public static implicit operator DateTime(DateTimeProtoParser proxy)
        {
            long ticks;
            switch (proxy.Scale)
            {
                case TimeSpanScale.Days:
                    ticks = proxy.Ticks * TimeSpan.TicksPerDay;
                    break;
                case TimeSpanScale.Hours:
                    ticks = proxy.Ticks * TimeSpan.TicksPerHour;
                    break;
                case TimeSpanScale.Minutes:
                    ticks = proxy.Ticks * TimeSpan.TicksPerMinute;
                    break;
                case TimeSpanScale.Seconds:
                    ticks = proxy.Ticks * TimeSpan.TicksPerSecond;
                    break;
                case TimeSpanScale.Milliseconds:
                    ticks = proxy.Ticks * TimeSpan.TicksPerMillisecond;
                    break;
                case TimeSpanScale.Ticks:
                    ticks = proxy.Ticks;
                    break;
                case TimeSpanScale.Minmax:
                    if (proxy.Ticks == -1)
                        return DateTime.MinValue;
                    else if (proxy.Ticks == 1)
                        return DateTime.MaxValue;
                    else
                        throw new ArgumentOutOfRangeException(
                            nameof(proxy.Ticks),
                            $"Invalid ticks for MINMAX scale: {proxy.Ticks}"
                        );
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(proxy.Scale),
                        $"Unknown scale: {proxy.Scale}"
                    );
            }

            return new DateTime(ticks: ticks + EpochOriginsTicks[(int)proxy.Kind], kind: proxy.Kind);
        }

        internal static readonly long[] EpochOriginsTicks = new long[]
        {
            new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified).Ticks,
            new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc).Ticks,
            new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Local).Ticks,
        };

        public static implicit operator DateTimeProtoParser(DateTime dt)
        {
            if (dt == DateTime.MinValue)
            {
                return new DateTimeProtoParser
                {
                    Ticks = -1,
                    Scale = TimeSpanScale.Minmax,
                    Kind = DateTimeKind.Unspecified,
                };
            }

            if (dt == DateTime.MaxValue)
            {
                return new DateTimeProtoParser
                {
                    Ticks = 1,
                    Scale = TimeSpanScale.Minmax,
                    Kind = DateTimeKind.Unspecified,
                };
            }

            var ticks = dt.Ticks - EpochOriginsTicks[(int)dt.Kind];
            var left = Math.DivRem(ticks, TimeSpan.TicksPerDay, out var reminder);
            if (reminder == 0)
            {
                return new()
                {
                    Ticks = left,
                    Scale = TimeSpanScale.Days,
                    Kind = dt.Kind,
                };
            }

            left = Math.DivRem(ticks, TimeSpan.TicksPerHour, out reminder);
            if (reminder == 0)
            {
                return new()
                {
                    Ticks = left,
                    Scale = TimeSpanScale.Hours,
                    Kind = dt.Kind,
                };
            }

            left = Math.DivRem(ticks, TimeSpan.TicksPerMinute, out reminder);
            if (reminder == 0)
            {
                return new()
                {
                    Ticks = left,
                    Scale = TimeSpanScale.Minutes,
                    Kind = dt.Kind,
                };
            }

            left = Math.DivRem(ticks, TimeSpan.TicksPerSecond, out reminder);
            if (reminder == 0)
            {
                return new()
                {
                    Ticks = left,
                    Scale = TimeSpanScale.Seconds,
                    Kind = dt.Kind,
                };
            }

            left = Math.DivRem(ticks, TimeSpan.TicksPerMillisecond, out reminder);
            if (reminder == 0)
            {
                return new()
                {
                    Ticks = left,
                    Scale = TimeSpanScale.Milliseconds,
                    Kind = dt.Kind,
                };
            }

            return new()
            {
                Ticks = ticks,
                Scale = TimeSpanScale.Ticks,
                Kind = dt.Kind,
            };
        }
    }

    internal enum TimeSpanScale
    {
        Days = 0,
        Hours = 1,
        Minutes = 2,
        Seconds = 3,
        Milliseconds = 4,
        Ticks = 5,
        Minmax = 15,
    }
}
