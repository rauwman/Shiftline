using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Media.Animation;
using System.Windows;
using System.Windows.Media;

namespace Shiftline.Plugin
{
    /// <summary>
    /// The Track Map: the cars as numbered circles on a line (horizontal or vertical), either the whole lap or the part around the
    /// player. Drawn in two layers (the map, and the cars gliding between data updates); the size follows from the options (DesignSize), there is no
    /// other scaling. A circle carries the class colour and a dark outline, the bold number sits inside. The player is larger with a
    /// light ring, a lap ring is the same band in red or blue; both sit flush on the circle. The player is always on top. Where circles overlap the important ones stay on top: cars close to the player over far
    /// ones, the own class over the others, cars in the pit below everything.
    /// </summary>
    internal sealed class TrackMapOverlay : FrameworkElement
    {
        private const double EdgeMargin = 6;
        private const double PlayerZoom = 1.25;
        private const double OutlineWidth = 2;      // the hollow ring of a car in the pit
        private const double GlassWidth = 1.5;      // edge of a plain circle in its own colour, darker and see-through
        private const double GlassShade = 0.75;
        private const byte GlassAlpha = 0x66;       // 40 %
        private const double EdgeWidth = 1;         // the thin dark edge outside a ring
        private const double RingWidth = 2.5;       // the ring of the player and the lap rings, drawn inside the circle's size
        private const double PitOpacity = 0.55;
        private const double NumberSize = 0.55;     // height of the figures as part of the coloured disc's diameter
        private const double NumberRoom = 0.82;     // widest a number may be, as part of the diameter; longer numbers shrink
        private const double StartLineWidth = 2;    // the start/finish line across the track
        private const double RoadLineWidth = 1.5;   // one of the two lines of the road
        private const double FlowSeconds = 4;       // the Shiftline colours run once along the start/finish line in this time

        private static readonly Brush TextBrush = StandingsOverlay.Frozen(0xFF, 0xE8, 0xEC, 0xF1);
        private static readonly Brush OutlineBrush = StandingsOverlay.Frozen(0xE6, 0x0E, 0x12, 0x19);
        private static readonly Brush PlayerRingBrush = StandingsOverlay.Frozen(0xFF, 0xF4, 0xF6, 0xF9);
        private static readonly Brush LapAheadBrush = StandingsOverlay.Frozen(0xFF, 0xF0, 0x40, 0x3E);      // a lap up on you, like the Relatives
        private static readonly Brush LapBehindBrush = StandingsOverlay.Frozen(0xFF, 0x7F, 0xB2, 0xE0);     // a lap down
        private static readonly Pen EdgePen = MakePen(OutlineBrush, EdgeWidth);
        private static readonly Pen PlayerRingPen = MakePen(PlayerRingBrush, RingWidth);
        private static readonly Pen LapAheadPen = MakePen(LapAheadBrush, RingWidth);
        private static readonly Pen LapBehindPen = MakePen(LapBehindBrush, RingWidth);
        private static readonly Pen FastestPen = MakePen(StandingsOverlay.Frozen(0xFF, 0xC0, 0x7C, 0xFF), RingWidth);    // violet: fastest lap

        private sealed class Figure
        {
            public Geometry Shape;               // the outlines of the figures, their middle at (0, 0)
        }

        private readonly Dictionary<string, Figure> _texts = new Dictionary<string, Figure>();
        private readonly Dictionary<Brush, Pen> _hollowPens = new Dictionary<Brush, Pen>();
        private readonly Dictionary<Color, Pen> _outlinePens = new Dictionary<Color, Pen>();
        private TrackMapOptions _options;
        private StandingsSnapshot _snapshot = StandingsSnapshot.Empty;
        private Typeface _typeface;
        private Brush _plate;
        private double _round;
        private Pen _linePen;
        private Pen _roadPen;
        private double _roadGap;                    // distance between the two lines of the road (centre to centre)
        private Brush _stripBrush;
        private Pen _stripPen;                      // the strip around the ring: one wide circle
        private bool _ring;
        private double _ringRadius;                 // radius of the middle of the track on the ring
        private Point _ringCentre;
        private double _startAngle;                 // where start/finish sits on the ring, in degrees clockwise from the top
        private double _turn;                       // +1 clockwise, -1 anticlockwise
        private string _startLine = "Off";
        private Pen _flowPen;                       // the moving Shiftline colours; WPF animates the brush itself, no redraw needed
        private double _flowLength;
        private string _style;
        private double _dpi = 1;
        private bool _vertical;
        private double _length;
        private double _dot;
        private double _playerRadius;
        private double _reach;                      // from the centre of the player's circle to its outer edge
        private double _alongPad;                   // from the edge of the window to the first point of the line
        private double _crossCentre;                // distance of the line from the top (horizontal) or the left edge (vertical)
        private Size _designSize;

