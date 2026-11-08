using System;
using System.Collections.Generic;
using System.Linq;

namespace Shiftline.Plugin
{
    /// <summary>
    /// Estimates how each driver's iRating would change if the race ended now. iRacing does not publish its formula; this is the
    /// community estimate from the "iRacing SOF iRating Calculator" spreadsheet shared in the SIMRacingApps project, as implemented
    /// in the irating-rs library by Tobias Bieniek (MIT/Apache-2.0). Every starter counts, positions are taken per class.
    /// </summary>
    internal static class IRatingEstimate
    {
        private static readonly double Factor = 1600 / Math.Log(2);

        public static void Apply(List<StandingsRow> rows)
        {
            foreach (IGrouping<int, StandingsRow> group in rows.GroupBy(r => r.ClassId))
            {
                List<StandingsRow> field = group.Where(r => r.IRating > 0 && r.ClassPosition > 0).ToList();
                int n = field.Count;
                if (n < 2)
                {
                    continue;
                }

                foreach (StandingsRow driver in field)
                {
                    double expected = -0.5;              // the driver against himself counts 0.5
                    foreach (StandingsRow other in field)
                    {
                        expected += Chance(driver.IRating, other.IRating);
                    }

                    double fudge = (n / 2.0 - driver.ClassPosition) / 100.0;
                    double change = (n - driver.ClassPosition - expected - fudge) * 200.0 / n;
                    driver.IRatingChange = (int)Math.Round(change);
                    driver.HasIRatingChange = true;
                }
            }
        }

        /// <summary>The chance that a driver rated a beats a driver rated b.</summary>
        private static double Chance(double a, double b)
        {
            double ea = Math.Exp(-a / Factor);
            double eb = Math.Exp(-b / Factor);
            return (1 - ea) * eb / ((1 - eb) * ea + (1 - ea) * eb);
        }
    }
}
