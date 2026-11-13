using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Media;

namespace Shiftline.Plugin
{
    /// <summary>
    /// The Track Map: the cars as numbered circles on a line (horizontal or vertical), either the whole lap or the part around the
    /// player. Drawn directly (OnRender) from the latest snapshot; the size follows from the options (DesignSize), there is no
    /// other scaling. A circle carries the class colour and a dark outline, the bold number sits inside. The player is larger with a
    /// light ring, a lap ring is the same band in red or blue; both sit flush on the circle. The player is always on top. Where circles overlap the important ones stay on top: cars close to the player over far
    /// ones, the own class over the others, cars in the pit below everything.
    /// </summary>
    internal sealed class TrackMapOverlay : FrameworkElement
    {
        private const double EdgeMargin = 6;
        private const double PlayerZoom = 1.25;
        private const double OutlineWidth = 2;      // the dark outline of a plain circle
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
        private static readonly Pen OutlinePen = MakePen(OutlineBrush, OutlineWidth);
        private static readonly Pen EdgePen = MakePen(OutlineBrush, EdgeWidth);
        private static readonly Pen PlayerRingPen = MakePen(PlayerRingBrush, RingWidth);
        private static readonly Pen LapAheadPen = MakePen(LapAheadBrush, RingWidth);
        private static readonly Pen LapBehindPen = MakePen(LapBehindBrush, RingWidth);

        private sealed class Figure
        {
            public Geometry Shape;               // the outlines of the figures, their middle at (0, 0)
        }

        private readonly Dictionary<string, Figure> _texts = new Dictionary<string, Figure>();
        private readonly Dictionary<Brush, Pen> _hollowPens = new Dictionary<Brush, Pen>();
        private TrackMapOptions _options;
        private StandingsSnapshot _snapshot = StandingsSnapshot.Empty;
        private Typeface _typeface;
        private Brush _plate;
        private double _round;
        private Pen _linePen;
        private Pen _roadPen;
        private double _roadGap;                    // distance between the two lines of the road (centre to centre)
        private Brush _stripBrush;
        private string _startLine = "Off";
        private DispatcherTimer _flowTimer;         // runs while the start/finish line is the moving Shiftline colours
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

        private sealed class Car
        {
            public StandingsRow Row;
            public double Along;                    // position along the line, in pixels from the left (horizontal) or from the top (vertical)
            public double Cross;                    // the other coordinate (the line)
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
            ApplyOptions(TrackMapOptions.Load());
            Loaded += (s, e) => UpdateFlowTimer();
            Unloaded += (s, e) => UpdateFlowTimer();
        }

        public bool Vertical
        {
            get { return _vertical; }
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
            _startLine = options.Get("StartLine");
            UpdateFlowTimer();
            _texts.Clear();

            _alongPad = EdgeMargin + _reach;
            _crossCentre = EdgeMargin + _reach;
            _designSize = _vertical
                ? new Size(2 * _crossCentre, _length + 2 * _alongPad)
                : new Size(_length + 2 * _alongPad, 2 * _crossCentre);

            InvalidateMeasure();
            InvalidateVisual();
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

        /// <summary>The Shiftline colours move along the start/finish line: redraw now and then, only while that line is shown.</summary>
        private void UpdateFlowTimer()
        {
            bool wanted = _startLine == "Shiftline" && IsLoaded;
            if (wanted)
            {
                if (_flowTimer == null)
                {
                    _flowTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(50) };
                    _flowTimer.Tick += (s, e) => InvalidateVisual();
                }

                _flowTimer.Start();
            }
            else if (_flowTimer != null)
            {
                _flowTimer.Stop();
            }
        }

        public void Update(StandingsSnapshot snapshot)
        {
            _snapshot = snapshot ?? StandingsSnapshot.Empty;
            InvalidateVisual();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            return _designSize;
        }

        // ================================================================ drawing

