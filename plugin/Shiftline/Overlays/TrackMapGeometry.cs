using System;

namespace Shiftline.Plugin
{
    /// <summary>
    /// The maths of the Track Map, without any drawing: where a car sits on the line. All distances along the lap are fractions of
    /// a lap (LapDistPct, 0..1).
    /// </summary>
    internal static class TrackMapGeometry
    {
        /// <summary>
        /// The distance from one point of the lap to another, as a fraction of a lap in the range (-0.5, 0.5]:
        /// positive when the target is in front of the origin on the track, negative when it is behind.
        /// </summary>
        public static double Signed(double target, double origin)
        {
            double d = (target - origin) % 1.0;
            if (d > 0.5)
            {
                d -= 1.0;
            }
            else if (d <= -0.5)
            {
                d += 1.0;
            }

            return d;
        }

        /// <summary>
        /// Where a point of the lap lies on the line, from 0 (start of the line) to 1 (end of the line). Whole lap: the line is the
        /// lap, start/finish at the start. Centred: the line shows the part of the lap within "range" (a fraction of a lap) on both
        /// sides of the player, who sits in the middle; the result is false for a point outside of it.
        /// </summary>
        public static bool LineFraction(double pct, double playerPct, bool centred, double range, out double fraction)
        {
            if (!centred || range <= 0)
            {
                fraction = Math.Max(0, Math.Min(1, pct));
                return true;
            }

            double d = Signed(pct, playerPct);
            fraction = 0.5 + d / (2 * range);
            return Math.Abs(d) <= range + 1e-9;
        }
    }
}
