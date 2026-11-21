using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Media;

namespace Shiftline.Plugin
{
    /// <summary>
    /// The rules of the Battle Bar, without any drawing: which cars it shows (Field, ChooseCards, ChooseTicker), the gaps,
    /// the trend, laps up or down, the frames, the side boxes and the sizes. BattleBarHtml turns them into what the page
    /// (web/battlebar.js) draws.
    /// </summary>
    internal static partial class BattleBarModel
    {
        internal const double OneLineHeight = 32;

        internal const double TwoLineHeight = 46;

        internal const double CardGap = 6;

        internal const double LeaderGap = 16;

        internal const double SideWidth = 66;

        internal const double LineHeight = 20;

        internal const double PlayerZoom = 1.18;

        internal static readonly Brush TextBrush = StandingsModel.Frozen(0xFF, 0xE8, 0xEC, 0xF1);

        internal static readonly Brush MutedBrush = StandingsModel.Frozen(0xFF, 0x8E, 0x98, 0xA6);

        internal static readonly Brush WhiteFill = StandingsModel.Frozen(0xFF, 0xF9, 0xF9, 0xF9);

        internal static readonly Brush SubtleFill = StandingsModel.Frozen(0x1E, 0xFF, 0xFF, 0xFF);

        internal static readonly Brush AccentBrush = StandingsModel.Frozen(0xFF, 0x3F, 0xA9, 0xF5);

        internal static readonly Brush BattleBrush = StandingsModel.Frozen(0xFF, 0xFF, 0x8A, 0x1F);

        internal static readonly Brush GreenBrush = StandingsModel.Frozen(0xFF, 0x5E, 0xE0, 0x7A);

        internal static readonly Brush LossBrush = StandingsModel.Frozen(0xFF, 0xF0, 0x5A, 0x4A);

        internal static readonly Brush PurpleBrush = StandingsModel.Frozen(0xFF, 0xC0, 0x7C, 0xFF);

        // A card, laid out on a grid so everything lines up:
        //   [position][class bar] | name ............ make   gap |
        //                         | iRating tyre pit      gained |
        // The name takes the room that is left, make and gap stand right-aligned at the end of the first line. On the second
        // line iRating, tyre and pit start under the name (in the order chosen on the page) and the places gained stand
        // right-aligned under the gap. The second line only exists when one of its items is on.
        internal const double Frame = 1.5;

        internal const double Space = 6;

        /// <summary>
        /// The size the bar is drawn at for a setting of 100 %. The setting stays a plain percentage, but the layout was drawn a
        /// little small, so 100 % is the size that looked right at 105 % before.
        /// </summary>
        internal const double BaseScale = 1.05;

        internal static readonly string[] LeftItems = { "Pos", "Class" };

        internal static readonly string[] TopItems = { "Flag", "Name", "Make", "Gap" };

        internal const double MakeWidth = 30;

        internal const double FlagWidth = 16;

        internal const double FlagGap = 5;

        /// <summary>Whether the bar should show in this session at all ("Show in": always, races, races and qualifying).</summary>
        public static bool ShowsIn(BattleBarOptions options, StandingsSnapshot snapshot)
        {
            string type = snapshot.SessionType ?? string.Empty;
            switch (options.Get("ShowIn"))
            {
                case "Race":
                    return snapshot.IsRace || snapshot.IsSample;
                case "RaceQuali":
                    return snapshot.IsRace || snapshot.IsSample || type.IndexOf("Qual", StringComparison.OrdinalIgnoreCase) >= 0;
                default:
                    return true;
            }
        }

        private static string SideLabel(string kind)
        {
            switch (kind)
            {
                case "Laps":
                    return "LAP";
                case "Position":
                    return "POS";
                case "Delta":
                    return "DELTA";
                case "Last":
                    return "LAST";
                case "Best":
                    return "BEST";
                case "Temp":
                    return "TRACK";
                case "Clock":
                    return "TIME";
                default:
                    return string.Empty;
            }
        }