        // Two layers: the map (background and track) is drawn only when the options change, the cars on top are drawn for every
        // frame of the screen while they move. Between two data updates every car glides from where it was to where it is now
        // (one update behind), so the cars move smoothly whatever the update rate is.
        private readonly DrawingVisual _trackLayer = new DrawingVisual();
        private readonly DrawingVisual _carLayer = new DrawingVisual();
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private readonly Dictionary<int, Glide> _glides = new Dictionary<int, Glide>();
        private double _lastUpdate = -1;            // seconds on Clock
        private double _interval = 0.05;            // time between two data updates, smoothed
        private bool _rendering;                    // hooked to CompositionTarget.Rendering
        private double _shownPlayerPct;
        private const double MaxGlide = 0.05;       // a step longer than this part of a lap between two updates is a jump, not glided

        private sealed class Glide
        {
            public double From;                     // LapDistPct shown at the last update
            public double To;                       // LapDistPct of the last update
            public bool Pit;
        }

        private sealed class Car
        {
            public StandingsRow Row;
            public Point At;                        // middle of the circle in the window
            public bool Fastest;                    // holds the fastest lap of its class
            public bool Player;
            public bool Pit;
            public bool OtherClass;                 // another class than the player (only when the player is known)
            public double Distance;                 // to the player along the lap, as a fraction of a lap (0 when there is no player)
            public int LapRelation;                 // +n: n laps up on the player, -n: n laps down
            public Brush Colour;
            public string Text;
        }

        public TrackMapOverlay()
        {
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            AddVisualChild(_trackLayer);
            AddVisualChild(_carLayer);
            IsVisibleChanged += (s, e) =>
            {
                if (!IsVisible)
                {
                    StopRendering();
                }
            };
            Unloaded += (s, e) => StopRendering();
            Loaded += (s, e) =>
            {
                _dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
                _texts.Clear();
                DrawCarLayer();
            };
            ApplyOptions(TrackMapOptions.Load());
        }

        public bool Vertical
        {
            get { return _vertical; }
        }

        public bool Ring
        {
            get { return _ring; }
        }

        /// <summary>The window size that fits the current design.</summary>
        public Size DesignSize
        {
            get { return _designSize; }
        }

