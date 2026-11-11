using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Media;

namespace Shiftline.Plugin
{
    /// <summary>The relatives: the cars right around you on the track, in the layout of the standings.</summary>
    internal sealed partial class StandingsOverlay
    {
        private static readonly Brush LappingBrush = Frozen(0xFF, 0xF0, 0x40, 0x3E);     // a lap up on you
        private static readonly Brush LappedBrush = Frozen(0xFF, 0x7F, 0xB2, 0xE0);      // a lap down, you lapped it

        /// <summary>
        /// The rows of the relatives: the cars in front (the farthest on top), you, the cars behind. A missing car leaves a
        /// blank row, so you always stay in the same row and the window does not change its size.
        /// </summary>
        private List<Entry> BuildRelative(StandingsRow[] all, Context context)
        {
            List<Entry> shown = new List<Entry>();
            StandingsRow me = context.Player;
            if (me == null || me.LapDistPct < 0)
            {
                return shown;
            }

            int aheadCount = Math.Max(1, _options.GetInt("Rel.Ahead"));
            int behindCount = Math.Max(1, _options.GetInt("Rel.Behind"));
            int decimals = _options.GetInt("Rel.Dec");
            double lapTime = me.BestLap > 0 ? me.BestLap : me.LastLap > 0 ? me.LastLap : 90;
            bool multiClass = all.Select(r => r.ClassId).Distinct().Count() > 1;

            List<KeyValuePair<double, Entry>> front = new List<KeyValuePair<double, Entry>>();
            List<KeyValuePair<double, Entry>> back = new List<KeyValuePair<double, Entry>>();
            foreach (StandingsRow car in all)
            {
                if (car.IsPlayer || car.LapDistPct < 0 || car.TrackSurface < 0)
                {
                    continue;
                }

                double distance = car.LapDistPct - me.LapDistPct;
                distance -= Math.Round(distance);                 // -0.5 .. 0.5: the shorter way around the lap, positive = in front
                double seconds = Math.Abs(distance) * lapTime;
                if (car.EstTime > 0 && me.EstTime > 0)
                {
                    // iRacing's own estimate of the time to each spot on the lap; the lap time closes the gap at the start line
                    double estimate = car.EstTime - me.EstTime;
                    if (distance >= 0 && estimate < 0)
                    {
                        estimate += lapTime;
                    }
                    else if (distance < 0 && estimate > 0)
                    {
                        estimate -= lapTime;
                    }

                    if (Math.Abs(estimate) <= lapTime)
                    {
                        seconds = Math.Abs(estimate);
                    }
                }

                int lapRelation = 0;
                if (context.Race)
                {
                    lapRelation = (int)Math.Round((car.Progress - me.Progress) - distance);      // +n: n laps up on you, -n: n laps down
                }

                Entry entry = new Entry
                {
                    Row = car,
                    Position = PositionOf(car, multiClass),
                    GapText = (distance > 0 ? "-" : "+") + Truncate(seconds, decimals).ToString(Format(decimals), CultureInfo.InvariantCulture),
                    GapBrush = lapRelation > 0 ? LappingBrush : lapRelation < 0 ? LappedBrush : TextBrush
                };
                (distance > 0 ? front : back).Add(new KeyValuePair<double, Entry>(Math.Abs(distance), entry));
            }

            List<Entry> ahead = front.OrderBy(p => p.Key).Take(aheadCount).Select(p => p.Value).Reverse().ToList();
            List<Entry> behind = back.OrderBy(p => p.Key).Take(behindCount).Select(p => p.Value).ToList();
            for (int i = ahead.Count; i < aheadCount; i++)
            {
                shown.Add(null);
            }

            shown.AddRange(ahead);
            shown.Add(new Entry { Row = me, Position = PositionOf(me, multiClass) });
            shown.AddRange(behind);
            return shown;
        }

        private static int PositionOf(StandingsRow row, bool multiClass)
        {
            return multiClass && row.ClassPosition > 0 ? row.ClassPosition : row.Position;
        }
    }
}
