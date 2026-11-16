using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Shiftline.Plugin
{
    /// <summary>
    /// A track layout fitted into the window: the points turned to the usual orientation (or north up), scaled so the longer
    /// side has the chosen size, and the geometry to draw. Places a car by its LapDistPct (point i at i/N, linear in between)
    /// and a car in the pit along the pit lane.
    /// </summary>
    internal sealed class TrackShape
    {
        private readonly Point[] _points;
        private readonly bool _closed;
        private readonly Point[] _pit;
        private readonly double[] _pitDistance;     // distance from the first pit point
        private readonly double _pitEntry;
        private readonly double _pitExit;
        private readonly Dictionary<double, Geometry> _roads = new Dictionary<double, Geometry>();

        private TrackShape(Point[] points, bool closed, Point[] pit, double pitEntry, double pitExit, Size size)
        {
            _points = points;
            _closed = closed;
            _pit = pit;
            _pitEntry = pitEntry;
            _pitExit = pitExit;
            Size = size;
            Line = Polyline(points, closed);
            if (pit != null)
            {
                Pit = Polyline(pit, false);
                _pitDistance = new double[pit.Length];
                for (int i = 1; i < pit.Length; i++)
                {
                    _pitDistance[i] = _pitDistance[i - 1] + (pit[i] - pit[i - 1]).Length;
                }
            }
        }

        public TrackLayout Layout { get; private set; }

        /// <summary>The window size: the track plus the padding on every side.</summary>
        public Size Size { get; private set; }

        public Geometry Line { get; private set; }

        /// <summary>The pit lane, null when the layout has none.</summary>
        public Geometry Pit { get; private set; }

        /// <summary>
        /// Builds the shape. "usual": turn by presentRotation (when the layout has it), otherwise north up. "size": the longer
        /// side of the track in pixels; "pad": free space around it (for the circles).
        /// </summary>
        public static TrackShape Build(TrackLayout layout, bool usual, double size, double pad)
        {
            double angle = usual && layout.PresentRotation.HasValue ? layout.PresentRotation.Value : 0;
            double rad = angle * Math.PI / 180;
            double cos = Math.Cos(rad);
            double sin = Math.Sin(rad);
            Func<Point, Point> turn = p =>
            {
                // metres with y north to the screen (y down), then turned clockwise
                double x = p.X;
                double y = -p.Y;
                return new Point(x * cos - y * sin, x * sin + y * cos);
            };

            Point[] track = Array.ConvertAll(layout.Points, p => turn(p));
            Point[] pit = layout.PitPoints == null ? null : Array.ConvertAll(layout.PitPoints, p => turn(p));

            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (Point p in pit == null ? track : Concat(track, pit))
            {
                minX = Math.Min(minX, p.X);
                minY = Math.Min(minY, p.Y);
                maxX = Math.Max(maxX, p.X);
                maxY = Math.Max(maxY, p.Y);
            }

            double width = Math.Max(1, maxX - minX);
            double height = Math.Max(1, maxY - minY);
            double scale = size / Math.Max(width, height);
            Func<Point, Point> fit = p => new Point((p.X - minX) * scale + pad, (p.Y - minY) * scale + pad);
            Point[] fittedTrack = Array.ConvertAll(track, p => fit(p));
            Point[] fittedPit = pit == null ? null : Array.ConvertAll(pit, p => fit(p));
            Size window = new Size(Math.Ceiling(width * scale + 2 * pad), Math.Ceiling(height * scale + 2 * pad));
            return new TrackShape(fittedTrack, layout.Closed, fittedPit, layout.PitEntryPct, layout.PitExitPct, window) { Layout = layout };
        }

        /// <summary>The point of the track at a LapDistPct.</summary>
        public Point At(double pct)
        {
            int n = _points.Length;
            double f;
            if (_closed)
            {
                f = Wrap(pct) * n;
            }
            else
            {
                f = Math.Max(0, Math.Min(1, pct)) * (n - 1);
            }

            int i = (int)Math.Floor(f);
            double t = f - i;
            Point a = _points[_closed ? i % n : Math.Min(i, n - 1)];
            Point b = _points[_closed ? (i + 1) % n : Math.Min(i + 1, n - 1)];
            return a + (b - a) * t;
        }

        /// <summary>The unit vector across the track (perpendicular to the driving direction) at a LapDistPct.</summary>
        public Vector Across(double pct)
        {
            Vector along = At(pct + 0.002) - At(pct - 0.002);
            if (along.Length < 1e-6)
            {
                return new Vector(0, 1);
            }

            along.Normalize();
            return new Vector(-along.Y, along.X);
        }

        /// <summary>Where a car in the pit is: along the pit lane when its LapDistPct lies between pit entry and pit exit.</summary>
        public bool PitAt(double pct, out Point at)
        {
            at = new Point();
            if (_pit == null)
            {
                return false;
            }

            double span = Wrap(_pitExit - _pitEntry);
            double into = Wrap(pct - _pitEntry);
            if (span <= 0 || into > span)
            {
                return false;
            }

            double target = into / span * _pitDistance[_pitDistance.Length - 1];
            for (int i = 1; i < _pit.Length; i++)
            {
                if (_pitDistance[i] >= target)
                {
                    double piece = _pitDistance[i] - _pitDistance[i - 1];
                    double t = piece > 0 ? (target - _pitDistance[i - 1]) / piece : 0;
                    at = _pit[i - 1] + (_pit[i] - _pit[i - 1]) * t;
                    return true;
                }
            }

            at = _pit[_pit.Length - 1];
            return true;
        }

        /// <summary>The outline of a band of the given width along the track: drawn with a thin pen it gives two parallel lines.</summary>
        public Geometry Road(double width)
        {
            Geometry road;
            if (!_roads.TryGetValue(width, out road))
            {
                Pen pen = new Pen(Brushes.Black, width) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat };
                // the widened line is one piece per segment; the outline of their union leaves only the two edges
                road = Line.GetWidenedPathGeometry(pen, 0.1, ToleranceType.Absolute).GetOutlinedPathGeometry(0.1, ToleranceType.Absolute);
                road.Freeze();
                _roads[width] = road;
            }

            return road;
        }

        private static Geometry Polyline(Point[] points, bool closed)
        {
            StreamGeometry geometry = new StreamGeometry();
            using (StreamGeometryContext context = geometry.Open())
            {
                context.BeginFigure(points[0], false, closed);
                Point[] rest = new Point[points.Length - 1];
                Array.Copy(points, 1, rest, 0, rest.Length);
                context.PolyLineTo(rest, true, true);
            }

            geometry.Freeze();
            return geometry;
        }

        private static IEnumerable<Point> Concat(Point[] a, Point[] b)
        {
            foreach (Point p in a)
            {
                yield return p;
            }

            foreach (Point p in b)
            {
                yield return p;
            }
        }

        private static double Wrap(double pct)
        {
            return pct - Math.Floor(pct);
        }
    }
}