        protected override void OnRender(DrawingContext dc)
        {
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            if (Math.Abs(dpi - _dpi) > 0.001)
            {
                _dpi = dpi;
                _texts.Clear();
            }

            dc.DrawRoundedRectangle(_plate, null, new Rect(0, 0, _designSize.Width, _designSize.Height), _round, _round);

            DrawTrack(dc);

            StandingsRow player;
            bool hasPlayer;
            bool centred;
            List<Car> cars = Collect(out player, out hasPlayer, out centred);
            double range = _options.GetInt("Range") / 100.0;
            double playerPct = hasPlayer ? player.LapDistPct : 0;

            if (_startLine != "Off")
            {
                List<double> marks = new List<double>();
                if (centred)
                {
                    double startFraction;
                    if (TrackMapGeometry.LineFraction(0, playerPct, true, range, out startFraction))
                    {
                        marks.Add(startFraction);
                    }
                }
                else
                {
                    marks.Add(0);
                    marks.Add(1);
                }

                foreach (double markFraction in marks)
                {
                    DrawStartLine(dc, markFraction);
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

        /// <summary>The start/finish line: one line across the track, plain or in the moving Shiftline colours.</summary>
        private void DrawStartLine(DrawingContext dc, double fraction)
        {
            Point centre = LinePoint(fraction, _crossCentre);
            double half = _reach + 2;
            Rect line = _vertical
                ? new Rect(centre.X - half, centre.Y - StartLineWidth / 2, 2 * half, StartLineWidth)
                : new Rect(centre.X - StartLineWidth / 2, centre.Y - half, StartLineWidth, 2 * half);
            dc.DrawRectangle(StartBrush(centre, 2 * half), null, line);
        }

        private static readonly Brush StartWhite = StandingsOverlay.Frozen(0xFF, 0xF4, 0xF6, 0xF9);
        private static readonly Brush StartGrey = StandingsOverlay.Frozen(0xFF, 0x8E, 0x98, 0xA6);
        private static readonly Brush StartBlack = StandingsOverlay.Frozen(0xFF, 0x0E, 0x12, 0x19);
        private static readonly int[] ShiftlineColours = { 0x01ABFF, 0x903AA1, 0xFF8C00, 0xD60000 };

        private Brush StartBrush(Point centre, double length)
        {
            switch (_startLine)
            {
                case "White":
                    return StartWhite;
                case "Black":
                    return StartBlack;
                case "Grey":
                    return StartGrey;
            }

            // the four Shiftline colours as a repeating gradient along the line, shifted a little further every frame
            double phase = (DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond) % (long)(FlowSeconds * 1000) / (FlowSeconds * 1000);
            double shift = phase * length;
            Point from = _vertical ? new Point(centre.X - length / 2 - shift, centre.Y) : new Point(centre.X, centre.Y - length / 2 - shift);
            Point to = _vertical ? new Point(from.X + length, from.Y) : new Point(from.X, from.Y + length);
            LinearGradientBrush brush = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = from, EndPoint = to, SpreadMethod = GradientSpreadMethod.Repeat };
            for (int i = 0; i <= ShiftlineColours.Length; i++)
            {
                int c = ShiftlineColours[i % ShiftlineColours.Length];
                brush.GradientStops.Add(new GradientStop(Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c), i / (double)ShiftlineColours.Length));
            }

            brush.Freeze();
            return brush;
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
            double playerPct = hasPlayer ? player.LapDistPct : 0;
            bool classOnly = _options.Get("Show") == "Class" && player != null;
            bool hidePit = _options.Get("Pit") == "Hide";
            bool lapRings = _options.GetBool("LapRings") && (_snapshot.IsRace || _snapshot.IsSample);
            string label = _options.Get("Label");

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

                double fraction;
                if (!TrackMapGeometry.LineFraction(row.LapDistPct, playerPct, centred, range, out fraction))
                {
                    continue;
                }

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

                Point at = LinePoint(fraction, _crossCentre);
                cars.Add(new Car
                {
                    Row = row,
                    Along = _vertical ? at.Y : at.X,
                    Cross = _vertical ? at.X : at.Y,
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
            Point centre = new Point(_vertical ? car.Cross : car.Along, _vertical ? car.Along : car.Cross);
            double r = car.Player ? _playerRadius : _dot / 2;       // outer edge of the circle
            Pen ringPen = car.Player ? PlayerRingPen : car.LapRelation > 0 ? LapAheadPen : car.LapRelation < 0 ? LapBehindPen : null;
            double inner = ringPen != null ? r - EdgeWidth - RingWidth : r - OutlineWidth;       // the coloured disc inside the bands
            if (car.Pit)
            {
                dc.PushOpacity(PitOpacity);
            }
            else
            {
                dc.DrawEllipse(car.Colour, null, centre, inner + 0.5, inner + 0.5);
            }

            if (ringPen != null)
            {
                // the ring sits flush on the circle: a thin dark edge outside, the coloured band inside of it
                dc.DrawEllipse(null, EdgePen, centre, r - EdgeWidth / 2, r - EdgeWidth / 2);
                dc.DrawEllipse(null, ringPen, centre, r - EdgeWidth - RingWidth / 2, r - EdgeWidth - RingWidth / 2);
            }
            else
            {
                dc.DrawEllipse(null, OutlinePen, centre, r - OutlineWidth / 2, r - OutlineWidth / 2);
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
