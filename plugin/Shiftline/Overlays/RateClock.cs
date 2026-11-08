using System.Diagnostics;

namespace Shiftline.Plugin
{
    /// <summary>
    /// Lets a consumer run at its own rate (for example 10 times per second) inside a faster update loop.
    /// Each consumer owns one clock, so a slow widget never pays for a fast one.
    /// </summary>
    internal sealed class RateClock
    {
        private long _interval;
        private long _next;

        public RateClock(double hertz)
        {
            _interval = (long)(Stopwatch.Frequency / hertz);
        }

        /// <summary>Changes the rate; the next interval already uses it.</summary>
        public void SetRate(double hertz)
        {
            _interval = (long)(Stopwatch.Frequency / hertz);
        }

        /// <summary>True when the interval has passed since the last time this returned true.</summary>
        public bool Due()
        {
            long now = Stopwatch.GetTimestamp();
            if (now < _next)
            {
                return false;
            }

            _next = now + _interval;
            return true;
        }

        /// <summary>The next call to Due returns true.</summary>
        public void Reset()
        {
            _next = 0;
        }
    }
}
