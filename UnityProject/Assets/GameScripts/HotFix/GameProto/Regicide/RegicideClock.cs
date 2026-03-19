using System;

namespace GameProto.Regicide
{
    public static class RegicideClock
    {
        public static long NowUnixMilliseconds()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }
}