        public void ApplyOptions(TrackMapOptions options)
        {
            _options = options;
            _vertical = options.Vertical;
            _length = options.Length;
            _round = options.Get("Corners") == "Square" ? 0 : 8;
            byte alpha = (byte)Math.Round(255 * Math.Max(0, Math.Min(100, options.GetInt("Opacity"))) / 100.0);
            _plate = StandingsOverlay.Frozen(alpha, 0x0E, 0x12, 0x19);
            _typeface = new Typeface(OverlayFonts.Get(options.Get("Font")), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            _dot = Math.Max(10, options.GetInt("Dot"));
            _playerRadius = _dot * PlayerZoom / 2;
            _reach = _playerRadius;
            _style = options.Get("LineStyle");
            double thickness = Math.Max(1, options.GetInt("Thickness"));
            Color colour = LineColour(options.Get("LineColour"));
            _linePen = MakeLinePen(Frozen(0xB0, colour), thickness);
            _roadPen = new Pen(Frozen(0xD8, colour), RoadLineWidth) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat };
            _roadPen.Freeze();
            _roadGap = thickness * 2 + 2;
            _stripBrush = Frozen(alpha, colour);
            _stripPen = new Pen(_stripBrush, _playerRadius * 2 + 8);
            _stripPen.Freeze();
            _startLine = options.Get("StartLine");
            _texts.Clear();

            _ring = options.Ring;
            _alongPad = EdgeMargin + _reach;
            _crossCentre = EdgeMargin + _reach;
            if (_ring)
            {
                double side = options.RingSize + 2 * _alongPad;
                _ringRadius = options.RingSize / 2.0;
                _ringCentre = new Point(side / 2, side / 2);
                _turn = options.Get("Direction") == "Anticlockwise" ? -1 : 1;
                switch (options.Get("StartAt"))
                {
                    case "Right":
                        _startAngle = 90;
                        break;
                    case "Bottom":
                        _startAngle = 180;
                        break;
                    case "Left":
                        _startAngle = 270;
                        break;
                    default:
                        _startAngle = 0;
                        break;
                }

                _designSize = new Size(side, side);
            }
            else
            {
                _designSize = _vertical
                    ? new Size(2 * _crossCentre, _length + 2 * _alongPad)
                    : new Size(_length + 2 * _alongPad, 2 * _crossCentre);
            }

            InvalidateMeasure();
            DrawTrackLayer();
            DrawCarLayer();
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

        private static Brush Frozen(byte alpha, Color colour)
        {
            return StandingsOverlay.Frozen(alpha, colour.R, colour.G, colour.B);
        }

        public void Update(StandingsSnapshot snapshot)
        {
            double now = Clock.Elapsed.TotalSeconds;
            double before = GlideFraction();       // how far the cars had glided towards the last update
            if (_lastUpdate >= 0)
            {
                double gap = Math.Max(0.01, Math.Min(0.5, now - _lastUpdate));
                _interval = _interval * 0.7 + gap * 0.3;
            }

            _lastUpdate = now;
            _snapshot = snapshot ?? StandingsSnapshot.Empty;
            bool moving = false;
            HashSet<int> seen = new HashSet<int>();
            foreach (StandingsRow row in _snapshot.Rows ?? new StandingsRow[0])
            {
                seen.Add(row.CarIdx);
                double pct = row.LapDistPct;
                Glide glide;
                if (!_glides.TryGetValue(row.CarIdx, out glide))
                {
                    _glides[row.CarIdx] = new Glide { From = pct, To = pct, Pit = row.OnPitRoad };
                    continue;
                }

                // glide on from where the car is shown now; a jump (into or out of the pit, a tow, a reset, no position) is not glided
                double shown = glide.From >= 0 && glide.To >= 0 ? Wrap(glide.From + TrackMapGeometry.Signed(glide.To, glide.From) * before) : glide.To;
                bool jump = pct < 0 || shown < 0 || row.OnPitRoad != glide.Pit || Math.Abs(TrackMapGeometry.Signed(pct, shown)) > MaxGlide;
                glide.From = jump ? pct : shown;
                glide.To = pct;
                glide.Pit = row.OnPitRoad;
                moving |= glide.From != glide.To;
            }

            List<int> gone = new List<int>();
            foreach (int idx in _glides.Keys)
            {
                if (!seen.Contains(idx))
                {
                    gone.Add(idx);
                }
            }

            foreach (int idx in gone)
            {
                _glides.Remove(idx);
            }

            DrawCarLayer();
            if (moving && IsVisible)
            {
                StartRendering();
            }
        }

        /// <summary>Where a car is shown now: between the last two updates, or its last position.</summary>
        private double Shown(StandingsRow row)
        {
            Glide glide;
            if (!_glides.TryGetValue(row.CarIdx, out glide) || glide.From < 0 || glide.To < 0)
            {
                return row.LapDistPct;
            }

            return Wrap(glide.From + TrackMapGeometry.Signed(glide.To, glide.From) * GlideFraction());
        }

        private static double Wrap(double pct)
        {
            return pct - Math.Floor(pct);
        }

        private double GlideFraction()
        {
            if (_lastUpdate < 0)
            {
                return 1;
            }

            return Math.Max(0, Math.Min(1, (Clock.Elapsed.TotalSeconds - _lastUpdate) / _interval));
        }

        private void StartRendering()
        {
            if (!_rendering)
            {
                _rendering = true;
                CompositionTarget.Rendering += OnFrame;
            }
        }

        private void StopRendering()
        {
            if (_rendering)
            {
                _rendering = false;
                CompositionTarget.Rendering -= OnFrame;
            }
        }

        /// <summary>One frame of the screen: draw the cars where they are now; stop once they have reached the last update.</summary>
        private void OnFrame(object sender, EventArgs e)
        {
            DrawCarLayer();
            if (GlideFraction() >= 1)
            {
                StopRendering();
            }
        }

        protected override int VisualChildrenCount
        {
            get { return 2; }
        }

        protected override Visual GetVisualChild(int index)
        {
            return index == 0 ? (Visual)_trackLayer : _carLayer;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            return _designSize;
        }

        // ================================================================ drawing

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            _dpi = newDpi.PixelsPerDip;
            _texts.Clear();
            DrawCarLayer();
        }