        /// <summary>The cars of the cards: optionally the leader, then the cars in front of the player, the player, the cars behind.</summary>
        internal static List<StandingsRow> ChooseCards(BattleBarOptions options, Field field)
        {
            int ahead = Math.Max(1, Math.Min(4, options.GetInt("Ahead")));
            int behind = Math.Max(1, Math.Min(4, options.GetInt("Behind")));
            bool leader = options.GetBool("Leader");
            List<StandingsRow> shown = new List<StandingsRow>();
            List<StandingsRow> order = field.Order;
            int p = field.PlayerIndex;
            if (leader)
            {
                // the leader slot stays empty when the leader is among the cars right in front anyway
                StandingsRow first = order.Count > 0 && !field.ByTrack ? order[0] : null;
                shown.Add(first != null && p - ahead > 0 ? first : null);
            }

            for (int k = ahead; k >= 1; k--)
            {
                shown.Add(At(order, p, -k));
            }

            shown.Add(p >= 0 ? order[p] : null);
            for (int k = 1; k <= behind; k++)
            {
                shown.Add(At(order, p, k));
            }

            return shown;
        }

        /// <summary>The car k places from the player (negative: in front), or null past the ends of the order.</summary>
        private static StandingsRow At(List<StandingsRow> order, int player, int k)
        {
            if (player < 0)
            {
                return null;
            }

            int index = player + k;
            return index >= 0 && index < order.Count ? order[index] : null;
        }

        internal static List<StandingsRow> ChooseTicker(Field field, int count)
        {
            List<StandingsRow> order = field.Order;
            int start = 0;
            if (field.PlayerIndex >= 0 && order.Count > count)
            {
                start = Math.Max(0, Math.Min(order.Count - count, field.PlayerIndex - count / 2));
            }

            return order.Skip(start).Take(count).ToList();
        }

        /// <summary>
        /// Which frame a card gets: "Player" around the player's card, "Battle" when a car is close to the player (same lap,
        /// under the battle limit, races only), "Class" in the class colour when all classes show, otherwise "".
        /// </summary>
        internal static string FrameKind(BattleBarOptions options, StandingsRow row, Field field, double gap, bool sameLap)
        {
            double limit = options.GetDouble("Battle");
            if (row.IsPlayer)
            {
                return "Player";
            }

            if (limit > 0 && sameLap && Math.Abs(gap) < limit && field.Race)
            {
                return "Battle";
            }

            return options.Get("Classes") == "All" && field.MultiClass ? "Class" : string.Empty;
        }

        internal static readonly Brush GreenFrame = StandingsModel.Frozen(0xFF, 0x3D, 0xDC, 0x84);

        internal static readonly Brush WhiteFrame = StandingsModel.Frozen(0xFF, 0xF9, 0xF9, 0xF9);

        internal static readonly Brush LappedBrush = StandingsModel.Frozen(0xFF, 0x7F, 0xB2, 0xE0);

        internal static readonly Brush LappingBrush = StandingsModel.Frozen(0xFF, 0xF0, 0x40, 0x3E);

        /// <summary>
        /// Sorted by the track, cars of another lap sit right next to the player. Like iRacing's relative: a car a lap (or more)
        /// down gets a muted blue name and "-1L", a car a lap up a red name and "+1L".
        /// </summary>
        internal static int LapsApart(StandingsRow row, Field field)
        {
            StandingsRow player = field.Player;
            if (field.ByTrack && field.Race && player != null && !row.IsPlayer && row.LapDistPct >= 0 && player.LapDistPct >= 0)
            {
                return (int)Math.Round(row.Progress - player.Progress);
            }

            return 0;
        }

        internal static readonly Brush BattleRed = StandingsModel.Frozen(0xFF, 0xF0, 0x3E, 0x3E);

        internal static readonly Brush BattleYellow = StandingsModel.Frozen(0xFF, 0xFF, 0xD2, 0x3F);

