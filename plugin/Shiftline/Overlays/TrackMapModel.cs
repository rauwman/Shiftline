using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Shiftline.Plugin
{
    /// <summary>Sizes, colours and the geometry of the Track Map for a set of options and a track.</summary>
    internal sealed class TrackMapLook
    {
        public bool Vertical;
        public double Length;
        public double Round;
        public byte Alpha;
        public double Dot;
        public double PlayerRadius;
        public double Reach;
        public string Style;
        public bool Smooth;
        public bool Radio;
        public string WaveColour;
        public double Thickness;
        public Color LineColour;
        public double RoadGap;
        public byte RoadFill;               // alpha of the fill between the two lines of the road (0: none)
        public double StripWidth;
        public bool ShapesOn;
        public bool Usual;
        public int TrackSize;
        public string StartLine;
        public bool Ring;
        public double AlongPad;
        public double CrossCentre;
        public double RingRadius;
        public Point RingCentre;
        public double Turn = 1;
        public double StartAngle;
        public TrackShape Shape;            // the layout fitted into the window; null = line or ring
        public Size DesignSize;
    }

    /// <summary>What the Track Map shows of one car, apart from where it is.</summary>
    internal sealed class CarFact
    {
        public StandingsRow Row;
        public bool Player;
        public bool Pit;                    // drawn as in the pit (never the player)
        public bool OtherClass;
        public bool Fastest;
        public int LapRelation;             // +n: n laps up on the player, -n: n laps down
        public int Position;                // live in races (see FactsOf), 0 = none
        public Brush Colour;
        public string Text;
    }

    /// <summary>
    /// The rules of the Track Map, without any drawing: sizes and colours for a set of options and a track (LookOf), and what
    /// the map shows of each car (FactsOf). The page (TrackMapHtmlView, web/trackmap.js) places and draws them.
    /// </summary>
    internal static class TrackMapModel
    {
        internal const double EdgeMargin = 6;
        internal const double PlayerZoom = 1.25;
        internal const double RingWidth = 2.5;      // the ring of the player and the lap rings
        internal const double EdgeWidth = 1;        // the thin dark edge outside a ring
        internal const double GlassWidth = 1.5;     // edge of a plain circle in its own colour

        internal static readonly Brush TextBrush = StandingsOverlay.Frozen(0xFF, 0xE8, 0xEC, 0xF1);

        /// <summary>How often the map drew live cars, and how far in the past it shows them (data check of the page).</summary>
        internal static long FramesDrawn;
        internal static double ShownDelay;

        /// <summary>Time the Track Map spent on SimHub's UI thread (Stopwatch ticks), for the data check record.</summary>
        private static long _workTicks;

        internal static double TakeWorkMs()
        {
            return System.Threading.Interlocked.Exchange(ref _workTicks, 0) * 1000.0 / Stopwatch.Frequency;
        }

        internal static void AddWork(long started)
        {
            System.Threading.Interlocked.Add(ref _workTicks, Stopwatch.GetTimestamp() - started);
        }

        internal static TrackMapLook LookOf(TrackMapOptions options, TrackLayout layout)
        {
            TrackMapLook look = new TrackMapLook
            {
                Vertical = options.Vertical,
                Length = options.Length,
                Round = options.Get("Corners") == "Square" ? 0 : 8,
                Alpha = (byte)Math.Round(255 * Math.Max(0, Math.Min(100, options.GetInt("Opacity"))) / 100.0),
                Dot = Math.Max(10, options.GetInt("Dot")),
                Style = options.Get("LineStyle"),
                Smooth = options.GetBool("Smooth"),
                Radio = options.GetBool("Radio"),
                WaveColour = options.Get("WaveColour"),
                Thickness = Math.Max(1, options.GetInt("Thickness")),
                LineColour = LineColour(options.Get("LineColour")),
                ShapesOn = options.TrackShapes,
                Usual = options.Get("TrackOrientation") != "North",
                TrackSize = options.SizeOf("TrackSize"),
                StartLine = options.Get("StartLine"),
            };
            look.PlayerRadius = look.Dot * PlayerZoom / 2;
            look.Reach = look.PlayerRadius;           // the player's ring ends at this radius (see RingOutset)
            look.RoadFill = (byte)Math.Round(255 * Math.Max(0, Math.Min(100, options.GetInt("RoadFill"))) / 100.0);
            look.RoadGap = look.Thickness * 2 + 2;
            look.StripWidth = look.PlayerRadius * 2 + 8;

            // with track shapes on, a track without a shape gets a ring of the track size (it takes the same place as a map)
            look.Ring = options.Ring || look.ShapesOn;
            look.AlongPad = EdgeMargin + look.Reach;
            look.CrossCentre = EdgeMargin + look.Reach;
            if (look.Ring)
            {
                int ringSize = look.ShapesOn ? look.TrackSize : options.RingSize;
                double side = ringSize + 2 * look.AlongPad;
                look.RingRadius = ringSize / 2.0;
                look.RingCentre = new Point(side / 2, side / 2);
                look.Turn = options.Get("Direction") == "Anticlockwise" ? -1 : 1;
                switch (options.Get("StartAt"))
                {
                    case "Right":
                        look.StartAngle = 90;
                        break;
                    case "Bottom":
                        look.StartAngle = 180;
                        break;
                    case "Left":
                        look.StartAngle = 270;
                        break;
                    default:
                        look.StartAngle = 0;
                        break;
                }

                look.DesignSize = new Size(side, side);
            }
            else
            {
                look.DesignSize = look.Vertical
                    ? new Size(2 * look.CrossCentre, look.Length + 2 * look.AlongPad)
                    : new Size(look.Length + 2 * look.AlongPad, 2 * look.CrossCentre);
            }

            // the real shape of the track, when there is one: then it replaces the line or the ring
            look.Shape = look.ShapesOn && layout != null ? TrackShape.Build(layout, look.Usual, look.TrackSize, EdgeMargin + look.Reach + 4) : null;
            if (look.Shape != null)
            {
                look.DesignSize = look.Shape.Size;
            }

            return look;
        }

        /// <summary>
        /// The cars the map shows (in the world, with a place on the lap, not filtered out by the options) and what is known of
        /// them apart from where they are: player, pit, class, fastest lap, laps up or down, the colour and the text.
        /// </summary>
        internal static List<CarFact> FactsOf(TrackMapOptions options, StandingsSnapshot snapshot, out StandingsRow player, out bool hasPlayer)
        {
            StandingsRow[] rows = snapshot.Rows ?? new StandingsRow[0];
            player = null;
            foreach (StandingsRow row in rows)
            {
                if (row.IsPlayer || row.CarIdx == snapshot.PlayerCarIdx)
                {
                    player = row;
                    break;
                }
            }

            hasPlayer = player != null && player.TrackSurface != -1 && player.LapDistPct >= 0;
            bool classOnly = options.Get("Show") == "Class" && player != null;
            bool hidePit = options.Get("Pit") == "Hide";
            bool lapRings = options.GetBool("LapRings") && (snapshot.IsRace || snapshot.IsSample);
            string label = options.Get("Label");
            bool marksFastest = options.GetBool("Fastest");

            // the fastest lap of each class, and whether there is more than one class
            Dictionary<int, double> fastest = new Dictionary<int, double>();
            foreach (StandingsRow row in rows)
            {
                double best;
                if (row.BestLap > 0 && (!fastest.TryGetValue(row.ClassId, out best) || row.BestLap < best))
                {
                    fastest[row.ClassId] = row.BestLap;
                }
            }

            bool classes = fastest.Count > 1;
            // one class in the session: the colour chosen for that, instead of the one the game gives the class
            Brush oneClass = rows.Select(r => r.ClassId).Distinct().Count() > 1 ? null : OneClassBrush(options.Get("OneClass"));
            if (label == "Auto")
            {
                label = !snapshot.IsRace && !snapshot.IsSample ? "Number" : classes ? "ClassPosition" : "Position";
            }

            // In races the game moves a car's position only at the line (iRacing), so the map counts it live from the laps
            // and the place on the lap; otherwise (and for cars not on the track) the game's position.
            Dictionary<int, int> live = new Dictionary<int, int>();
            Dictionary<int, int> liveInClass = new Dictionary<int, int>();
            if (snapshot.IsRace)
            {
                List<StandingsRow> order = rows
                    .OrderByDescending(r => r.TrackSurface != -1 && r.LapDistPct >= 0)
                    .ThenByDescending(r => r.TrackSurface != -1 && r.LapDistPct >= 0 ? r.Progress : 0)
                    .ThenBy(r => r.Position > 0 ? r.Position : int.MaxValue)
                    .ToList();
                Dictionary<int, int> inClass = new Dictionary<int, int>();
                for (int k = 0; k < order.Count; k++)
                {
                    int n;
                    inClass.TryGetValue(order[k].ClassId, out n);
                    inClass[order[k].ClassId] = n + 1;
                    live[order[k].CarIdx] = k + 1;
                    liveInClass[order[k].CarIdx] = n + 1;
                }
            }

            Func<StandingsRow, int> positionOf = r => { int p; return live.TryGetValue(r.CarIdx, out p) ? p : r.Position; };
            Func<StandingsRow, int> classPositionOf = r => { int p; return liveInClass.TryGetValue(r.CarIdx, out p) ? p : r.ClassPosition; };
            List<CarFact> facts = new List<CarFact>();
            foreach (StandingsRow row in rows)
            {
                if (row.TrackSurface == -1 || !(row.LapDistPct >= 0))
                {
                    continue;       // not in the world, or no position on the track
                }

                bool isPlayer = ReferenceEquals(row, player);
                if (isPlayer && !hasPlayer)
                {
                    continue;
                }

                bool otherClass = player != null && !isPlayer && row.ClassId != player.ClassId;
                if (classOnly && otherClass)
                {
                    continue;
                }

                bool pit = row.OnPitRoad && !isPlayer;
                if (pit && hidePit)
                {
                    continue;
                }

                double classBest;
                // only the own class: the fastest lap of another class says nothing about your race (all classes without a player)
                bool isFastest = marksFastest && row.BestLap > 0 && (player == null || row.ClassId == player.ClassId)
                    && fastest.TryGetValue(row.ClassId, out classBest) && row.BestLap <= classBest;

                int lapRelation = 0;
                if (lapRings && hasPlayer && !isPlayer)
                {
                    // a whole lap or more apart: the map shows the whole lap, so a car half a lap ahead is simply ahead (the
                    // Relatives' rule, which looks the short way round the lap, marked it from the far side of the track on)
                    lapRelation = (int)Math.Truncate(row.Progress - player.Progress);
                }

                string text = string.Empty;
                switch (label)
                {
                    case "Number":
                        text = row.Number ?? string.Empty;
                        if (text.Length == 0)
                        {
                            // games without car numbers (AMS2 through SimHub): the position instead, in the class with several
                            int position = classes && classPositionOf(row) > 0 ? classPositionOf(row) : positionOf(row);
                            text = position > 0 ? position.ToString(CultureInfo.InvariantCulture) : string.Empty;
                        }

                        break;
                    case "Position":
                        text = positionOf(row) > 0 ? positionOf(row).ToString(CultureInfo.InvariantCulture) : string.Empty;
                        break;
                    case "ClassPosition":
                        text = classPositionOf(row) > 0 ? classPositionOf(row).ToString(CultureInfo.InvariantCulture) : string.Empty;
                        break;
                    case "Start":
                        int start = classes ? row.StartClassPosition : row.StartPosition;
                        text = start > 0 ? start.ToString(CultureInfo.InvariantCulture) : string.Empty;
                        break;
                }

                facts.Add(new CarFact
                {
                    Row = row,
                    Player = isPlayer,
                    Pit = pit,
                    OtherClass = otherClass,
                    Fastest = isFastest,
                    LapRelation = lapRelation,
                    Position = positionOf(row),
                    Colour = oneClass ?? StandingsOverlay.ClassBrush(row.ClassColor),
                    Text = text
                });
            }

            return facts;
        }

        private static readonly Brush OneClassBlue = StandingsOverlay.Frozen(0xFF, 0x3F, 0xA9, 0xF5);
        private static readonly Brush OneClassWhite = StandingsOverlay.Frozen(0xFF, 0xF4, 0xF6, 0xF9);
        private static readonly Brush OneClassOrange = StandingsOverlay.Frozen(0xFF, 0xFF, 0x8C, 0x00);
        private static readonly Brush OneClassGreen = StandingsOverlay.Frozen(0xFF, 0x3D, 0xDC, 0x84);

        /// <summary>The colour of the cars when only one class drives; null for "Game" (the class colour the game sends).</summary>
        private static Brush OneClassBrush(string choice)
        {
            switch (choice)
            {
                case "Game":
                    return null;
                case "White":
                    return OneClassWhite;
                case "Orange":
                    return OneClassOrange;
                case "Green":
                    return OneClassGreen;
                default:
                    return OneClassBlue;
            }
        }


        private static Color LineColour(string name)
        {
            switch (name)
            {
                case "White":
                    return Color.FromRgb(0xF4, 0xF6, 0xF9);
                case "LightBlue":
                    return Color.FromRgb(0x7C, 0xC6, 0xFF);
                case "Orange":
                    return Color.FromRgb(0xFF, 0x8C, 0x00);
                case "Green":
                    return Color.FromRgb(0x3D, 0xDC, 0x84);
                case "Red":
                    return Color.FromRgb(0xF0, 0x40, 0x3E);
                case "Yellow":
                    return Color.FromRgb(0xFF, 0xD2, 0x3F);
                default:
                    return Color.FromRgb(0x8E, 0x98, 0xA6);
            }
        }
    }
}