        /// <summary>The background and the track; only when the options change.</summary>
        private void DrawTrackLayer()
        {
            using (DrawingContext dc = _trackLayer.RenderOpen())
            {
                dc.DrawRoundedRectangle(_plate, null, new Rect(0, 0, _designSize.Width, _designSize.Height), _round, _round);
                DrawTrack(dc);
            }
        }

        /// <summary>The start/finish line (it moves in the mode Centred on me) and the cars.</summary>
        private void DrawCarLayer()
        {
            using (DrawingContext dc = _carLayer.RenderOpen())
            {
                DrawCars(dc);
            }
        }

        private void DrawCars(DrawingContext dc)
        {
            StandingsRow player;
            bool hasPlayer;
            bool centred;
            List<Car> cars = Collect(out player, out hasPlayer, out centred);
            double range = _options.GetInt("Range") / 100.0;
            double playerPct = _shownPlayerPct;

            if (_startLine != "Off")
            {
                if (_ring)
                {
                    DrawStartLine(dc, RingAngle(0, playerPct, centred));
                }
                else if (centred)
                {
                    double startFraction;
                    if (TrackMapGeometry.LineFraction(0, playerPct, true, range, out startFraction))
                    {
                        DrawStartLine(dc, startFraction);
                    }
                }
                else
                {
                    DrawStartLine(dc, 0);
                    DrawStartLine(dc, 1);
                }
            }

            // What lies on top where circles overlap, from below to above: cars in the pit, other classes, the own class, the
            // player. Inside each group the cars closer to the player lie on top of the far ones; without a player the better
            // position lies on top.
            cars.Sort((a, b) =>
            {
                int byRank = Rank(a).CompareTo(Rank(b));
                if (byRank != 0)
                {
                    return byRank;
                }

                int byDistance = b.Distance.CompareTo(a.Distance);
                return byDistance != 0 ? byDistance : PositionKey(b).CompareTo(PositionKey(a));
            });

            foreach (Car car in cars)
            {
                DrawCar(dc, car);
            }
        }

        /// <summary>The track from fraction 0 (start/finish; left or bottom) to fraction 1: a thin line, a road or a strip.</summary>
        private void DrawTrack(DrawingContext dc)
        {
            if (_ring)
            {
                if (_style == "Strip")
                {
                    dc.DrawEllipse(null, _stripPen, _ringCentre, _ringRadius, _ringRadius);
                }
                else if (_style == "Road")
                {
                    dc.DrawEllipse(null, _roadPen, _ringCentre, _ringRadius - _roadGap / 2, _ringRadius - _roadGap / 2);
                    dc.DrawEllipse(null, _roadPen, _ringCentre, _ringRadius + _roadGap / 2, _ringRadius + _roadGap / 2);
                }
                else
                {
                    dc.DrawEllipse(null, _linePen, _ringCentre, _ringRadius, _ringRadius);
                }

                return;
            }

            Point start = LinePoint(0, _crossCentre);
            Point end = LinePoint(1, _crossCentre);
            if (_style == "Strip")
            {
                double height = _playerRadius * 2 + 8;
                Rect strip = _vertical
                    ? new Rect(_crossCentre - height / 2, _alongPad - height / 2, height, _length + height)
                    : new Rect(_alongPad - height / 2, _crossCentre - height / 2, _length + height, height);
                double radius = Math.Min(_round, height / 2);
                dc.DrawRoundedRectangle(_stripBrush, null, strip, radius, radius);
            }
            else if (_style == "Road")
            {
                dc.DrawLine(_roadPen, LinePoint(0, _crossCentre - _roadGap / 2), LinePoint(1, _crossCentre - _roadGap / 2));
                dc.DrawLine(_roadPen, LinePoint(0, _crossCentre + _roadGap / 2), LinePoint(1, _crossCentre + _roadGap / 2));
            }
            else
            {
                dc.DrawLine(_linePen, start, end);
            }
        }