        internal static Brush BattleColour(string name)
        {
            switch (name)
            {
                case "Red":
                    return BattleRed;
                case "Yellow":
                    return BattleYellow;
                default:
                    return BattleBrush;
            }
        }

        internal static string GapText(BattleBarOptions options, StandingsRow row, Field field, out double gap, out bool sameLap)
        {
            gap = 0;
            sameLap = false;
            StandingsRow player = field.Player;
            if (row.IsPlayer)
            {
                // the player's own card: the gap to the leader in races, the best lap otherwise
                if (!field.Race)
                {
                    return StandingsModel.Lap(row.BestLap, 3);
                }

                StandingsRow first = field.Order.Count > 0 ? field.Order[0] : null;
                if (first == null || ReferenceEquals(first, row) || field.ByTrack)
                {
                    return string.Empty;
                }

                return Difference(row, first, field, out gap, out sameLap, false);
            }

            StandingsRow reference = player;
            bool signed = true;
            if (options.Get("GapTo") == "Ahead")
            {
                int index = field.Order.IndexOf(row);
                reference = index > 0 ? field.Order[index - 1] : null;
                signed = false;
                if (reference == null)
                {
                    return string.Empty;
                }
            }

            if (reference == null)
            {
                return string.Empty;
            }

            string text = Difference(row, reference, field, out gap, out sameLap, signed);
            if (!ReferenceEquals(reference, player) && player != null)
            {
                // the battle frame always looks at the distance to the player
                double toPlayer;
                bool lap;
                Difference(row, player, field, out toPlayer, out lap, true);
                gap = toPlayer;
                sameLap = lap;
            }

            return text;
        }

        /// <summary>Time of row behind reference (positive: row is behind). Laps when they are a lap or more apart.</summary>
        private static string Difference(StandingsRow row, StandingsRow reference, Field field, out double gap, out bool sameLap, bool signed)
        {
            gap = 0;
            sameLap = false;
            if (field.ByTrack)
            {
                double rel = row.LapDistPct - reference.LapDistPct;
                if (row.LapDistPct < 0 || reference.LapDistPct < 0)
                {
                    return "-";
                }

                rel -= Math.Round(rel);                   // -0.5 .. 0.5, the shorter way around
                gap = -rel * field.LapTime;               // in front on the track: negative
                sameLap = true;
                return Signed(gap, signed);
            }

            if (field.Race)
            {
                double laps = reference.Progress - row.Progress;
                if (Math.Abs(laps) >= 1)
                {
                    int whole = (int)Math.Truncate(laps);
                    return (whole > 0 ? "+" : "-") + Math.Abs(whole).ToString(CultureInfo.InvariantCulture) + "L";
                }

                if (row.F2Time < 0 || reference.F2Time < 0 || (row.F2Time == 0 && row.Position != 1) || (reference.F2Time == 0 && reference.Position != 1))
                {
                    return "-";
                }

                gap = row.F2Time - reference.F2Time;
                sameLap = true;
                return Signed(gap, signed);
            }

            if (row.BestLap <= 0 || reference.BestLap <= 0)
            {
                return "-";
            }

            gap = row.BestLap - reference.BestLap;
            return Signed(gap, signed);
        }

        private static string Signed(double gap, bool signed)
        {
            string value = Math.Abs(gap).ToString(Math.Abs(gap) < 100 ? "0.0" : "0", CultureInfo.InvariantCulture);
            if (!signed)
            {
                return value;
            }

            return (gap > 0.05 ? "+" : gap < -0.05 ? "-" : string.Empty) + value;
        }

