using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Shiftline.Plugin
{
    /// <summary>What the overlay host and the settings page need from the Track Map.</summary>
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

    /// <summary>The Track Map for the HTML page: the layout and the cars as JSON (see TrackMapModel for the rules).</summary>
    internal static class TrackMapHtml
    {
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
            foreach (CarFact fact in TrackMapModel.FactsOf(options, snapshot, out player, out hasPlayer))
            {
                StandingsRow row = fact.Row;
                cars.Add(new JObject
                {
                    { "i", row.CarIdx },
                    { "t", fact.Text },
                    { "c", StandingsOverlay.Css(fact.Colour) },
                    { "k", StandingsOverlay.Css(fact.Pit ? TrackMapModel.TextBrush : StandingsOverlay.ReadableOn(fact.Colour)) },
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
    /// the page shows the cars a little in the past, between two known places, so they move evenly.
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
            _look = TrackMapModel.LookOf(options, _layout);
            _layoutJson = TrackMapHtml.HtmlLayoutJson(options, _look);
            Post(_layoutJson);
            SendCars();
            Post(_placesJson);
        }

        public void Update(StandingsSnapshot snapshot)
        {
            long started = Stopwatch.GetTimestamp();
            try
            {
                UpdateCore(snapshot);
            }
            finally
            {
                TrackMapModel.AddWork(started);
            }
        }

        private void UpdateCore(StandingsSnapshot snapshot)
        {
            StandingsSnapshot next = snapshot ?? StandingsSnapshot.Empty;
            if (ReferenceEquals(next, _snapshot) || (next.Sequence != 0 && next.Sequence == _snapshot.Sequence && !next.IsSample && !_snapshot.IsSample))
            {
                return;         // no new data (the overlay can tick faster than a game delivers)
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
            long started = Stopwatch.GetTimestamp();
            try
            {
                UpdatePositionsCore(positions);
            }
            finally
            {
                TrackMapModel.AddWork(started);
            }
        }

        private void UpdatePositionsCore(CarPositions positions)
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

            _carsJson = TrackMapHtml.HtmlCarsJson(_options, _snapshot, _startOffset, _talking);
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
                TrackMapModel.FramesDrawn += (long)message["frames"];
                TrackMapModel.ShownDelay = (double)message["delay"];
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
