using System;

namespace Shiftline.Plugin
{
    /// <summary>
    /// Invented cars spread around the lap for the Track Map preview and for edit mode without a session. Built on the standings
    /// sample (two classes, no real drivers). One car a lap down is just in front of the player and the leader a lap up just
    /// behind; two cars are in the pit near the start/finish line and one is not in the world.
    /// </summary>
    internal static class TrackMapSample
    {
        private const double LapSeconds = 90;
        private const int NotInWorld = 19;
        private static readonly int[] InPit = { 5, 13 };

        /// <summary>The cars after "seconds" of driving (0 = standing as they are made); the order of the cars does not change.</summary>
        public static StandingsSnapshot Create(double seconds)
        {
            StandingsSnapshot baseSample = StandingsSample.OnTrack(StandingsSample.Create(true));
            StandingsRow[] rows = new StandingsRow[baseSample.Rows.Length];
            for (int i = 0; i < rows.Length; i++)
            {
                StandingsRow row = baseSample.Rows[i].Copy();
                if (i == 3)
                {
                    row.Number = "112";
                }

                if (i == InPit[0])
                {
                    row.LapDistPct = 0.012;
                }
                else if (i == InPit[1])
                {
                    row.LapDistPct = 0.975;
                }
                else if (i == NotInWorld)
                {
                    row.TrackSurface = -1;
                }
                else if (seconds > 0)
                {
                    // every car a little faster or slower than the others; the lap count follows when a car crosses the line
                    double speed = (1 + ((i * 5) % 7 - 3) * 0.004) / LapSeconds;
                    double progress = row.LapsCompleted + row.LapDistPct + seconds * speed;
                    double laps = Math.Floor(progress);
                    row.LapsCompleted = (int)laps;
                    row.LapDistPct = progress - laps;
                }

                rows[i] = row;
            }

            StandingsSnapshot sample = baseSample.As(baseSample.Features, baseSample.Game);
            sample.Rows = rows;
            return sample;
        }
    }
}