        /// <summary>
        /// 1 (green, up): the player was faster than this car over the last laps (closing in, or pulling away); -1 (red,
        /// down): slower; 0: no trend (off, the player's own card, too few laps).
        /// </summary>
        internal static int TrendOf(BattleBarOptions options, StandingsRow row, Field field)
        {
            int laps = Math.Max(1, options.GetInt("Trend.Laps"));
            StandingsRow player = field.Player;
            if (options.GetBool("Trend") && !row.IsPlayer && player != null && row.RecentLaps != null && player.RecentLaps != null
                && row.RecentLaps.Length >= laps && player.RecentLaps.Length >= laps)
            {
                double sum = 0;
                for (int k = 0; k < laps; k++)
                {
                    sum += row.RecentLaps[k] - player.RecentLaps[k];   // positive: the other car was slower
                }

                return sum > 0.05 ? 1 : sum < -0.05 ? -1 : 0;
            }

            return 0;
        }

        internal static double FadeSecondsOf(BattleBarOptions options)
        {
            switch (options.Get("Anim.Speed"))
            {
                case "Fast":
                    return 0.5;
                case "Slow":
                    return 1.3;
                default:
                    return 0.85;
            }
        }

        internal static double AnimationSecondsOf(BattleBarOptions options, bool replay)
        {
            if (options.Get("Anim.Style") == "Off" || (replay && !options.GetBool("Anim.Replay")))
            {
                return 0;
            }

            switch (options.Get("Anim.Speed"))
            {
                case "Fast":
                    return 0.25;
                case "Slow":
                    return 0.9;
                default:
                    return 0.5;
            }
        }

        private static string LapsText(StandingsSnapshot snapshot, Field field)
        {
            if (snapshot.LapsTotal > 0)
            {
                StandingsRow first = field.Order.Count > 0 && !field.ByTrack ? field.Order[0] : field.Player;
                int lap = first == null ? 0 : Math.Max(0, first.LapsCompleted) + 1;
                return "Lap " + Math.Min(lap, snapshot.LapsTotal) + "/" + snapshot.LapsTotal;
            }

            if (snapshot.TimeRemaining > 0 && snapshot.TimeRemaining < 360000)
            {
                return TimeSpan.FromSeconds(snapshot.TimeRemaining).ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture);
            }

            return string.Empty;
        }