        /// <summary>
        /// The start/finish line: one line across the track, plain or in the moving Shiftline colours. On the line "at" is the
        /// fraction of the line, on the ring the angle in degrees.
        /// </summary>
        private void DrawStartLine(DrawingContext dc, double at)
        {
            double half = _reach + 2;
            Point centre;
            Vector across;
            if (_ring)
            {
                centre = RingPoint(at, _ringRadius);
                across = RingPoint(at, _ringRadius + 1) - centre;
            }
            else
            {
                centre = LinePoint(at, _crossCentre);
                across = _vertical ? new Vector(1, 0) : new Vector(0, 1);
            }

            // drawn upright in its own coordinates and turned across the track, so the colours always run along the line
            double angle = Math.Atan2(-across.X, across.Y) * 180 / Math.PI;
            dc.PushTransform(new TranslateTransform(centre.X, centre.Y));
            dc.PushTransform(new RotateTransform(angle));
            dc.DrawLine(StartPen(2 * half), new Point(0, -half), new Point(0, half));
            dc.Pop();
            dc.Pop();
        }

        private static readonly Brush StartWhite = StandingsOverlay.Frozen(0xFF, 0xF4, 0xF6, 0xF9);
        private static readonly Brush StartGrey = StandingsOverlay.Frozen(0xFF, 0x8E, 0x98, 0xA6);
        private static readonly Brush StartBlack = StandingsOverlay.Frozen(0xFF, 0x0E, 0x12, 0x19);
        private static readonly int[] ShiftlineColours = { 0x01ABFF, 0x903AA1, 0xFF8C00, 0xD60000 };

        private readonly Dictionary<Brush, Pen> _startPens = new Dictionary<Brush, Pen>();

        private Pen StartPen(double length)
        {
            Brush plain = null;
            switch (_startLine)
            {
                case "White":
                    plain = StartWhite;
                    break;
                case "Black":
                    plain = StartBlack;
                    break;
                case "Grey":
                    plain = StartGrey;
                    break;
            }

            if (plain != null)
            {
                Pen pen;
                if (!_startPens.TryGetValue(plain, out pen))
                {
                    pen = new Pen(plain, StartLineWidth) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat };
                    pen.Freeze();
                    _startPens[plain] = pen;
                }

                return pen;
            }

            return FlowPen(length);
        }

