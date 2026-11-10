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
        private const double FooterHeight = 26;
        private static readonly Geometry ClockShape = FrozenGeometry("M 6,1 A 5,5 0 1 1 5.99,1 Z M 6,3.2 L 6,6 L 8,7.2");
        private static readonly Geometry SunShape = FrozenGeometry("M 8.5,6 A 2.5,2.5 0 1 1 8.49,5.99 Z M 6,0.6 L 6,1.9 M 6,10.1 L 6,11.4 M 0.6,6 L 1.9,6 M 10.1,6 L 11.4,6 M 2.2,2.2 L 3.1,3.1 M 8.9,8.9 L 9.8,9.8 M 2.2,9.8 L 3.1,8.9 M 8.9,3.1 L 9.8,2.2");
        private static readonly Geometry MoonShape = FrozenGeometry("M 8.2,1.2 A 5,5 0 1 0 10.8,8.2 A 4,4 0 0 1 8.2,1.2 Z");
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
        private static readonly Brush SubtleFill = Frozen(0x1E, 0xFF, 0xFF, 0xFF);
        private static readonly Brush LightFill = Frozen(0x3A, 0xFF, 0xFF, 0xFF);
        private static readonly Brush WhiteFill = Frozen(0xFF, 0xF9, 0xF9, 0xF9);
        // Best lap and average lap get their own text colours so they read apart from the lap time. Both are light enough to stay
        // readable on the grey backgrounds; on the white background they turn dark like every other value.
        private static readonly Brush BestLapBrush = Frozen(0xFF, 0xC4, 0xCB, 0xD6);
        private static readonly Brush AverageBrush = Frozen(0xFF, 0x9F, 0xD0, 0xFF);
        private static readonly Brush DarkGreenBrush = Frozen(0xFF, 0x1B, 0x8A, 0x3A);
        private static readonly Brush DarkRedBrush = Frozen(0xFF, 0xC6, 0x28, 0x28);
        private Brush[] _columnFills = new Brush[0];
        private bool[] _columnLight = new bool[0];
        private double _round = 1;                  // 1 = rounded boxes, 0 = square
        private double _deltaWidth = 28;
        private Border _headerBox;
        private Border _footerBox;
        private Path _simIcon;
        private Path _clockIcon;
        private TextBlock _simTime;
        private TextBlock _clockTime;
        private TextBlock _footLast;
        private TextBlock _footDelta;
        private TextBlock _footBest;
        private TextBlock _footSession;
        private Path _footDeltaArrow;
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
            return (options.Header ? HeaderHeight + 4 : 0) + (options.Footer ? FooterHeight + 4 : 0) + (options.Labels ? LabelHeight : 0) + 12;
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
            FontFamily overlayFont = OverlayFonts.Get(options.Get("Font"));
            FontWeight overlayWeight = WeightOf(options.Get("Weight"));
            _deltaWidth = 28;
            foreach (StandingsColumn column in columns)
            {
                // At least the planned width, more when the longest value would not fit in the chosen font.
                string font = options.Get(column.Key + ".Font");
                string weight = options.Get(column.Key + ".Weight");
                FontFamily family = string.IsNullOrEmpty(font) || font == "Default" ? overlayFont : OverlayFonts.Get(font);
                FontWeight bold = string.IsNullOrEmpty(weight) || weight == "Default" ? overlayWeight : WeightOf(weight);
                if (column.Key == "Num" && (string.IsNullOrEmpty(weight) || weight == "Default") && options.GetInt("Num.Style") % 2 == 1)
                {
                    bold = FontWeights.Bold;
                }

                double width = Math.Max(options.WidthOf(column), Required(column.Key, options, family, bold));
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Ceiling(width)) });
                _designWidth += Math.Ceiling(width);
            }

            _designWidth = Math.Max(_designWidth, 120);
            _round = options.Get("Corners") == "Square" ? 0 : 1;
            _columnFills = new Brush[columns.Count];
            _columnLight = new bool[columns.Count];
            for (int c = 0; c < columns.Count; c++)
            {
                string fill = options.Get(columns[c].Key + ".Bg");
                _columnFills[c] = fill == "White" ? WhiteFill : fill == "Light" ? LightFill : fill == "Subtle" ? SubtleFill : null;
                _columnLight[c] = fill == "White";
            }

            int nextRow = 0;
            if (options.Header)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HeaderHeight + 4) });
                _header = new TextBlock { Foreground = TextBrush, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis };

                // Right side: the time of day in the simulation (sun or moon) and the time on this PC (clock).
                _simIcon = NewIcon();
                _simTime = new TextBlock { Foreground = TextBrush, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 10, 0) };
                _clockIcon = NewIcon();
                _clockIcon.Data = ClockShape;
                _clockTime = new TextBlock { Foreground = TextBrush, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 8, 0) };
                StackPanel right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                if (options.GetBool("H.SimTime"))
                {
                    right.Children.Add(_simIcon);
                    right.Children.Add(_simTime);
                }

                if (options.GetBool("H.Clock"))
                {
                    right.Children.Add(_clockIcon);
                    right.Children.Add(_clockTime);
                }

                Grid headerGrid = new Grid();
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetColumn(right, 1);
                headerGrid.Children.Add(_header);
                headerGrid.Children.Add(right);
                Border headerBox = new Border { Background = HeaderBrush, CornerRadius = new CornerRadius(5 * _round), Height = HeaderHeight, VerticalAlignment = VerticalAlignment.Top, Child = headerGrid };
                _headerBox = headerBox;
                Grid.SetColumnSpan(headerBox, span);
                grid.Children.Add(headerBox);
                nextRow++;
            }
            else
            {
                _header = null;
                _headerBox = null;
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
                Border background = new Border { CornerRadius = new CornerRadius((gap > 0 ? 4 : 3) * _round), Margin = new Thickness(0, 0, 0, gap), Background = r % 2 == 1 ? _oddRowBrush : _rowBrush, RenderTransform = shift };
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
                        content = BuildTyre(r, text, _columnFills[c]);
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
                        content = BuildIRating(r, text, _columnFills[c]);
                    }

                    if (gap > 0)
                    {
                        margin = new Thickness(margin.Left, margin.Top, margin.Right, margin.Bottom + gap);
                    }



                    Border box = new Border
                    {
                        Margin = margin,
                        Padding = padding,
                        CornerRadius = new CornerRadius((key == "Class" ? 2 : 3) * _round),
                        Child = content,
                        Background = _interactive ? Brushes.Transparent : null,
                        RenderTransform = shift
                    };
                    // Font and weight of this column, when chosen apart from the whole overlay (inherited by everything in the cell).
                    string font = options.Get(key + ".Font");
                    if (!string.IsNullOrEmpty(font) && font != "Default")
                    {
                        TextElement.SetFontFamily(box, OverlayFonts.Get(font));
                    }

                    string weight = options.Get(key + ".Weight");
                    if (!string.IsNullOrEmpty(weight) && weight != "Default")
                    {
                        TextElement.SetFontWeight(box, WeightOf(weight));
                        text.ClearValue(TextBlock.FontWeightProperty);
                    }

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

            // Footer: own last lap, live delta to own best, own best, best lap of the session.
            _footerBox = null;
            if (options.Footer)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(FooterHeight + 4) });
                Grid footer = new Grid { Margin = new Thickness(8, 0, 8, 0) };
                List<FrameworkElement> items = new List<FrameworkElement>();
                if (options.GetBool("F.Last"))
                {
                    items.Add(FooterItem("LAST", out _footLast));
                }

                if (options.GetBool("F.Delta"))
                {
                    items.Add(DeltaItem(out _footDelta, out _footDeltaArrow));
                }

                if (options.GetBool("F.Best"))
                {
                    items.Add(FooterItem("BEST", out _footBest));
                }

                if (options.GetBool("F.Session"))
                {
                    items.Add(FooterItem("SESSION", out _footSession));
                }

                for (int i = 0; i < items.Count; i++)
                {
                    footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    items[i].HorizontalAlignment = i == 0 ? HorizontalAlignment.Left : i == items.Count - 1 ? HorizontalAlignment.Right : HorizontalAlignment.Center;
                    Grid.SetColumn(items[i], i);
                    footer.Children.Add(items[i]);
                }

                _footerBox = new Border { Background = HeaderBrush, CornerRadius = new CornerRadius(5 * _round), Height = FooterHeight, VerticalAlignment = VerticalAlignment.Bottom, Child = footer };
                Grid.SetRow(_footerBox, firstRow + rows);
                Grid.SetColumnSpan(_footerBox, span);
                grid.Children.Add(_footerBox);
            }

            // Fixed size: the scale comes from the "Size" choice, not from the window. A longer window only adds rows.
            if (gap > 0)
            {
                Brush bar = Frozen(alpha, 0x16, 0x1C, 0x26);
                if (_headerBox != null)
                {
                    _headerBox.Background = bar;
                }

                if (_footerBox != null)
                {
                    _footerBox.Background = bar;
                }
            }

            Border panel = new Border
            {
                Background = panelBrush,
                CornerRadius = new CornerRadius(8 * _round),
                Padding = new Thickness(8, 6, 8, 6),
                Width = _designWidth + 16,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Child = grid,
                ClipToBounds = true,
                LayoutTransform = new ScaleTransform(options.Scale, options.Scale)
            };
            TextElement.SetFontFamily(panel, OverlayFonts.Get(options.Get("Font")));
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

        private UIElement BuildTyre(int r, TextBlock laps, Brush fill)
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
            laps.Margin = new Thickness(0, 0, 4, 0);
            Grid row = new Grid();
            row.Children.Add(icon);
            row.Children.Add(laps);
            return new Border { Background = fill, CornerRadius = new CornerRadius(9 * _round), Padding = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Stretch, Child = row };
        }

        internal static Geometry LetterShape(string code)
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
        private UIElement BuildIRating(int r, TextBlock rating, Brush fill)
        {
            Path arrow = new Path { Width = 8, Height = 8, StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 1, 0) };
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
            return new Border { Background = fill, CornerRadius = new CornerRadius(4 * _round), Padding = new Thickness(4, 0, 4, 0), HorizontalAlignment = HorizontalAlignment.Stretch, Child = panel };
        }

        /// <summary>
        /// The width a column needs so that its longest normal value is shown in full: for example a lap time of 1:59.999, a pit
        /// stop of 20:00 or a four-digit iRating with a three-digit change. Names keep their chosen maximum and are cut with "...".
        /// </summary>
        private double Required(string column, StandingsOptions options, FontFamily family, FontWeight weight)
        {
            const double Cell = 10;                         // margin and padding around every cell
            int decimals = Math.Max(options.GetInt("Last.DecRace"), options.GetInt("Last.DecPQ"));
            int gapDecimals = Math.Max(options.GetInt(column + ".DecRace"), options.GetInt(column + ".DecPQ"));
            switch (column)
            {
                case "Pos":
                    return Measure("00", family, weight) + Cell;
                case "Gain":
                    return Measure("00", family, weight) + 11 + Cell;
                case "Num":
                    return Measure("000", family, weight) + Cell + 4;
                case "Make":
                    return Measure("WWW", family, weight) + Cell;
                case "Lic":
                    return Measure("W0.00", family, weight) + Cell + 2;
                case "iR":
                    return options.GetBool("iR.Gain")
                        ? Measure("00000", family, weight) + 12 + Measure("000", family, weight) + 8 + Cell
                        : Measure("00000", family, weight) + 8 + Cell;
                case "Gap":
                case "Int":
                    return Measure(gapDecimals >= 3 ? "000.000" : gapDecimals == 2 ? "000.00" : "000.0", family, weight) + Cell;
                case "Last":
                case "Best":
                case "Avg":
                    return Measure(decimals >= 3 ? "0:00.000" : decimals == 2 ? "0:00.00" : "0:00.0", family, weight) + Cell;
                case "Tyre":
                    return options.GetBool("Tyre.Laps") ? 16 + 6 + Measure("00", family, weight) + 6 + Cell : 16 + 4 + Cell;
                case "Pit":
                    return options.Get("Pit.Mode") == "Plain"
                        ? Measure("TOW", family, weight) + Cell + 4
                        : Measure("PIT", family, weight) + 8 + Measure("00:00", family, weight) + Cell + 4;
                case "Stint":
                    return Measure("000", family, weight) + Cell;
                case "Delta":
                    _deltaWidth = Math.Max(28, Measure("00.0", family, weight) + 6);
                    return _deltaWidth * Math.Max(1, options.GetInt("Delta.Count")) + Cell;
                default:
                    return 0;
            }
        }

        private static double Measure(string text, FontFamily family, FontWeight weight)
        {
            FormattedText formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal), 13, Brushes.White, 1.0);
            return formatted.WidthIncludingTrailingWhitespace;
        }

        internal static FontWeight WeightOf(string weight)
        {
            return weight == "Bold" ? FontWeights.Bold : weight == "SemiBold" ? FontWeights.SemiBold : FontWeights.Normal;
        }

        private UIElement BuildDelta(int r, TextBlock first)
        {
            StackPanel panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            int count = Math.Max(1, Math.Min(MaxDelta, _options.GetInt("Delta.Count")));
            for (int k = 0; k < count; k++)
            {
                TextBlock text = k == 0 ? first : NewText(StandingsColumn.Center);
                text.Width = _deltaWidth;
                text.HorizontalAlignment = HorizontalAlignment.Center;
                text.TextAlignment = TextAlignment.Center;
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
                UpdateClocks(snapshot);
            }

            StandingsRow[] all = snapshot.Rows ?? new StandingsRow[0];
            Context context = new Context(all, snapshot.IsRace, _options) { HasDelta = snapshot.HasDelta, DeltaToBest = snapshot.DeltaToBest };
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
            UpdateFooter(context);
        }

        private static Path NewIcon()
        {
            return new Path { Width = 12, Height = 12, Stroke = MutedBrush, StrokeThickness = 1.3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, VerticalAlignment = VerticalAlignment.Center };
        }

        private static FrameworkElement FooterItem(string label, out TextBlock value)
        {
            TextBlock name = new TextBlock { Text = label, Foreground = MutedBrush, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            value = new TextBlock { Foreground = TextBrush, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            StackPanel item = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            item.Children.Add(name);
            item.Children.Add(value);
            return item;
        }

        /// <summary>The live delta: the same thin arrow as for places and iRating (up and green when faster), then the value.</summary>
        private static FrameworkElement DeltaItem(out TextBlock value, out Path arrow)
        {
            arrow = new Path { Width = 8, Height = 8, StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
            value = new TextBlock { Foreground = TextBrush, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            StackPanel item = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            item.Children.Add(arrow);
            item.Children.Add(value);
            return item;
        }

        /// <summary>Sim time of day with a sun (day) or moon (night), and the time on this PC with a clock.</summary>
        private void UpdateClocks(StandingsSnapshot snapshot)
        {
            if (_simTime != null)
            {
                double seconds = snapshot.TimeOfDay;
                bool known = seconds >= 0;
                Visibility shown = known ? Visibility.Visible : Visibility.Collapsed;      // games without a sim clock hide it
                if (_simTime.Visibility != shown)
                {
                    _simTime.Visibility = shown;
                    _simIcon.Visibility = shown;
                }

                SetText(_simTime, known ? TimeSpan.FromSeconds(seconds % 86400).ToString(@"hh\:mm", CultureInfo.InvariantCulture) : "-");
                bool day = !known || (seconds % 86400 >= 6 * 3600 && seconds % 86400 < 20 * 3600);
                Geometry shape = day ? SunShape : MoonShape;
                if (!ReferenceEquals(_simIcon.Data, shape))
                {
                    _simIcon.Data = shape;
                }
            }

            if (_clockTime != null)
            {
                SetText(_clockTime, DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture));
            }
        }

        private void UpdateFooter(Context context)
        {
            if (_footerBox == null)
            {
                return;
            }

            StandingsRow player = context.Player;
            int decimals = _options.GetInt("Last.Dec" + context.Mode);
            if (_footLast != null)
            {
                SetText(_footLast, player == null ? "-" : Lap(player.LastLap, decimals));
            }

            if (_footBest != null)
            {
                SetText(_footBest, player == null ? "-" : Lap(player.BestLap, decimals));
                Brush brush = player != null && player.BestLap > 0 && Math.Abs(player.BestLap - context.SessionBest) < 0.0005 ? PurpleBrush : GreenBrush;
                if (!ReferenceEquals(_footBest.Foreground, brush))
                {
                    _footBest.Foreground = brush;
                }
            }

            if (_footSession != null)
            {
                SetText(_footSession, context.SessionBest < double.MaxValue ? Lap(context.SessionBest, decimals) : "-");
                if (!ReferenceEquals(_footSession.Foreground, PurpleBrush))
                {
                    _footSession.Foreground = PurpleBrush;
                }
            }

            if (_footDelta != null)
            {
                // iRacing's live delta of the running lap to the player's best lap; negative is faster.
                string text = "-";
                Brush brush = MutedBrush;
                Geometry shape = null;
                if (context.HasDelta)
                {
                    double delta = context.DeltaToBest;
                    text = Math.Abs(delta).ToString("0.00", CultureInfo.InvariantCulture);
                    brush = delta < -0.005 ? GreenBrush : delta > 0.005 ? LossBrush : TextBrush;
                    shape = delta < -0.005 ? ChevronUp : delta > 0.005 ? ChevronDown : null;
                }

                SetText(_footDelta, text);
                if (!ReferenceEquals(_footDelta.Foreground, brush))
                {
                    _footDelta.Foreground = brush;
                    _footDeltaArrow.Stroke = brush;
                }

                if (!ReferenceEquals(_footDeltaArrow.Data, shape))
                {
                    _footDeltaArrow.Data = shape;
                }
            }
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

            if (mode == "ByClass")
            {
                // Every class as its own block, in the order of their best car. Each block shows as many cars as still fit;
                // the player's block keeps the player in view.
                for (int k = 0; k < classes.Count && entries.Count < count; k++)
                {
                    if (k > 0)
                    {
                        if (entries.Count + 2 > count)
                        {
                            break;
                        }

                        entries.Add(null);
                    }

                    int left = count - entries.Count;
                    int reserve = 0;
                    for (int later = k + 1; later < classes.Count; later++)
                    {
                        reserve += 3;                               // keep room for at least two cars of every later class
                    }

                    int take = Math.Max(Math.Min(3, left), Math.Min(classes[k].Count, left - reserve));
                    AddBlock(entries, classes[k].ToArray(), Math.Min(take, left), true);
                }

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
                BestAverage = double.MaxValue;
                AverageLaps = options.GetInt("Avg.Laps") == 3 ? 3 : 5;
                foreach (StandingsRow row in rows)
                {
                    if (row.BestLap > 0 && row.BestLap < SessionBest)
                    {
                        SessionBest = row.BestLap;
                    }

                    double average = AverageOf(row, AverageLaps);
                    if (average > 0 && average < BestAverage)
                    {
                        BestAverage = average;
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
            public double BestAverage { get; }        // the fastest average in the field, double.MaxValue when none
            public int AverageLaps { get; }
            public StandingsRow Player { get; }

            /// <summary>The average of the last laps, or 0 while a car has not completed that many laps yet.</summary>
            public static double AverageOf(StandingsRow row, int laps)
            {
                if (row.RecentLaps == null || row.RecentLaps.Length < laps)
                {
                    return 0;
                }

                double sum = 0;
                for (int k = 0; k < laps; k++)
                {
                    if (row.RecentLaps[k] <= 0)
                    {
                        return 0;
                    }

                    sum += row.RecentLaps[k];
                }

                return sum / laps;
            }
            public bool HasDelta { get; set; }
            public double DeltaToBest { get; set; }

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
                FillIRatingChange(r, null, false, false);
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
                        FillIRatingChange(r, row, options.GetBool("iR.Gain"), _columnLight[c]);
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
                    case "Best":
                        value = Lap(row.BestLap, options.GetInt("Last.Dec" + context.Mode));
                        foreground = BestLapBrush;
                        if (row.BestLap > 0 && Math.Abs(row.BestLap - context.SessionBest) < 0.0005)
                        {
                            foreground = PurpleBrush;
                        }

                        break;
                    case "Avg":
                        // green only for the fastest average in the field; no colour for the others
                        double average = Context.AverageOf(row, context.AverageLaps);
                        value = average > 0 ? Lap(average, options.GetInt("Last.Dec" + context.Mode)) : "-";
                        foreground = AverageBrush;
                        if (average > 0 && Math.Abs(average - context.BestAverage) < 0.0005)
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

            // The column's own background (for example behind the position), unless the cell has a coloured one already.
            if (background == null || ReferenceEquals(background, Brushes.Transparent))
            {
                bool inner = column == "iR" || column == "Tyre";                // these two draw their background themselves
                Brush fill = inner || row == null ? null : _columnFills[c];
                background = fill ?? background;
                if (row != null && _columnLight[c] && (fill != null || inner))
                {
                    // Dark text on the white background; green and red get darker so they stay readable.
                    foreground = ReferenceEquals(foreground, GreenBrush) ? DarkGreenBrush
                        : ReferenceEquals(foreground, LossBrush) ? DarkRedBrush
                        : ReferenceEquals(foreground, PurpleBrush) ? foreground
                        : DarkText;
                }
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
        internal static void TyreLook(string type, out string code, out Brush colour)
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

        internal static string FormatName(string name, string format)
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

        internal static string FormatIRating(int rating, string mode)
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

        private void FillIRatingChange(int r, StandingsRow row, bool show, bool onLight)
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
            Brush brush = has && row.IRatingChange > 0 ? (onLight ? DarkGreenBrush : GreenBrush) : (onLight ? DarkRedBrush : LossBrush);
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
        internal static double Truncate(double value, int decimals)
        {
            double factor = Math.Pow(10, Math.Max(1, Math.Min(3, decimals)));
            return Math.Floor(value * factor) / factor;
        }

        private string HeaderText(StandingsSnapshot snapshot)
        {
            List<string> parts = new List<string>();
            string type = snapshot.SessionType ?? string.Empty;
            if (_options.GetBool("H.Session"))
            {
                parts.Add(type.IndexOf("Race", StringComparison.OrdinalIgnoreCase) >= 0 ? "R"
                    : type.IndexOf("Qual", StringComparison.OrdinalIgnoreCase) >= 0 ? "Q"
                    : type.Length > 0 ? "P" : "-");
            }

            if (_options.GetBool("H.Track") && !string.IsNullOrEmpty(snapshot.TrackShort))
            {
                parts.Add(snapshot.TrackShort);
            }

            if (_options.GetBool("H.Car") && !string.IsNullOrEmpty(snapshot.CarName))
            {
                parts.Add(snapshot.CarName);
            }

            StandingsRow[] rows = snapshot.Rows ?? new StandingsRow[0];
            if (!_options.GetBool("H.Laps"))
            {
            }
            else if (snapshot.LapsTotal > 0)
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
            if (_options.GetBool("H.Temp") && double.TryParse(tempText, NumberStyles.Float, CultureInfo.InvariantCulture, out temp))
            {
                parts.Add("Track " + temp.ToString("0", CultureInfo.InvariantCulture) + "\u00B0C");
            }

            if (_options.GetBool("H.Sof") && snapshot.StrengthOfField > 0)
            {
                parts.Add("SOF " + snapshot.StrengthOfField.ToString(CultureInfo.InvariantCulture));
            }

            string limit = snapshot.IncidentLimit ?? string.Empty;
            if (_options.GetBool("H.Inc") && (snapshot.Features & StandingsFeatures.Incidents) != 0)
            {
                parts.Add("Inc " + snapshot.Incidents + (limit.Length > 0 && char.IsDigit(limit[0]) ? "/" + limit : string.Empty));
            }
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

        internal static string Lap(double seconds, int decimals)
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
        internal static Brush LicenceBrush(string value)
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

            string hex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value.Substring(2) : value.TrimStart('#');
            if (hex.Length == 8)
            {
                hex = hex.Substring(2);                     // #AARRGGBB from SimHub: the colour without its alpha
            }

            int rgb;
            brush = hex.Length == 6 && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb)
                ? Frozen(0xFF, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)
                : null;
            LicenceBrushes[value] = brush;
            return brush;
        }

        internal static Brush ReadableOn(Brush background)
        {
            SolidColorBrush solid = background as SolidColorBrush;
            if (solid == null)
            {
                return TextBrush;
            }

            Color c = solid.Color;
            double luminance = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
            return luminance > 120 ? DarkText : Brushes.White;     // green, yellow and orange get dark text; blue and red white
        }

        internal static Geometry FrozenGeometry(string data)
        {
            Geometry geometry = Geometry.Parse(data);
            geometry.Freeze();
            return geometry;
        }

        internal static Brush Frozen(byte a, byte r, byte g, byte b)
        {
            SolidColorBrush brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}
