using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Shiftline.Plugin
{
    /// <summary>
    /// The standings overlay: an optional header line and one row per car, with the columns and their looks chosen on the
    /// Standings page. The visual tree is built once per option change; an update only changes texts and colours. The content
    /// scales with the window size and keeps its proportions.
    /// </summary>
    internal sealed class StandingsOverlay : Border
    {
        private const double RowHeight = 24;
        private const double HeaderHeight = 26;
        private const double LabelHeight = 18;
        private const int AroundPlayer = 3;           // rows kept for the player and the cars right before and after
        private const int MaxDelta = 5;

        private static readonly Brush TextBrush = Frozen(0xFF, 0xE8, 0xEC, 0xF1);
        private static readonly Brush MutedBrush = Frozen(0xFF, 0x8E, 0x98, 0xA6);
        private static readonly Brush PanelBrush = Frozen(0xE6, 0x0E, 0x12, 0x19);
        private static readonly Brush HeaderBrush = Frozen(0xFF, 0x16, 0x1C, 0x26);
        private static readonly Brush OddRowBrush = Frozen(0x14, 0xFF, 0xFF, 0xFF);
        private static readonly Brush PlayerRowBrush = Frozen(0x55, 0x3F, 0xA9, 0xF5);
        private static readonly Brush PurpleBrush = Frozen(0xFF, 0xC0, 0x7C, 0xFF);
        private static readonly Brush GreenBrush = Frozen(0xFF, 0x5E, 0xE0, 0x7A);
        private static readonly Brush LossBrush = Frozen(0xFF, 0xF0, 0x5A, 0x4A);
        private static readonly Brush PitBrush = Frozen(0xFF, 0xFF, 0xB0, 0x20);
        private static readonly Brush NumberBoxBrush = Frozen(0xFF, 0x05, 0x06, 0x08);
        private static readonly Brush DarkText = Frozen(0xFF, 0x10, 0x12, 0x16);
        private static readonly Brush WetBrush = Frozen(0xFF, 0x3F, 0xA9, 0xF5);
        private static readonly Brush PillBrush = Frozen(0x1E, 0xFF, 0xFF, 0xFF);
        private static readonly Geometry ChevronUp = FrozenGeometry("M 0.5,5.5 L 4,2 L 7.5,5.5");
        private static readonly Geometry ChevronDown = FrozenGeometry("M 0.5,2 L 4,5.5 L 7.5,2");
        private static readonly Dictionary<string, Brush> LicenceBrushes = new Dictionary<string, Brush>(StringComparer.OrdinalIgnoreCase);

        private readonly bool _interactive;
        private StandingsOptions _options;
        private TextBlock _header;
        private Border[] _rowBackgrounds = new Border[0];
        private Border[,] _cellBoxes = new Border[0, 0];
        private TextBlock[,] _cells = new TextBlock[0, 0];
        private Ellipse[] _tyreRings = new Ellipse[0];
        private Path[] _tyreLetters = new Path[0];
        private static readonly Dictionary<string, Geometry> LetterShapes = new Dictionary<string, Geometry>();
        private TextBlock[,] _deltas = new TextBlock[0, 0];
        private Path[] _gainArrows = new Path[0];
        private TranslateTransform[] _rowShifts = new TranslateTransform[0];
        private TextBlock[] _pitValues = new TextBlock[0];
        private TextBlock[] _irChanges = new TextBlock[0];
        private Path[] _irArrows = new Path[0];
        private Brush _rowBrush;
        private Brush _oddRowBrush;
        private double _rowPitch = RowHeight;
        private Dictionary<int, int> _lastSlots = new Dictionary<int, int>();
        private double _designWidth;

        public StandingsOverlay() : this(false)
        {
        }

        /// <param name="interactive">For the preview on the settings page: a click on a column raises ColumnClicked.</param>
        public StandingsOverlay(bool interactive)
        {
            _interactive = interactive;
            Background = Brushes.Transparent;
            ApplyOptions(StandingsOptions.Load());
        }

        /// <summary>Raised in interactive mode with the key of the clicked column.</summary>
        public event Action<string> ColumnClicked;

        /// <summary>The window size that fits the current design.</summary>
        public Size DesignSize
        {
            get
            {
                double height = _options.Rows * Pitch(_options) + FixedHeight(_options);
                return new Size((_designWidth + 16) * _options.Scale, height * _options.Scale);
            }
        }

        /// <summary>How many rows fit into a window of this height (used while the window is pulled longer or shorter).</summary>
        public int RowsForHeight(double height)
        {
            double inner = height / _options.Scale - FixedHeight(_options);
            return Math.Max(3, Math.Min(40, (int)Math.Round(inner / Pitch(_options))));
        }

        /// <summary>Height of one row plus the gap below it.</summary>
        private static double Pitch(StandingsOptions options)
        {
            return RowHeight + Math.Max(0, options.GetInt("RowGap"));
        }

        private static double FixedHeight(StandingsOptions options)
        {
            return (options.Header ? HeaderHeight + 4 : 0) + (options.Labels ? LabelHeight : 0) + 12;
        }

        /// <summary>Rebuilds the rows and columns. Call on the UI thread after the options changed.</summary>
        public void ApplyOptions(StandingsOptions options)
        {
            _options = options;
            List<StandingsColumn> columns = options.Columns;
            int rows = options.Rows;
            int numStyle = options.GetInt("Num.Style");
            bool smallCaps = options.Get("Name.Case") == "SmallCaps";
            int span = Math.Max(1, columns.Count);
            int gap = Math.Max(0, options.GetInt("RowGap"));
            byte alpha = (byte)Math.Round(255 * Math.Max(0, Math.Min(100, options.GetInt("Opacity"))) / 100.0);
            _rowPitch = RowHeight + gap;

            // Without a gap the rows sit on one panel and every second row is lighter. With a gap every row is its own dark bar
            // and the space between them stays see-through.
            Brush panelBrush = gap > 0 ? null : Frozen(alpha, 0x0E, 0x12, 0x19);
            _rowBrush = gap > 0 ? Frozen(alpha, 0x0E, 0x12, 0x19) : null;
            _oddRowBrush = gap > 0 ? Frozen(alpha, 0x1A, 0x20, 0x2A) : OddRowBrush;

            Grid grid = new Grid();
            _designWidth = 0;
            foreach (StandingsColumn column in columns)
            {
                double width = options.WidthOf(column);
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
                _designWidth += width;
            }

            _designWidth = Math.Max(_designWidth, 120);
            int nextRow = 0;
            if (options.Header)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HeaderHeight + 4) });
                _header = new TextBlock { Foreground = TextBrush, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis };
                Border headerBox = new Border { Background = HeaderBrush, CornerRadius = new CornerRadius(5), Height = HeaderHeight, VerticalAlignment = VerticalAlignment.Top, Child = _header };
                Grid.SetColumnSpan(headerBox, span);
                grid.Children.Add(headerBox);
                nextRow++;
            }
            else
            {
                _header = null;
            }

            if (options.Labels)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(LabelHeight) });
                for (int c = 0; c < columns.Count; c++)
                {
                    TextBlock label = NewText(columns[c].Align);
                    label.Text = columns[c].ShortLabel.ToUpperInvariant();
                    label.FontSize = 10;
                    label.Foreground = MutedBrush;
                    label.Margin = new Thickness(5, 0, 5, 0);
                    Grid.SetRow(label, nextRow);
                    Grid.SetColumn(label, c);
                    grid.Children.Add(label);
                }

                nextRow++;
            }

            int firstRow = nextRow;
            _rowBackgrounds = new Border[rows];
            _cellBoxes = new Border[rows, columns.Count];
            _cells = new TextBlock[rows, columns.Count];
            _tyreRings = new Ellipse[rows];
            _tyreLetters = new Path[rows];
            _gainArrows = new Path[rows];
            _rowShifts = new TranslateTransform[rows];
            _lastSlots = new Dictionary<int, int>();
            _deltas = new TextBlock[rows, MaxDelta];
            _pitValues = new TextBlock[rows];
            _irChanges = new TextBlock[rows];
            _irArrows = new Path[rows];
            for (int r = 0; r < rows; r++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(_rowPitch) });
                TranslateTransform shift = new TranslateTransform();
                _rowShifts[r] = shift;
                Border background = new Border { CornerRadius = new CornerRadius(gap > 0 ? 4 : 3), Margin = new Thickness(0, 0, 0, gap), Background = r % 2 == 1 ? _oddRowBrush : _rowBrush, RenderTransform = shift };
                Grid.SetRow(background, firstRow + r);
                Grid.SetColumnSpan(background, span);
                grid.Children.Add(background);
                _rowBackgrounds[r] = background;

                for (int c = 0; c < columns.Count; c++)
                {
                    string key = columns[c].Key;
                    TextBlock text = NewText(columns[c].Align);
                    UIElement content = text;
                    Thickness margin = new Thickness(2, 3, 2, 3);
                    Thickness padding = new Thickness(3, 0, 3, 0);
                    if (key == "Class")
                    {
                        content = null;                     // the box itself is the coloured bar
                        margin = new Thickness(1, 4, 3, 4);
                        padding = new Thickness(0);
                    }
                    else if (key == "Num")
                    {
                        text.FontWeight = numStyle % 2 == 1 ? FontWeights.Bold : FontWeights.Normal;
                        text.FontStyle = numStyle % 4 >= 2 ? FontStyles.Italic : FontStyles.Normal;
                    }
                    else if (key == "Name" && smallCaps)
                    {
                        Typography.SetCapitals(text, FontCapitals.SmallCaps);
                    }
                    else if (key == "Gain")
                    {
                        content = BuildGain(r, text);
                    }
                    else if (key == "Tyre")
                    {
                        content = BuildTyre(r, text);
                    }
                    else if (key == "Delta")
                    {
                        content = BuildDelta(r, text);
                    }
                    else if (key == "Pit")
                    {
                        content = BuildPit(r, text);
                    }
                    else if (key == "iR")
                    {
                        content = BuildIRating(r, text);
                    }

                    if (gap > 0)
                    {
                        margin = new Thickness(margin.Left, margin.Top, margin.Right, margin.Bottom + gap);
                    }

                    Border box = new Border
                    {
                        Margin = margin,
                        Padding = padding,
                        CornerRadius = new CornerRadius(key == "Class" ? 2 : 3),
                        Child = content,
                        Background = _interactive ? Brushes.Transparent : null,
                        RenderTransform = shift
                    };
                    if (_interactive)
                    {
                        box.Cursor = Cursors.Hand;
                        box.Tag = key;
                        box.MouseLeftButtonUp += (sender, args) =>
                        {
                            Action<string> handler = ColumnClicked;
                            if (handler != null)
                            {
                                handler((string)((Border)sender).Tag);
                            }
                        };
                    }

                    Grid.SetRow(box, firstRow + r);
                    Grid.SetColumn(box, c);
                    grid.Children.Add(box);
                    _cellBoxes[r, c] = box;
                    _cells[r, c] = text;
                }
            }

            // Fixed size: the scale comes from the "Size" choice, not from the window. A longer window only adds rows.
            if (options.Header && _header != null && gap > 0)
            {
                ((Border)_header.Parent).Background = Frozen(alpha, 0x16, 0x1C, 0x26);
            }

            Border panel = new Border
            {
                Background = panelBrush,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 6, 8, 6),
                Width = _designWidth + 16,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Child = grid,
                ClipToBounds = true,
                LayoutTransform = new ScaleTransform(options.Scale, options.Scale)
            };
            TextElement.SetFontFamily(panel, new FontFamily(options.Get("Font")));
            TextElement.SetFontWeight(panel, WeightOf(options.Get("Weight")));
            Child = panel;
        }

        private UIElement BuildGain(int r, TextBlock text)
        {
            Path arrow = new Path { Width = 8, Height = 8, StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 3, 0) };
            _gainArrows[r] = arrow;
            StackPanel panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(arrow);
            panel.Children.Add(text);
            return panel;
        }

        private static TextBlock NewText(int align)
        {
            return new TextBlock
            {
                Foreground = TextBrush,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = align == StandingsColumn.Right ? HorizontalAlignment.Right : align == StandingsColumn.Center ? HorizontalAlignment.Center : HorizontalAlignment.Left,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
        }

        private UIElement BuildTyre(int r, TextBlock laps)
        {
            // The letter is drawn as a shape centred on its own outline, so it sits exactly in the middle of the ring whatever
            // the font's line spacing is.
            Ellipse ring = new Ellipse { Width = 16, Height = 16, StrokeThickness = 2.5, Stroke = MutedBrush, Fill = NumberBoxBrush };
            Path letter = new Path { Fill = TextBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Stretch = Stretch.None };
            Grid icon = new Grid { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
            icon.Children.Add(ring);
            icon.Children.Add(letter);
            _tyreRings[r] = ring;
            _tyreLetters[r] = letter;
            laps.Foreground = MutedBrush;
            laps.HorizontalAlignment = HorizontalAlignment.Right;
            laps.Margin = new Thickness(0, 0, 3, 0);
            Grid row = new Grid();
            row.Children.Add(icon);
            row.Children.Add(laps);
            return new Border { Background = PillBrush, CornerRadius = new CornerRadius(9), Padding = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Stretch, Child = row };
        }

        private static Geometry LetterShape(string code)
        {
            Geometry shape;
            if (LetterShapes.TryGetValue(code, out shape))
            {
                return shape;
            }

            FormattedText text = new FormattedText(code, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 9.5, Brushes.White, 1.0);
            Geometry glyph = text.BuildGeometry(new Point(0, 0));
            Rect bounds = glyph.Bounds;
            GeometryGroup moved = new GeometryGroup { Transform = new TranslateTransform(-bounds.X, -bounds.Y) };   // outline starts at 0,0
            moved.Children.Add(glyph);
            shape = moved;
            shape.Freeze();
            LetterShapes[code] = shape;
            return shape;
        }

        /// <summary>The pit cell: the state word on the left, its value (time in the pit lane, lap of the last stop) on the right.</summary>
        private UIElement BuildPit(int r, TextBlock word)
        {
            TextBlock value = NewText(StandingsColumn.Right);
            _pitValues[r] = value;
            word.HorizontalAlignment = HorizontalAlignment.Left;
            Grid grid = new Grid();
            grid.Children.Add(word);
            grid.Children.Add(value);
            return grid;
        }

        /// <summary>The iRating with its estimated change, on one shared background so they read as one.</summary>
        private UIElement BuildIRating(int r, TextBlock rating)
        {
            Path arrow = new Path { Width = 8, Height = 8, StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 2, 0) };
            TextBlock change = NewText(StandingsColumn.Left);
            _irArrows[r] = arrow;
            _irChanges[r] = change;
            rating.HorizontalAlignment = HorizontalAlignment.Left;
            StackPanel right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            right.Children.Add(arrow);
            right.Children.Add(change);
            Grid panel = new Grid();
            panel.Children.Add(rating);
            panel.Children.Add(right);
            return new Border { Background = PillBrush, CornerRadius = new CornerRadius(4), Padding = new Thickness(4, 0, 4, 0), HorizontalAlignment = HorizontalAlignment.Stretch, Child = panel };
        }

        private static FontWeight WeightOf(string weight)
        {
            return weight == "Bold" ? FontWeights.Bold : weight == "SemiBold" ? FontWeights.SemiBold : FontWeights.Normal;
        }

        private UIElement BuildDelta(int r, TextBlock first)
        {
            StackPanel panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            int count = Math.Max(1, Math.Min(MaxDelta, _options.GetInt("Delta.Count")));
            for (int k = 0; k < count; k++)
            {
                TextBlock text = k == 0 ? first : NewText(StandingsColumn.Right);
                text.Width = 28;
                text.TextAlignment = TextAlignment.Right;
                panel.Children.Add(text);
                _deltas[r, k] = text;
            }

            return panel;
        }

        public void Update(StandingsSnapshot snapshot)
        {
            List<StandingsColumn> columns = _options.Columns;
            if (_header != null)
            {
                SetText(_header, HeaderText(snapshot));
            }

            StandingsRow[] all = snapshot.Rows ?? new StandingsRow[0];
            Context context = new Context(all, snapshot.IsRace, _options);
            List<Entry> shown = BuildEntries(all, _options.Rows, _options.Get("Classes"));
            Dictionary<int, int> slots = new Dictionary<int, int>();
            double animation = AnimationSeconds(snapshot.IsReplay);
            for (int r = 0; r < _options.Rows; r++)
            {
                Entry entry = r < shown.Count ? shown[r] : null;
                StandingsRow row = entry == null ? null : entry.Row;
                Brush rowBrush = row == null ? null : row.IsPlayer ? PlayerRowBrush : (r % 2 == 1 ? _oddRowBrush : _rowBrush);
                if (!ReferenceEquals(_rowBackgrounds[r].Background, rowBrush))
                {
                    _rowBackgrounds[r].Background = rowBrush;
                }

                // A car that moved to another slot slides from its old place to the new one.
                if (row != null)
                {
                    slots[row.CarIdx] = r;
                    int previous;
                    if (animation > 0 && _lastSlots.TryGetValue(row.CarIdx, out previous) && previous != r)
                    {
                        DoubleAnimation slide = new DoubleAnimation((previous - r) * RowHeight, 0, TimeSpan.FromSeconds(animation))
                        {
                            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                        };
                        slide.From = (previous - r) * _rowPitch;
                        _rowShifts[r].BeginAnimation(TranslateTransform.YProperty, slide);
                    }
                }

                for (int c = 0; c < columns.Count; c++)
                {
                    Fill(r, c, columns[c].Key, entry, context);
                }
            }

            _lastSlots = slots;
        }

        private double AnimationSeconds(bool replay)
        {
            switch (_options.Get(replay ? "Anim.Replay" : "Anim.Car"))
            {
                case "Fast":
                    return 0.25;
                case "Normal":
                    return 0.5;
                case "Slow":
                    return 0.9;
                default:
                    return 0;
            }
        }

        /// <summary>One shown row: the car, the position to show, and the cars its gap and interval refer to.</summary>
        private sealed class Entry
        {
            public StandingsRow Row;
            public int Position;            // overall, or in the class when classes are split
            public int StartPosition;       // the same kind of position at the start, 0 = unknown
            public StandingsRow Leader;     // gap reference (null for the leader itself)
            public StandingsRow Ahead;      // interval reference (null for the leader itself)
        }

        /// <summary>
        /// The rows to show. Mixed: the overall order. Own: only the player's class. Grouped: the player's class first, then the
        /// leaders of every other class, each block separated by an empty row. Inside the player's block the leaders are shown,
        /// and when the player is further back the last rows show the player and the cars right before and after.
        /// </summary>
        private static List<Entry> BuildEntries(StandingsRow[] all, int count, string mode)
        {
            StandingsRow player = Array.Find(all, row => row.IsPlayer);
            List<List<StandingsRow>> classes = new List<List<StandingsRow>>();
            Dictionary<int, List<StandingsRow>> byClass = new Dictionary<int, List<StandingsRow>>();
            foreach (StandingsRow row in all)
            {
                List<StandingsRow> list;
                if (!byClass.TryGetValue(row.ClassId, out list))
                {
                    list = new List<StandingsRow>();
                    byClass[row.ClassId] = list;
                    classes.Add(list);
                }

                list.Add(row);
            }

            List<Entry> entries = new List<Entry>(count);
            bool split = classes.Count > 1 && mode != "Mixed";
            if (!split)
            {
                AddBlock(entries, all, count, false);
                return entries;
            }

            List<StandingsRow> own = player != null ? byClass[player.ClassId] : classes[0];
            if (mode == "Own")
            {
                AddBlock(entries, own.ToArray(), count, true);
                return entries;
            }

            // Grouped: two rows per other class plus an empty row before each block; the player's class gets the rest.
            const int OtherRows = 2;
            int others = classes.Count - 1;
            int ownRows = Math.Max(3, count - others * (OtherRows + 1));
            AddBlock(entries, own.ToArray(), ownRows, true);
            foreach (List<StandingsRow> other in classes)
            {
                if (ReferenceEquals(other, own) || entries.Count + 2 > count)
                {
                    continue;
                }

                entries.Add(null);
                AddBlock(entries, other.ToArray(), Math.Min(OtherRows, count - entries.Count), true);
            }

            return entries;
        }

        private static void AddBlock(List<Entry> entries, StandingsRow[] rows, int count, bool inClass)
        {
            foreach (int i in Choose(rows, count))
            {
                StandingsRow row = rows[i];
                entries.Add(new Entry
                {
                    Row = row,
                    Position = inClass ? row.ClassPosition : row.Position,
                    StartPosition = inClass ? row.StartClassPosition : row.StartPosition,
                    Leader = i > 0 ? rows[0] : null,
                    Ahead = i > 0 ? rows[i - 1] : null
                });
            }
        }

        // ---------------------------------------------------------------- cells

        private sealed class Context
        {
            public Context(StandingsRow[] rows, bool race, StandingsOptions options)
            {
                Rows = rows;
                Race = race;
                Options = options;
                SessionBest = double.MaxValue;
                foreach (StandingsRow row in rows)
                {
                    if (row.BestLap > 0 && row.BestLap < SessionBest)
                    {
                        SessionBest = row.BestLap;
                    }

                    if (row.IsPlayer)
                    {
                        Player = row;
                    }
                }
            }

            public StandingsRow[] Rows { get; }
            public bool Race { get; }
            public StandingsOptions Options { get; }
            public double SessionBest { get; }
            public StandingsRow Player { get; }

            public string Mode
            {
                get { return Race ? "Race" : "PQ"; }
            }
        }

        private void Fill(int r, int c, string column, Entry entry, Context context)
        {
            StandingsRow row = entry == null ? null : entry.Row;
            Border box = _cellBoxes[r, c];
            TextBlock text = _cells[r, c];
            StandingsOptions options = context.Options;
            Brush background = _interactive ? Brushes.Transparent : null;
            Brush foreground = TextBrush;
            string value = string.Empty;
            if (column == "Tyre")
            {
                FillTyre(r, row, options);
            }

            if (column == "Delta")
            {
                FillDelta(r, row, context);
                return;
            }

            if (column == "iR" && row == null)
            {
                FillIRatingChange(r, null, false);
            }

            if (row != null)
            {
                switch (column)
                {
                    case "Pos":
                        value = entry.Position > 0 ? entry.Position.ToString(CultureInfo.InvariantCulture) : "-";
                        foreground = MutedBrush;
                        break;
                    case "Class":
                        background = LicenceBrush(row.ClassColor) ?? MutedBrush;
                        break;
                    case "Gain":
                        if (entry.StartPosition > 0 && entry.Position > 0 && entry.StartPosition != entry.Position)
                        {
                            int gained = entry.StartPosition - entry.Position;
                            value = Math.Abs(gained).ToString(CultureInfo.InvariantCulture);
                            foreground = gained > 0 ? GreenBrush : LossBrush;
                        }

                        break;
                    case "Num":
                        value = row.Number ?? string.Empty;
                        if (options.GetInt("Num.Style") >= 4)
                        {
                            background = NumberBoxBrush;
                        }

                        break;
                    case "Name":
                        value = FormatName(row.Name, options.Get("Name.Format"));
                        if (options.Get("Name.Case") == "Upper")
                        {
                            value = value.ToUpperInvariant();
                        }

                        break;
                    case "Make":
                        value = row.Make ?? string.Empty;
                        foreground = MutedBrush;
                        break;
                    case "Lic":
                        FormatLicence(row, options.Get("Lic.Mode"), out value, out background, out foreground);
                        break;
                    case "iR":
                        value = FormatIRating(row.IRating, options.Get("iR.Mode"));
                        foreground = MutedBrush;
                        FillIRatingChange(r, row, options.GetBool("iR.Gain"));
                        break;
                    case "Gap":
                    case "Int":
                        if (options.GetBool(column + ".Show" + context.Mode))
                        {
                            StandingsRow reference = column == "Gap" ? entry.Leader : entry.Ahead;
                            if (reference == null)
                            {
                                value = column.ToUpperInvariant();      // the leader of a block shows what the column means
                                foreground = MutedBrush;
                            }
                            else
                            {
                                value = Gap(row, context, reference, options.GetInt(column + ".Dec" + context.Mode));
                            }
                        }

                        break;
                    case "Last":
                        value = Lap(row.LastLap, options.GetInt("Last.Dec" + context.Mode));
                        if (row.LastLap > 0 && Math.Abs(row.LastLap - context.SessionBest) < 0.0005)
                        {
                            foreground = PurpleBrush;
                        }
                        else if (row.LastLap > 0 && Math.Abs(row.LastLap - row.BestLap) < 0.0005)
                        {
                            foreground = GreenBrush;
                        }

                        break;
                    case "Tyre":
                        value = options.GetBool("Tyre.Laps") && !string.IsNullOrEmpty(row.TireType) ? row.StintLaps.ToString(CultureInfo.InvariantCulture) : string.Empty;
                        foreground = MutedBrush;
                        break;
                    case "Stint":
                        value = row.StintLaps > 0 ? row.StintLaps.ToString(CultureInfo.InvariantCulture) : string.Empty;
                        foreground = MutedBrush;
                        break;
                    case "Pit":
                        string detail;
                        Brush pitBackground;
                        FormatPit(row, options.Get("Pit.Mode") != "Plain", out value, out detail, out pitBackground, out foreground);
                        if (pitBackground != null)
                        {
                            background = pitBackground;
                        }

                        SetText(_pitValues[r], detail);
                        if (!ReferenceEquals(_pitValues[r].Foreground, foreground))
                        {
                            _pitValues[r].Foreground = foreground;
                        }

                        break;
                }
            }

            if (column == "Gain")
            {
                SetArrow(_gainArrows[r], entry, foreground);
            }

            if (_interactive && background == null)
            {
                background = Brushes.Transparent;       // keeps the whole cell clickable in the preview
            }

            SetText(text, value);
            if (column != "Tyre" && !ReferenceEquals(text.Foreground, foreground))
            {
                text.Foreground = foreground;
            }

            if (!ReferenceEquals(box.Background, background))
            {
                box.Background = background;
            }
        }

        private static void SetArrow(Path arrow, Entry entry, Brush brush)
        {
            if (arrow == null)
            {
                return;
            }

            Geometry shape = null;
            if (entry != null && entry.StartPosition > 0 && entry.Position > 0 && entry.StartPosition != entry.Position)
            {
                shape = entry.StartPosition > entry.Position ? ChevronUp : ChevronDown;
            }

            if (!ReferenceEquals(arrow.Data, shape))
            {
                arrow.Data = shape;
            }

            if (!ReferenceEquals(arrow.Stroke, brush))
            {
                arrow.Stroke = brush;
            }
        }

        private void FillTyre(int r, StandingsRow row, StandingsOptions options)
        {
            Ellipse ring = _tyreRings[r];
            Path letter = _tyreLetters[r];
            if (ring == null)
            {
                return;
            }

            string type = row == null ? string.Empty : row.TireType ?? string.Empty;
            Visibility visible = type.Length > 0 ? Visibility.Visible : Visibility.Hidden;
            UIElement pill = ((FrameworkElement)((FrameworkElement)ring.Parent).Parent).Parent as UIElement;   // ring > icon > grid > pill
            if (pill != null && pill.Visibility != visible)
            {
                pill.Visibility = visible;
            }

            if (type.Length == 0)
            {
                return;
            }

            string code;
            Brush colour;
            TyreLook(type, out code, out colour);
            Geometry shape = LetterShape(code);
            if (!ReferenceEquals(letter.Data, shape))
            {
                letter.Data = shape;
            }

            if (!ReferenceEquals(ring.Stroke, colour))
            {
                ring.Stroke = colour;
            }
        }

        /// <summary>F1-style colours: soft red, medium yellow, hard white, intermediate green, wet blue.</summary>
        private static void TyreLook(string type, out string code, out Brush colour)
        {
            string t = type.ToLowerInvariant();
            if (t.Contains("soft"))
            {
                code = "S";
                colour = LossBrush;
            }
            else if (t.Contains("medium"))
            {
                code = "M";
                colour = PitBrush;
            }
            else if (t.Contains("hard") || t.Contains("dry"))
            {
                code = t.Contains("dry") ? "D" : "H";
                colour = TextBrush;
            }
            else if (t.Contains("inter"))
            {
                code = "I";
                colour = GreenBrush;
            }
            else if (t.Contains("wet") || t.Contains("rain"))
            {
                code = "W";
                colour = WetBrush;
            }
            else
            {
                code = type.Substring(0, 1).ToUpperInvariant();
                colour = MutedBrush;
            }
        }

        /// <summary>Time gained (green) or lost (red) against the player, per recent lap, newest first.</summary>
        private void FillDelta(int r, StandingsRow row, Context context)
        {
            int count = Math.Max(1, Math.Min(MaxDelta, _options.GetInt("Delta.Count")));
            StandingsRow player = context.Player;
            for (int k = 0; k < count; k++)
            {
                TextBlock text = _deltas[r, k];
                if (text == null)
                {
                    continue;
                }

                string value = string.Empty;
                Brush brush = MutedBrush;
                if (row != null && player != null && !row.IsPlayer && row.RecentLaps != null && player.RecentLaps != null
                    && k < row.RecentLaps.Length && k < player.RecentLaps.Length)
                {
                    double delta = row.RecentLaps[k] - player.RecentLaps[k];   // positive: the other car was slower, you gained
                    value = Math.Abs(delta).ToString("0.0", CultureInfo.InvariantCulture);
                    brush = delta > 0.05 ? GreenBrush : delta < -0.05 ? LossBrush : MutedBrush;
                }

                SetText(text, value);
                if (!ReferenceEquals(text.Foreground, brush))
                {
                    text.Foreground = brush;
                }
            }
        }

        private static string FormatName(string name, string format)
        {
            string[] parts = (name ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                return name ?? string.Empty;
            }

            string first = parts[0];
            string last = parts[parts.Length - 1];
            switch (format)
            {
                case "Full":
                    return string.Join(" ", parts);
                case "InitialLast":
                    return first.Substring(0, 1) + ". " + last;
                case "Last":
                    return last;
                default:
                    return first + " " + last;
            }
        }

        private static void FormatLicence(StandingsRow row, string mode, out string value, out Brush background, out Brush foreground)
        {
            // iRacing gives for example "A 3.48": the class letter and the safety rating.
            string[] parts = (row.License ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string letter = parts.Length > 0 ? parts[0] : string.Empty;
            double rating = 0;
            bool hasRating = parts.Length > 1 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out rating);
            Brush classBrush = LicenceBrush(row.LicenseColor);
            switch (mode)
            {
                case "Short":
                    value = letter + (hasRating ? (Math.Floor(rating * 10) / 10).ToString("0.0", CultureInfo.InvariantCulture) : string.Empty);
                    break;
                case "Round":
                    value = letter + (hasRating ? Math.Floor(rating).ToString("0", CultureInfo.InvariantCulture) : string.Empty);
                    break;
                case "Letter":
                    value = letter;
                    break;
                case "Number":
                    value = hasRating ? rating.ToString("0.00", CultureInfo.InvariantCulture) : string.Empty;
                    background = null;
                    foreground = classBrush ?? MutedBrush;
                    return;
                default:
                    value = letter + (hasRating ? rating.ToString("0.00", CultureInfo.InvariantCulture) : string.Empty);
                    break;
            }

            background = classBrush;
            foreground = classBrush == null ? MutedBrush : ReadableOn(classBrush);
        }

        private static string FormatIRating(int rating, string mode)
        {
            if (rating <= 0)
            {
                return string.Empty;
            }

            switch (mode)
            {
                case "K1":
                    return (Math.Floor(rating / 100.0) / 10).ToString("0.0", CultureInfo.InvariantCulture) + "k";
                case "K0":
                    return Math.Round(rating / 1000.0).ToString("0", CultureInfo.InvariantCulture) + "k";
                default:
                    return rating.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static void FormatPit(StandingsRow row, bool withInfo, out string value, out string detail, out Brush background, out Brush foreground)
        {
            background = null;
            foreground = MutedBrush;
            value = string.Empty;
            detail = string.Empty;
            if (row.Towing)
            {
                value = "TOW";
                background = LossBrush;
                foreground = Brushes.White;
            }
            else if (row.OnPitRoad)
            {
                value = "PIT";
                detail = withInfo && row.PitLaneSeconds >= 0 ? MinutesSeconds(row.PitLaneSeconds) : string.Empty;
                background = PitBrush;
                foreground = DarkText;
            }
            else if (row.TrackSurface < 0)
            {
                value = "OUT";
            }
            else if (withInfo && row.PitLap > 0)
            {
                value = "PIT";
                detail = "L" + row.PitLap.ToString(CultureInfo.InvariantCulture);    // lap of the last stop
            }
        }

        private void FillIRatingChange(int r, StandingsRow row, bool show)
        {
            TextBlock change = _irChanges[r];
            Path arrow = _irArrows[r];
            if (change == null)
            {
                return;
            }

            FrameworkElement group = change.Parent as FrameworkElement;                         // change > right group > grid > pill
            FrameworkElement grid = group == null ? null : group.Parent as FrameworkElement;
            UIElement pill = grid == null ? null : grid.Parent as UIElement;
            Visibility pillVisible = row != null && row.IRating > 0 ? Visibility.Visible : Visibility.Hidden;
            if (pill != null && pill.Visibility != pillVisible)
            {
                pill.Visibility = pillVisible;
            }

            bool has = show && row != null && row.HasIRatingChange && row.IRatingChange != 0;
            Brush brush = has && row.IRatingChange > 0 ? GreenBrush : LossBrush;
            SetText(change, has ? Math.Abs(row.IRatingChange).ToString(CultureInfo.InvariantCulture) : string.Empty);
            Geometry shape = has ? (row.IRatingChange > 0 ? ChevronUp : ChevronDown) : null;
            if (!ReferenceEquals(arrow.Data, shape))
            {
                arrow.Data = shape;
            }

            arrow.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (!ReferenceEquals(change.Foreground, brush))
            {
                change.Foreground = brush;
                arrow.Stroke = brush;
            }
        }

        private static string MinutesSeconds(double seconds)
        {
            int whole = (int)seconds;
            return (whole / 60).ToString(CultureInfo.InvariantCulture) + ":" + (whole % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Gap to another car. In a race from iRacing's F2Time (race time behind the leader), with laps when a full lap or more
        /// behind; otherwise the difference of the best laps.
        /// </summary>
        private static string Gap(StandingsRow row, Context context, StandingsRow reference, int decimals)
        {
            if (reference == null)
            {
                return string.Empty;
            }

            string format = Format(decimals);
            if (context.Race)
            {
                int laps = (int)Math.Floor(reference.Progress - row.Progress);
                if (laps >= 1)
                {
                    return "+" + laps.ToString(CultureInfo.InvariantCulture) + "L";
                }

                double gap = row.F2Time - reference.F2Time;
                return row.F2Time > 0 && gap >= 0 ? Truncate(gap, decimals).ToString(format, CultureInfo.InvariantCulture) : "-";
            }

            if (row.BestLap <= 0 || reference.BestLap <= 0)
            {
                return "-";
            }

            return Truncate(row.BestLap - reference.BestLap, decimals).ToString(format, CultureInfo.InvariantCulture);
        }

        private static string Format(int decimals)
        {
            return decimals <= 1 ? "0.0" : decimals == 2 ? "0.00" : "0.000";
        }

        /// <summary>Cuts instead of rounding, as timing screens do (1.746 shows as 1.74 and 1.7).</summary>
        private static double Truncate(double value, int decimals)
        {
            double factor = Math.Pow(10, Math.Max(1, Math.Min(3, decimals)));
            return Math.Floor(value * factor) / factor;
        }

        private static string HeaderText(StandingsSnapshot snapshot)
        {
            List<string> parts = new List<string>();
            string type = snapshot.SessionType ?? string.Empty;
            parts.Add(type.IndexOf("Race", StringComparison.OrdinalIgnoreCase) >= 0 ? "R"
                : type.IndexOf("Qual", StringComparison.OrdinalIgnoreCase) >= 0 ? "Q"
                : type.Length > 0 ? "P" : "-");
            if (!string.IsNullOrEmpty(snapshot.CarName))
            {
                parts.Add(snapshot.CarName);
            }

            StandingsRow[] rows = snapshot.Rows ?? new StandingsRow[0];
            if (snapshot.LapsTotal > 0)
            {
                int leaderLap = rows.Length > 0 ? Math.Max(0, rows[0].LapsCompleted) + 1 : 0;
                parts.Add("Lap " + Math.Min(leaderLap, snapshot.LapsTotal) + "/" + snapshot.LapsTotal);
            }
            else if (snapshot.TimeRemaining > 0 && snapshot.TimeRemaining < 360000)
            {
                parts.Add(TimeSpan.FromSeconds(snapshot.TimeRemaining).ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture));
            }

            double temp;
            string tempText = (snapshot.TrackTemp ?? string.Empty).Split(' ')[0];
            if (double.TryParse(tempText, NumberStyles.Float, CultureInfo.InvariantCulture, out temp))
            {
                parts.Add("Track " + temp.ToString("0", CultureInfo.InvariantCulture) + "\u00B0C");
            }

            if (snapshot.StrengthOfField > 0)
            {
                parts.Add("SOF " + snapshot.StrengthOfField.ToString(CultureInfo.InvariantCulture));
            }

            string limit = snapshot.IncidentLimit ?? string.Empty;
            parts.Add("Inc " + snapshot.Incidents + (limit.Length > 0 && char.IsDigit(limit[0]) ? "/" + limit : string.Empty));
            if (snapshot.IsSample)
            {
                parts.Add("Preview");
            }

            return string.Join("   ", parts);
        }

        /// <summary>The leaders; when the player is not among them, the last rows show the player and the cars around.</summary>
        private static List<int> Choose(StandingsRow[] rows, int count)
        {
            List<int> list = new List<int>(count);
            int player = Array.FindIndex(rows, row => row.IsPlayer);
            if (player < count || rows.Length <= count || count <= AroundPlayer)
            {
                for (int i = 0; i < rows.Length && i < count; i++)
                {
                    list.Add(i);
                }

                return list;
            }

            for (int i = 0; i < count - AroundPlayer; i++)
            {
                list.Add(i);
            }

            int start = Math.Min(player - 1, rows.Length - AroundPlayer);
            for (int i = start; i < start + AroundPlayer && i < rows.Length; i++)
            {
                list.Add(i);
            }

            return list;
        }

        private static string Lap(double seconds, int decimals)
        {
            if (seconds <= 0)
            {
                return "-";
            }

            int minutes = (int)(seconds / 60);
            double rest = Truncate(seconds - minutes * 60, decimals);
            string format = decimals <= 1 ? "00.0" : decimals == 2 ? "00.00" : "00.000";
            return minutes + ":" + rest.ToString(format, CultureInfo.InvariantCulture);
        }

        private static void SetText(TextBlock text, string value)
        {
            if (!string.Equals(text.Text, value, StringComparison.Ordinal))
            {
                text.Text = value;
            }
        }

        /// <summary>The licence colour as iRacing reports it ("0x0153db"); null when it cannot be read.</summary>
        private static Brush LicenceBrush(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            Brush brush;
            if (LicenceBrushes.TryGetValue(value, out brush))
            {
                return brush;
            }

            string hex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value.Substring(2) : value;
            int rgb;
            brush = hex.Length == 6 && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb)
                ? Frozen(0xFF, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)
                : null;
            LicenceBrushes[value] = brush;
            return brush;
        }

        private static Brush ReadableOn(Brush background)
        {
            SolidColorBrush solid = background as SolidColorBrush;
            if (solid == null)
            {
                return TextBrush;
            }

            Color c = solid.Color;
            double luminance = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
            return luminance > 140 ? DarkText : Brushes.White;
        }

        private static Geometry FrozenGeometry(string data)
        {
            Geometry geometry = Geometry.Parse(data);
            geometry.Freeze();
            return geometry;
        }

        private static Brush Frozen(byte a, byte r, byte g, byte b)
        {
            SolidColorBrush brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}