        /// <summary>
        /// The four Shiftline colours as a repeating gradient along the upright line (0, -length/2) to (0, length/2). The gradient is
        /// moved by a WPF animation, which runs at the frame rate of the screen without drawing the map again.
        /// </summary>
        private Pen FlowPen(double length)
        {
            if (_flowPen != null && Math.Abs(_flowLength - length) < 0.01)
            {
                return _flowPen;
            }

            LinearGradientBrush brush = new LinearGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                StartPoint = new Point(0, -length / 2),
                EndPoint = new Point(0, length / 2),
                SpreadMethod = GradientSpreadMethod.Repeat
            };
            for (int i = 0; i <= ShiftlineColours.Length; i++)
            {
                int c = ShiftlineColours[i % ShiftlineColours.Length];
                brush.GradientStops.Add(new GradientStop(Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c), i / (double)ShiftlineColours.Length));
            }

            TranslateTransform move = new TranslateTransform();
            brush.Transform = move;
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, length, TimeSpan.FromSeconds(FlowSeconds)) { RepeatBehavior = RepeatBehavior.Forever });
            _flowPen = new Pen(brush, StartLineWidth) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat };
            _flowLength = length;
            return _flowPen;
        }

        private static int Rank(Car car)
        {
            return car.Player ? 3 : car.Pit ? 0 : car.OtherClass ? 1 : 2;
        }

        private static int PositionKey(Car car)
        {
            return car.Row.Position > 0 ? car.Row.Position : int.MaxValue;
        }

        /// <summary>The point at "fraction" (0..1) of the line, "cross" away from it across the line (the line itself: _crossCentre).</summary>
        private Point LinePoint(double fraction, double cross)
        {
            double along = _alongPad + fraction * _length;
            return _vertical ? new Point(cross, _designSize.Height - along) : new Point(along, cross);
        }

        /// <summary>
        /// The angle of a point of the lap on the ring, in degrees clockwise from the top. Whole lap: start/finish at the chosen
        /// place, the lap runs in the chosen direction. Centred on me: the ring turns so that the player stays at the top.
        /// </summary>
        private double RingAngle(double pct, double playerPct, bool centred)
        {
            return centred ? _turn * TrackMapGeometry.Signed(pct, playerPct) * 360 : _startAngle + _turn * pct * 360;
        }

        /// <summary>The point at "angle" (degrees clockwise from the top) and "radius" from the middle of the ring.</summary>
        private Point RingPoint(double angle, double radius)
        {
            double rad = angle * Math.PI / 180;
            return new Point(_ringCentre.X + radius * Math.Sin(rad), _ringCentre.Y - radius * Math.Cos(rad));
        }

        /// <summary>The cars to draw, with their place on the line. Not sorted for drawing yet.</summary>
        private List<Car> Collect(out StandingsRow player, out bool hasPlayer, out bool centred)
        {
            StandingsRow[] rows = _snapshot.Rows ?? new StandingsRow[0];
            player = null;
            foreach (StandingsRow row in rows)
            {
                if (row.IsPlayer || row.CarIdx == _snapshot.PlayerCarIdx)
                {
                    player = row;
                    break;
                }
            }

            hasPlayer = player != null && player.TrackSurface != -1 && player.LapDistPct >= 0;
            centred = _options.Centred && hasPlayer;
            double range = _options.GetInt("Range") / 100.0;
            double playerPct = hasPlayer ? Shown(player) : 0;
            _shownPlayerPct = playerPct;
            bool classOnly = _options.Get("Show") == "Class" && player != null;
            bool hidePit = _options.Get("Pit") == "Hide";
            bool lapRings = _options.GetBool("LapRings") && (_snapshot.IsRace || _snapshot.IsSample);
            string label = _options.Get("Label");
            bool marksFastest = _options.GetBool("Fastest");

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
            if (label == "Auto")
            {
                label = !_snapshot.IsRace && !_snapshot.IsSample ? "Number" : classes ? "ClassPosition" : "Position";
            }

            List<Car> cars = new List<Car>();
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

                Point at;
                if (_ring)
                {
                    at = RingPoint(RingAngle(Shown(row), playerPct, centred), _ringRadius);
                }
                else
                {
                    double fraction;
                    if (!TrackMapGeometry.LineFraction(Shown(row), playerPct, centred, range, out fraction))
                    {
                        continue;
                    }

                    at = LinePoint(fraction, _crossCentre);
                }

                double classBest;
                // only the own class: the fastest lap of another class says nothing about your race (all classes without a player)
                bool isFastest = marksFastest && row.BestLap > 0 && (player == null || row.ClassId == player.ClassId)
                    && fastest.TryGetValue(row.ClassId, out classBest) && row.BestLap <= classBest;

                double distance = hasPlayer ? Math.Abs(TrackMapGeometry.Signed(row.LapDistPct, playerPct)) : 0;
                int lapRelation = 0;
                if (lapRings && hasPlayer && !isPlayer)
                {
                    lapRelation = (int)Math.Round((row.Progress - player.Progress) - TrackMapGeometry.Signed(row.LapDistPct, playerPct));
                }

                string text = string.Empty;
                switch (label)
                {
                    case "Number":
                        text = row.Number ?? string.Empty;
                        break;
                    case "Position":
                        text = row.Position > 0 ? row.Position.ToString(CultureInfo.InvariantCulture) : string.Empty;
                        break;
                    case "ClassPosition":
                        text = row.ClassPosition > 0 ? row.ClassPosition.ToString(CultureInfo.InvariantCulture) : string.Empty;
                        break;
                }

                cars.Add(new Car
                {
                    Row = row,
                    At = at,
                    Fastest = isFastest,
                    Player = isPlayer,
                    Pit = pit,
                    OtherClass = otherClass,
                    Distance = distance,
                    LapRelation = lapRelation,
                    Colour = StandingsOverlay.ClassBrush(row.ClassColor),
                    Text = text
                });
            }

            return cars;
        }

        private void DrawCar(DrawingContext dc, Car car)
        {
            Point centre = car.At;
            double r = car.Player ? _playerRadius : _dot / 2;       // outer edge of the circle
            // one ring at most: the player's own (violet with the fastest lap), else a lap ring, else the fastest lap
            Pen ringPen = car.Player ? (car.Fastest ? FastestPen : PlayerRingPen)
                : car.LapRelation > 0 ? LapAheadPen
                : car.LapRelation < 0 ? LapBehindPen
                : car.Fastest ? FastestPen : null;
            // disc: the coloured area; inner: the part of it the number may use
            double disc = ringPen != null ? r - EdgeWidth - RingWidth + 0.5 : r;
            double inner = ringPen != null ? r - EdgeWidth - RingWidth : r - GlassWidth;
            if (car.Pit)
            {
                dc.PushOpacity(PitOpacity);
            }
            else
            {
                dc.DrawEllipse(car.Colour, null, centre, disc, disc);
            }

            if (ringPen != null)
            {
                // the ring sits flush on the circle: a thin dark edge outside, the coloured band inside of it
                dc.DrawEllipse(null, EdgePen, centre, r - EdgeWidth / 2, r - EdgeWidth / 2);
                dc.DrawEllipse(null, ringPen, centre, r - EdgeWidth - RingWidth / 2, r - EdgeWidth - RingWidth / 2);
            }
            else
            {
                // glass: the own class colour, a little darker and see-through, as a soft rim
                dc.DrawEllipse(null, GlassPen(car.Colour), centre, r - GlassWidth / 2, r - GlassWidth / 2);
            }

            if (car.Pit)
            {
                dc.DrawEllipse(null, HollowPen(car.Colour), centre, inner - 1, inner - 1);       // hollow: the class colour only as a ring
            }

            if (car.Text.Length > 0)
            {
                Brush ink = car.Pit ? TextBrush : StandingsOverlay.ReadableOn(car.Colour);
                Figure number = Number(car.Text, 2 * inner, ink);
                // The figures are drawn as their outline (geometry), not as text: DrawText snaps the baseline to a whole pixel,
                // a geometry is placed exactly. Its middle (the bounds of the drawn figures) sits on the middle of the circle.
                dc.PushTransform(new TranslateTransform(centre.X, centre.Y));
                dc.DrawGeometry(ink, null, number.Shape);
                dc.Pop();
            }

            if (car.Pit)
            {
                dc.Pop();
            }
        }

        /// <summary>The glass edge: the class colour a little darker and see-through, so it reads as a soft rim of the same colour.</summary>
        private Pen GlassPen(Brush colour)
        {
            SolidColorBrush solid = colour as SolidColorBrush;
            Color c = solid != null ? solid.Color : Color.FromRgb(0x3F, 0xA9, 0xF5);
            Pen pen;
            if (!_outlinePens.TryGetValue(c, out pen))
            {
                pen = MakePen(StandingsOverlay.Frozen(GlassAlpha, (byte)(c.R * GlassShade), (byte)(c.G * GlassShade), (byte)(c.B * GlassShade)), GlassWidth);
                _outlinePens[c] = pen;
            }

            return pen;
        }

        private Pen HollowPen(Brush colour)
        {
            Pen pen;
            if (!_hollowPens.TryGetValue(colour, out pen))
            {
                pen = MakePen(colour, OutlineWidth);
                _hollowPens[colour] = pen;
            }

            return pen;
        }

        /// <summary>The figures for a disc of the given diameter; a long number is made smaller until it fits.</summary>
        private Figure Number(string text, double diameter, Brush ink)
        {
            string key = text + "|" + ((int)Math.Round(diameter * 10)).ToString(CultureInfo.InvariantCulture);
            Figure figure;
            if (!_texts.TryGetValue(key, out figure))
            {
                double size = diameter * NumberSize;
                FormattedText formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, size, ink, _dpi);
                double room = diameter * NumberRoom;
                if (formatted.Width > room)
                {
                    size = size * room / formatted.Width;
                    formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, size, ink, _dpi);
                }

                Geometry shape = formatted.BuildGeometry(new Point(0, 0));
                Rect bounds = shape.Bounds;
                if (!bounds.IsEmpty)
                {
                    shape.Transform = new TranslateTransform(-(bounds.X + bounds.Width / 2), -(bounds.Y + bounds.Height / 2));
                }

                shape.Freeze();
                figure = new Figure { Shape = shape };
                _texts[key] = figure;
            }

            return figure;
        }

        private static Pen MakeLinePen(Brush brush, double thickness)
        {
            Pen pen = new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            pen.Freeze();
            return pen;
        }

        private static Pen MakePen(Brush brush, double thickness)
        {
            Pen pen = new Pen(brush, thickness);
            pen.Freeze();
            return pen;
        }
    }
}
