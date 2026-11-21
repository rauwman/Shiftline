using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Shiftline.Plugin
{
    /// <summary>What the overlay host and the settings page need from the Track Map, whether WPF or the HTML page draws it.</summary>
    internal interface ITrackMapView
    {
        Size DesignSize { get; }

        bool Vertical { get; }

        bool Ring { get; }

        bool IsTrack { get; }

        TrackLayout Layout { get; }

        string SizeKey { get; }

        int TalkingCarIdx { get; set; }

        event Action DesignSizeChanged;

        void ApplyOptions(TrackMapOptions options);

        void Update(StandingsSnapshot snapshot);

        void UpdatePositions(CarPositions positions);
    }

    /// <summary>Sizes, colours and the geometry of the Track Map for a set of options and a track (shared by WPF and HTML).</summary>
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

    internal sealed partial class TrackMapOverlay
    {
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

        private static string Css(Color colour, byte alpha)
        {
            return "rgba(" + colour.R + "," + colour.G + "," + colour.B + "," + (alpha / 255.0).ToString("0.###", CultureInfo.InvariantCulture) + ")";
        }

        private static JArray PointsJson(Point[] points)
        {
            JArray json = new JArray();
            if (points != null)
            {
                foreach (Point p in points)
                {
                    json.Add(Math.Round(p.X, 2));
                    json.Add(Math.Round(p.Y, 2));
                }
            }

            return json;
        }

        /// <summary>The layout for the page: sizes, colours, the line, the ring or the fitted track shape.</summary>
        internal static string HtmlLayoutJson(TrackMapOptions options, TrackMapLook look)
        {
            Color colour = look.LineColour;
            Color wave = WaveColourOf(look.WaveColour);
            JObject json = new JObject
            {
                { "type", "track-layout" },
                { "width", look.DesignSize.Width },
                { "height", look.DesignSize.Height },
                { "round", look.Round },
                { "plate", Css(Color.FromRgb(0x0E, 0x12, 0x19), look.Alpha) },
                { "font", options.Get("Font") },
                { "mode", look.Shape != null ? "shape" : look.Ring ? "ring" : "line" },
                { "vertical", look.Vertical },
                { "length", look.Length },
                { "alongPad", look.AlongPad },
                { "cross", look.CrossCentre },
                { "ringR", look.RingRadius },
                { "ringX", look.RingCentre.X },
                { "ringY", look.RingCentre.Y },
                { "turn", look.Turn },
                { "startAngle", look.StartAngle },
                { "centred", options.Centred },
                { "range", options.GetInt("Range") / 100.0 },
                { "dot", look.Dot },
                { "playerR", look.PlayerRadius },
                { "reach", look.Reach },
                { "style", look.Style },
                { "thickness", look.Thickness },
                { "line", Css(colour, 0xB0) },
                { "road", Css(colour, 0xD8) },
                { "roadFill", look.RoadFill > 0 ? Css(colour, look.RoadFill) : null },
                { "roadGap", look.RoadGap },
                { "strip", Css(colour, look.Alpha) },
                { "stripW", look.StripWidth },
                { "pitLane", Css(colour, 0x90) },
                { "startLine", look.StartLine },
                { "smooth", look.Smooth },
                { "radio", look.Radio },
                { "wave", look.WaveColour == "Class" ? null : Css(wave, 0xFF) },
            };

            if (look.Shape != null)
            {
                json["points"] = PointsJson(look.Shape.Points);
                json["closed"] = look.Shape.Closed;
                json["pit"] = look.Shape.PitPoints == null ? null : PointsJson(look.Shape.PitPoints);
                json["pitEntry"] = look.Shape.PitEntry;
                json["pitExit"] = look.Shape.PitExit;
            }

            return json.ToString(Formatting.None);
        }

        private static Color WaveColourOf(string name)
        {
            switch (name)
            {
                case "Shiftline":
                    return Color.FromRgb(0x3F, 0xA9, 0xF5);
                case "Green":
                    return Color.FromRgb(0x3D, 0xDC, 0x84);
                case "Orange":
                    return Color.FromRgb(0xFF, 0x8C, 0x00);
                default:
                    return Color.FromRgb(0xF4, 0xF6, 0xF9);
            }
        }

        /// <summary>The cars for the page (with the update rate): what each shows, not where it is.</summary>
        internal static string HtmlCarsJson(TrackMapOptions options, StandingsSnapshot snapshot, double startOffset, int talking)
        {
            StandingsRow player;
            bool hasPlayer;
            JArray cars = new JArray();
            foreach (CarFact fact in FactsOf(options, snapshot, out player, out hasPlayer))
            {
                StandingsRow row = fact.Row;
                cars.Add(new JObject
                {
                    { "i", row.CarIdx },
                    { "t", fact.Text },
                    { "c", StandingsOverlay.Css(fact.Colour) },
                    { "k", StandingsOverlay.Css(fact.Pit ? TextBrush : StandingsOverlay.ReadableOn(fact.Colour)) },
                    { "p", fact.Player },
                    { "pit", fact.Pit },
                    { "inPit", row.OnPitRoad },
                    { "o", fact.OtherClass },
                    { "f", fact.Fastest },
                    { "l", fact.LapRelation },
                    { "pos", fact.Position > 0 ? fact.Position : 9999 },
                    { "at", Math.Round(row.LapDistPct, 5) },
                });
            }

            return new JObject
            {
                { "type", "track-cars" },
                { "cars", cars },
                { "player", hasPlayer ? player.CarIdx : -1 },
                { "offset", startOffset },
                { "talking", talking },
                { "sample", snapshot.IsSample },
            }.ToString(Formatting.None);
        }
    }

    /// <summary>
    /// The Track Map drawn by the HTML page (a canvas), in an overlay window or in the preview of the settings page. C# sends
    /// the layout, the cars with the update rate and the places from the fast position feed (about 60 times per second);
    /// the page shows the cars a little in the past, between two known places, as the WPF map does.
    /// </summary>
    internal sealed class TrackMapHtmlView : ITrackMapView
    {
        public const string Page = "trackmap.html";
        private const double UnixEpochSeconds = 62135596800;    // DateTime ticks start at year 1, the page's clock at 1970
        private TrackMapOptions _options;
        private TrackLayout _layout;
        private TrackMapLook _look;
        private IHtmlSurface _window;
        private string _layoutJson;
        private string _carsJson;
        private string _placesJson;
        private StandingsSnapshot _snapshot = StandingsSnapshot.Empty;
        private long _feedSequence = -1;
        private DateTime _feedSeen = DateTime.MinValue;
        private double _startOffset;
        private int _talking = -1;

        public TrackMapHtmlView()
        {
            ApplyOptions(TrackMapOptions.Load());
        }

        public event Action DesignSizeChanged;

        public Size DesignSize
        {
            get { return _look.DesignSize; }
        }

        public bool Vertical
        {
            get { return _look.Vertical; }
        }

        public bool Ring
        {
            get { return _look.Ring; }
        }

        public bool IsTrack
        {
            get { return _look.Shape != null; }
        }

        public TrackLayout Layout
        {
            get { return _look.Shape == null ? null : _layout; }
        }

        public string SizeKey
        {
            get { return _look.Shape != null || _look.ShapesOn ? "TrackSize" : _look.Ring ? "RingSize" : "Length"; }
        }

        public int TalkingCarIdx
        {
            get { return _talking; }
            set
            {
                if (_talking != value)
                {
                    _talking = value;
                    SendCars();
                }
            }
        }

        public void Attach(IHtmlSurface window)
        {
            Detach();
            _window = window;
            _window.PageReady += OnPageReady;
            _window.PageMessage += OnPageMessage;
            OnPageReady();
        }

        public void Detach()
        {
            if (_window != null)
            {
                _window.PageReady -= OnPageReady;
                _window.PageMessage -= OnPageMessage;
                _window = null;
            }
        }

        public void ApplyOptions(TrackMapOptions options)
        {
            _options = options;
            _look = TrackMapOverlay.LookOf(options, _layout);
            _layoutJson = TrackMapOverlay.HtmlLayoutJson(options, _look);
            Post(_layoutJson);
            SendCars();
            Post(_placesJson);
        }

        public void Update(StandingsSnapshot snapshot)
        {
            StandingsSnapshot next = snapshot ?? StandingsSnapshot.Empty;
            if (ReferenceEquals(next, _snapshot) || (next.Sequence != 0 && next.Sequence == _snapshot.Sequence && !next.IsSample && !_snapshot.IsSample))
            {
                return;         // no new data (see TrackMapOverlay.Update)
            }

            _snapshot = next;
            TrackLayout layout = _look.ShapesOn ? TrackMapTracks.For(_snapshot).Layout : null;
            if (!ReferenceEquals(layout, _layout))
            {
                _layout = layout;
                Size before = _look.DesignSize;
                ApplyOptions(_options);
                if (before != _look.DesignSize && DesignSizeChanged != null)
                {
                    DesignSizeChanged();
                }
            }

            TrackAlignmentResult alignment = _look.Shape != null ? TrackAlignment.For(_snapshot, _layout) : TrackAlignmentResult.None;
            _startOffset = alignment.Usable ? alignment.Offset : 0;
            SendCars();

            // without the fast feed (or for the invented cars) the places come from the standings
            if (_snapshot.IsSample || (DateTime.UtcNow - _feedSeen).TotalSeconds > 0.5)
            {
                double taken = _snapshot.TakenUtcTicks > 0 ? _snapshot.TakenUtcTicks / (double)TimeSpan.TicksPerSecond : DateTime.UtcNow.Ticks / (double)TimeSpan.TicksPerSecond;
                JArray places = new JArray();
                foreach (StandingsRow row in _snapshot.Rows ?? new StandingsRow[0])
                {
                    places.Add(new JArray(row.CarIdx, Math.Round(row.LapDistPct, 6), row.OnPitRoad ? 1 : 0));
                }

                SendPlaces(taken, places);
            }
        }

        public void UpdatePositions(CarPositions positions)
        {
            if (positions == null || positions.Sequence == 0 || positions.Sequence == _feedSequence || _snapshot.IsSample || !_snapshot.HasData)
            {
                return;
            }

            _feedSequence = positions.Sequence;
            _feedSeen = DateTime.UtcNow;
            JArray places = new JArray();
            foreach (StandingsRow row in _snapshot.Rows ?? new StandingsRow[0])
            {
                int idx = row.CarIdx;
                bool pit = idx >= 0 && idx < positions.Pit.Length ? positions.Pit[idx] : row.OnPitRoad;
                places.Add(new JArray(idx, Math.Round(positions.PctOf(idx), 6), pit ? 1 : 0));
            }

            SendPlaces(positions.TakenSeconds, places);
        }

        private void SendPlaces(double takenSeconds, JArray places)
        {
            _placesJson = new JObject
            {
                { "type", "track-places" },
                { "t", Math.Round(takenSeconds - UnixEpochSeconds, 4) },
                { "cars", places },
            }.ToString(Formatting.None);
            Post(_placesJson);
        }

        private void SendCars()
        {
            if (_options == null)
            {
                return;
            }

            _carsJson = TrackMapOverlay.HtmlCarsJson(_options, _snapshot, _startOffset, _talking);
            Post(_carsJson);
        }

        /// <summary>The page counts the frames it drew with live cars; the data check of the Track Map page shows them.</summary>
        private static void OnPageMessage(string json)
        {
            if (json == null || json.IndexOf("track-stats", StringComparison.Ordinal) < 0)
            {
                return;
            }

            try
            {
                JObject message = JObject.Parse(json);
                TrackMapOverlay.FramesDrawn += (long)message["frames"];
                TrackMapOverlay.ShownDelay = (double)message["delay"];
            }
            catch (Exception)
            {
                // not a message of ours
            }
        }

        private void OnPageReady()
        {
            Post(_layoutJson);
            Post(_carsJson);
            Post(_placesJson);
        }

        private void Post(string json)
        {
            if (_window != null && json != null)
            {
                _window.Post(json);
            }
        }
    }
}