        private static string TempText(StandingsSnapshot snapshot)
        {
            double temp;
            string text = (snapshot.TrackTemp ?? string.Empty).Split(' ')[0];
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out temp)
                ? temp.ToString("0", CultureInfo.InvariantCulture) + "°C"
                : string.Empty;
        }

        /// <summary>The value of a side box and its colour.</summary>
        internal static string SideValue(string kind, StandingsSnapshot snapshot, Field field, out Brush brush)
        {
            StandingsRow player = field.Player;
            string value = "-";
            brush = TextBrush;
            switch (kind)
            {
                case "Laps":
                    if (snapshot.LapsTotal > 0 || snapshot.TimeRemaining <= 0)
                    {
                        int lap = player == null ? 0 : Math.Max(0, player.LapsCompleted) + 1;
                        value = lap + (snapshot.LapsTotal > 0 ? "/" + snapshot.LapsTotal : string.Empty);
                    }
                    else
                    {
                        value = LapsText(snapshot, field);
                    }

                    break;
                case "Position":
                    int position = player == null ? 0 : field.PositionOf(player);
                    value = position > 0 ? position + "/" + field.Order.Count : "-";
                    break;
                case "Delta":
                    if (snapshot.HasDelta)
                    {
                        double delta = snapshot.DeltaToBest;
                        value = (delta > 0 ? "+" : delta < 0 ? "-" : string.Empty) + Math.Abs(delta).ToString("0.00", CultureInfo.InvariantCulture);
                        brush = delta < -0.005 ? GreenBrush : delta > 0.005 ? LossBrush : TextBrush;
                    }

                    break;
                case "Last":
                    value = player == null ? "-" : StandingsModel.Lap(player.LastLap, 3);
                    break;
                case "Best":
                    value = player == null ? "-" : StandingsModel.Lap(player.BestLap, 3);
                    brush = player != null && player.BestLap > 0 && Math.Abs(player.BestLap - field.SessionBest) < 0.0005 ? PurpleBrush : GreenBrush;
                    break;
                case "Temp":
                    value = TempText(snapshot);
                    break;
                case "Clock":
                    value = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);
                    break;
            }

            return value;
        }

        internal static string NameOf(BattleBarOptions options, StandingsRow row)
        {
            string format = options.Get("Name.Format");
            return format == "Code" ? ShortName(row.Name) : StandingsModel.FormatName(row.Name, format);
        }

        /// <summary>Three letters of the last name in capitals, as TV graphics show drivers (ROG).</summary>
        private static string ShortName(string name)
        {
            string[] parts = (name ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string last = parts.Length > 0 ? parts[parts.Length - 1] : string.Empty;
            string letters = new string(last.Where(char.IsLetter).ToArray());
            return (letters.Length >= 3 ? letters.Substring(0, 3) : letters).ToUpperInvariant();
        }

        /// <summary>The cars in the order the bar shows them, the player, and what the gaps need.</summary>
        internal sealed class Field
        {
            public Field(StandingsSnapshot snapshot, BattleBarOptions options)
            {
                StandingsRow[] all = snapshot.Rows ?? new StandingsRow[0];
                Race = snapshot.IsRace;
                Player = all.FirstOrDefault(r => r.IsPlayer);
                SessionBest = double.MaxValue;
                foreach (StandingsRow row in all)
                {
                    if (row.BestLap > 0 && row.BestLap < SessionBest)
                    {
                        SessionBest = row.BestLap;
                    }
                }

                MultiClass = all.Select(r => r.ClassId).Distinct().Count() > 1;
                OwnClass = options.Get("Classes") != "All" && MultiClass && Player != null;
                IEnumerable<StandingsRow> cars = OwnClass ? all.Where(r => r.ClassId == Player.ClassId) : all;
                ByTrack = options.Get("Sort") == "Track" && Player != null && Player.LapDistPct >= 0;
                LapTime = Player != null && Player.BestLap > 0 ? Player.BestLap : Player != null && Player.LastLap > 0 ? Player.LastLap : 90;
                if (ByTrack)
                {
                    // On the track: each car's distance to the player the shorter way around the lap (-0.5 .. 0.5, positive in
                    // front). The farthest car in front comes first, then the nearer ones, the player, and the cars behind.
                    double start = Player.LapDistPct;
                    bool hideLapped = Race && options.Get("Lapped") == "Hide";
                    StandingsRow me = Player;
                    Order = cars.Where(r => r.LapDistPct >= 0)
                        .Where(r => !hideLapped || r.IsPlayer || Math.Round(r.Progress - me.Progress) == 0)
                        .OrderByDescending(r => r.IsPlayer ? 0 : Relative(r.LapDistPct - start))
                        .ToList();
                }
                else
                {
                    Order = cars.OrderBy(r => PositionKey(r)).ThenByDescending(r => r.Progress).ToList();
                }

                PlayerIndex = Player == null ? -1 : Order.IndexOf(Player);
            }

            public List<StandingsRow> Order { get; }
            public StandingsRow Player { get; }
            public int PlayerIndex { get; }
            public bool Race { get; }
            public bool ByTrack { get; }
            public bool MultiClass { get; }
            public bool OwnClass { get; }
            public double LapTime { get; }
            public double SessionBest { get; }

            private static double Relative(double difference)
            {
                double rel = difference - Math.Round(difference);
                return rel <= -0.5 ? rel + 1 : rel;
            }

            private int PositionKey(StandingsRow row)
            {
                int position = OwnClass && row.ClassPosition > 0 ? row.ClassPosition : row.Position;
                return position > 0 ? position : int.MaxValue;
            }

            public int PositionOf(StandingsRow row)
            {
                return OwnClass && row.ClassPosition > 0 ? row.ClassPosition : row.Position;
            }

            public int StartPositionOf(StandingsRow row)
            {
                return OwnClass ? row.StartClassPosition : row.StartPosition;
            }
        }
    }
}
