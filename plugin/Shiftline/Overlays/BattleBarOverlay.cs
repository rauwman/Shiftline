using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Shiftline.Plugin
{
    /// <summary>
    /// The Battle Bar: a horizontal overlay with the player in the middle and the cars around (cards), or a narrow line with
    /// the field (ticker), fixed or running. The visual tree is built once per option change; an update only changes texts,
    /// colours and the slide of the cards.
    /// </summary>
    internal sealed class BattleBarOverlay : Border
    {
        private const double OneLineHeight = 32;
        private const double TwoLineHeight = 46;
        private const double CardGap = 6;
        private const double LeaderGap = 16;
        private const double SideWidth = 66;
        private const double LineHeight = 20;
        private const double PlayerZoom = 1.18;

        private static readonly Brush TextBrush = StandingsOverlay.Frozen(0xFF, 0xE8, 0xEC, 0xF1);
        private static readonly Brush MutedBrush = StandingsOverlay.Frozen(0xFF, 0x8E, 0x98, 0xA6);
        private static readonly Brush DarkText = StandingsOverlay.Frozen(0xFF, 0x10, 0x12, 0x16);
        private static readonly Brush WhiteFill = StandingsOverlay.Frozen(0xFF, 0xF9, 0xF9, 0xF9);
        private static readonly Brush SubtleFill = StandingsOverlay.Frozen(0x1E, 0xFF, 0xFF, 0xFF);
        private static readonly Brush AccentBrush = StandingsOverlay.Frozen(0xFF, 0x3F, 0xA9, 0xF5);
        private static readonly Brush BattleBrush = StandingsOverlay.Frozen(0xFF, 0xFF, 0x8A, 0x1F);
        private static readonly Brush GreenBrush = StandingsOverlay.Frozen(0xFF, 0x5E, 0xE0, 0x7A);
        private static readonly Brush LossBrush = StandingsOverlay.Frozen(0xFF, 0xF0, 0x5A, 0x4A);
        private static readonly Brush PurpleBrush = StandingsOverlay.Frozen(0xFF, 0xC0, 0x7C, 0xFF);
        private static readonly Brush PitBrush = StandingsOverlay.Frozen(0xFF, 0xFF, 0xB0, 0x20);
        private static readonly Brush RingFill = StandingsOverlay.Frozen(0xFF, 0x05, 0x06, 0x08);
        private static readonly Geometry ChevronUp = StandingsOverlay.FrozenGeometry("M 0.5,5.5 L 4,2 L 7.5,5.5");
        private static readonly Geometry ChevronDown = StandingsOverlay.FrozenGeometry("M 0.5,2 L 4,5.5 L 7.5,2");

        private readonly bool _interactive;
        private BattleBarOptions _options;
        private Brush _cardBrush;
        private Brush _playerBrush;
        private double _round = 1;
        private double _nameWidth = 100;
        private double _cardWidth;
        private double _pitch;
        private FontFamily _font = new FontFamily("Segoe UI");
        private FontWeight _weight = FontWeights.SemiBold;
        private double _designWidth;
        private double _designHeight;

        private StackPanel _header;
        private Border _headerBox;
        private List<HeaderItem> _headerItems = new List<HeaderItem>();
        private Panel _footer;
        private TextBlock _footLast;
        private TextBlock _footDelta;
        private TextBlock _footBest;
        private SideBox _left;
        private SideBox _right;

        // cards and fixed ticker: one view per slot, slid when a car moves to another slot
        private Slot[] _slots = new Slot[0];
        private Dictionary<int, int> _lastSlots = new Dictionary<int, int>();
        private int _lastPlayerPosition;

        // running ticker
        private Canvas _scrollView;
        private StackPanel _scrollStrip;
        private readonly TranslateTransform _scrollShift = new TranslateTransform();
        private readonly List<Slot> _scrollEntries = new List<Slot>();
        private double _scrollOffset;
        private DateTime _lastFrame = DateTime.MinValue;
        private DateTime _pauseUntil = DateTime.MinValue;
        private bool _pausedThisPass;
        private bool _rendering;

        public BattleBarOverlay() : this(false)
        {
        }

        /// <param name="interactive">For the preview on the settings page (no difference yet, kept for symmetry).</param>
        public BattleBarOverlay(bool interactive)
        {
            _interactive = interactive;
            Background = Brushes.Transparent;
            Loaded += (sender, args) => SetRendering(Mode == "Scroll");
            Unloaded += (sender, args) => SetRendering(false);
            ApplyOptions(BattleBarOptions.Load());
        }

        /// <summary>The window size that fits the current design.</summary>
        public Size DesignSize
        {
            get { return new Size(_designWidth * _options.Scale * BaseScale, _designHeight * _options.Scale * BaseScale); }
        }

        private string Mode
        {
            get { return _options == null ? "Cards" : _options.Get("Mode"); }
        }

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

        // ================================================================ building

        public void ApplyOptions(BattleBarOptions options)
        {
            _options = options;
            _round = options.Get("Corners") == "Square" ? 0 : 1;
            _font = OverlayFonts.Get(options.Get("Font"));
            _weight = StandingsOverlay.WeightOf(options.Get("Weight"));
            byte alpha = (byte)Math.Round(255 * Math.Max(0, Math.Min(100, options.GetInt("Opacity"))) / 100.0);
            _cardBrush = StandingsOverlay.Frozen(alpha, 0x0E, 0x12, 0x19);
            _playerBrush = StandingsOverlay.Frozen((byte)Math.Max((int)alpha, 0xC0), 0x16, 0x2A, 0x3E);
            _nameWidth = options.Get("Name.Format") == "Code" ? 40 : Math.Max(60, options.GetInt("Name.Width"));
            _cardWidth = CardWidth();

            _lastSlots = new Dictionary<int, int>();
            _scrollEntries.Clear();
            _scrollView = null;
            _scrollStrip = null;

            StackPanel root = new StackPanel { Margin = new Thickness(8) };
            _header = null;
            _headerBox = null;
            _headerItems = new List<HeaderItem>();
            _footer = null;
            if (options.Header)
            {
                // Eight places in a row, each showing what was chosen for it (the contents are shared with the standings).
                _header = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                System.Windows.Documents.TextElement.SetFontFamily(_header, _font);
                foreach (string slot in new[] { "H.S1", "H.S2", "H.S3", "H.S4", "H.S5", "H.S6", "H.S7", "H.S8" })
                {
                    HeaderItem item = HeaderItems.Create(options.Get(slot), key => options.Get(key), TextBrush, MutedBrush, 12, new Thickness(6, 0, 6, 0));
                    if (item != null)
                    {
                        _headerItems.Add(item);
                        _header.Children.Add(item.Panel);
                    }
                }

                _headerBox = new Border
                {
                    Background = _cardBrush,
                    CornerRadius = new CornerRadius(4 * _round),
                    Height = LineHeight,
                    Padding = new Thickness(4, 0, 4, 0),
                    Margin = new Thickness(0, 0, 0, 4),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Child = _header
                };
                root.Children.Add(_headerBox);
            }

            string mode = Mode;
            double rowHeight = mode == "Cards" ? Math.Ceiling(CardHeight * PlayerZoom) : CardHeight;
            Grid middle = new Grid { Height = rowHeight };
            middle.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            middle.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            middle.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _left = BuildSide(options.Get("Side.Left"), rowHeight);
            _right = BuildSide(options.Get("Side.Right"), rowHeight);
            double width = 0;
            if (_left != null)
            {
                _left.Box.Margin = new Thickness(0, 0, CardGap, 0);
                middle.Children.Add(_left.Box);
                width += SideWidth + CardGap;
            }

            FrameworkElement strip = mode == "Cards" ? BuildCards(rowHeight) : mode == "Ticker" ? BuildTicker() : BuildScroll();
            Grid.SetColumn(strip, 1);
            middle.Children.Add(strip);
            width += strip.Width;
            if (_right != null)
            {
                _right.Box.Margin = new Thickness(CardGap, 0, 0, 0);
                Grid.SetColumn(_right.Box, 2);
                middle.Children.Add(_right.Box);
                width += SideWidth + CardGap;
            }

            // The header may be wider than the cards; the overlay then grows and the cards stay in the middle.
            double headerWidth = 0;
            if (_header != null)
            {
                foreach (HeaderItem item in _headerItems)
                {
                    item.ValueRun.Text = item.Sample;
                }

                _header.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                headerWidth = Math.Ceiling(_header.DesiredSize.Width) + 8;
                foreach (HeaderItem item in _headerItems)
                {
                    item.ValueRun.Text = string.Empty;
                }
            }

            middle.HorizontalAlignment = HorizontalAlignment.Center;
            root.Children.Add(middle);
            if (options.Footer)
            {
                root.Children.Add(BuildFooter());
            }

            _designWidth = Math.Max(width, headerWidth) + 16;
            _designHeight = rowHeight + 16 + (options.Header ? LineHeight + 4 : 0) + (options.Footer ? LineHeight + 4 : 0);
            root.LayoutTransform = new ScaleTransform(options.Scale * BaseScale, options.Scale * BaseScale);
            Child = root;
            SetRendering(IsLoaded && mode == "Scroll");
        }

        /// <summary>One line when only position, class, name, make and gap are on; two lines when more is shown.</summary>
        private double CardHeight
        {
            get { return BottomItems().Count > 0 ? TwoLineHeight : OneLineHeight; }
        }

        private FrameworkElement BuildCards(double rowHeight)
        {
            int ahead = Math.Max(1, Math.Min(4, _options.GetInt("Ahead")));
            int behind = Math.Max(1, Math.Min(4, _options.GetInt("Behind")));
            bool leader = _options.GetBool("Leader");
            int count = (leader ? 1 : 0) + ahead + 1 + behind;
            _slots = new Slot[count];
            Canvas canvas = new Canvas { Height = rowHeight, ClipToBounds = false };
            double x = 0;
            for (int i = 0; i < count; i++)
            {
                bool player = i == (leader ? 1 : 0) + ahead;
                double zoom = player ? PlayerZoom : 1;
                Slot slot = BuildCard(zoom);
                double w = _cardWidth * zoom;
                double h = CardHeight * zoom;
                slot.X = x;
                Canvas.SetLeft(slot.Box, x);
                Canvas.SetTop(slot.Box, (rowHeight - h) / 2);
                canvas.Children.Add(slot.Box);
                _slots[i] = slot;
                x += w + (leader && i == 0 ? LeaderGap : CardGap);
            }

            canvas.Width = x - CardGap;
            return canvas;
        }

        // A card, laid out on a grid so everything lines up:
        //   [position][class bar] | name ............ make   gap |
        //                         | iRating tyre pit      gained |
        // The name takes the room that is left, make and gap stand right-aligned at the end of the first line. On the second
        // line iRating, tyre and pit start under the name (in the order chosen on the page) and the places gained stand
        // right-aligned under the gap. The second line only exists when one of its items is on.
        private const double Frame = 1.5;
        private const double Space = 6;

        /// <summary>
        /// The size the bar is drawn at for a setting of 100 %. The setting stays a plain percentage, but the layout was drawn a
        /// little small, so 100 % is the size that looked right at 105 % before.
        /// </summary>
        private const double BaseScale = 1.05;
        private static readonly string[] LeftItems = { "Pos", "Class" };
        private static readonly string[] TopItems = { "Name", "Make", "Gap" };

        private List<string> ItemsOf(string[] part)
        {
            return _options.Items.Select(c => c.Key).Where(k => part.Contains(k)).ToList();
        }

        private List<string> BottomItems()
        {
            return _options.Items.Select(c => c.Key).Where(k => !LeftItems.Contains(k) && !TopItems.Contains(k)).ToList();
        }

        private bool On(string key)
        {
            return _options.Items.Any(c => c.Key == key);
        }

        private double LeftWidth()
        {
            double width = 0;
            if (On("Pos"))
            {
                width += 24 + 5;
            }

            if (On("Class"))
            {
                width += 3 + 7;
            }

            return width;
        }

        /// <summary>Width of a normal card (unscaled): frame, padding, the left block and the first line.</summary>
        private double CardWidth()
        {
            double line = On("Name") ? _nameWidth : 0;
            if (On("Make"))
            {
                line += (line > 0 ? Space : 0) + MakeWidth;
            }

            if (On("Gap"))
            {
                line += (line > 0 ? 4 : 0) + GapWidth;
            }

            if (line == 0)
            {
                line = 90;                  // only second-line items: room for them under nothing
            }

            return Math.Ceiling(2 * Frame + 2 * 7 + LeftWidth() + line);
        }

        private const double MakeWidth = 30;

        private double GapWidth
        {
            get { return _options.GetBool("Trend") ? 46 : 34; }
        }

        private double ItemWidth(string key)
        {
            switch (key)
            {
                case "iR":
                    return 34;
                case "Tyre":
                    return 14;
                case "Pit":
                    return 26;
                case "Gain":
                    return 30;
                default:
                    return 30;
            }
        }

        /// <summary>A card at normal size; the player's card in the cards layout is the same card drawn larger.</summary>
        private Slot BuildCard(double zoom)
        {
            Slot slot = new Slot { Player = zoom > 1 };
            double height = CardHeight;
            double top = 13;
            double small = 11;
            bool twoLines = BottomItems().Count > 0;

            Grid inner = new Grid { Margin = new Thickness(7, 0, 7, 0) };
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // position box
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // class bar
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });   // name
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // make
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // gap
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            inner.RowDefinitions.Add(new RowDefinition { Height = twoLines ? GridLength.Auto : new GridLength(0) });
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            if (On("Pos"))
            {
                FrameworkElement pos = BuildItem(slot, "Pos", top);
                pos.Width = 24;
                pos.Margin = new Thickness(0, 0, 5, 0);
                Place(inner, pos, 0, 0, 3);
            }

            if (On("Class"))
            {
                FrameworkElement bar = BuildItem(slot, "Class", top);
                bar.Width = 3;
                slot.ClassBar.Height = height - 18;
                bar.Margin = new Thickness(0, 0, 7, 0);
                Place(inner, bar, 1, 0, 3);
            }

            // first line: one line spans all rows and sits centred; with two lines it sits on the second line
            const int firstRow = 0;
            if (On("Name"))
            {
                Place(inner, BuildItem(slot, "Name", top), 2, firstRow, twoLines ? 1 : 3, twoLines ? VerticalAlignment.Bottom : VerticalAlignment.Center);
            }

            if (On("Make"))
            {
                FrameworkElement make = BuildItem(slot, "Make", top);
                make.Width = MakeWidth;
                make.Margin = new Thickness(Space, 0, 0, 0);
                make.HorizontalAlignment = HorizontalAlignment.Right;
                Place(inner, make, 3, firstRow, twoLines ? 1 : 3, twoLines ? VerticalAlignment.Bottom : VerticalAlignment.Center);
            }

            if (On("Gap"))
            {
                FrameworkElement gap = BuildItem(slot, "Gap", top);
                gap.Width = GapWidth;
                gap.Margin = new Thickness(4, 0, 0, 0);       // the make stands close to the gap
                Place(inner, gap, 4, firstRow, twoLines ? 1 : 3, twoLines ? VerticalAlignment.Bottom : VerticalAlignment.Center);
            }

            if (twoLines)
            {
                // second line: the iRating under the name; on the right, in this order, pit, tyre and places gained (under the gap)
                // on the left under the name: the iRating, then the licence (when on), a little smaller so it does not outweigh the iRating
                StackPanel ratings = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 0) };
                if (On("iR"))
                {
                    FrameworkElement rating = BuildItem(slot, "iR", small);
                    rating.VerticalAlignment = VerticalAlignment.Center;
                    ratings.Children.Add(rating);
                }

                if (On("Lic"))
                {
                    FrameworkElement licence = BuildItem(slot, "Lic", small - 2);
                    licence.Margin = new Thickness(ratings.Children.Count > 0 ? Space : 0, 0, 0, 0);
                    ratings.Children.Add(licence);
                }

                if (ratings.Children.Count > 0)
                {
                    Place(inner, ratings, 2, 1, 1, VerticalAlignment.Top);
                }

                StackPanel right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 0, 0) };
                foreach (string key in new[] { "Pit", "Tyre", "Gain" })
                {
                    if (!On(key))
                    {
                        continue;
                    }

                    FrameworkElement element = BuildItem(slot, key, small);
                    element.Margin = new Thickness(right.Children.Count > 0 ? Space : 0, 0, 0, 0);
                    element.VerticalAlignment = VerticalAlignment.Center;
                    right.Children.Add(element);
                }

                if (right.Children.Count > 0)
                {
                    Place(inner, right, 3, 1, 1, VerticalAlignment.Top);
                    Grid.SetColumnSpan(right, 2);
                }
            }

            // the two rows above and below the lines share the free height, so the block stays centred in the card
            if (twoLines)
            {
                inner.RowDefinitions[0].Height = GridLength.Auto;
                inner.RowDefinitions[2].Height = GridLength.Auto;
                inner.VerticalAlignment = VerticalAlignment.Center;
                foreach (UIElement child in inner.Children)
                {
                    if (Grid.GetRowSpan(child) == 3)
                    {
                        Grid.SetRowSpan(child, 2);
                    }
                }
            }

            slot.Flash = new Border { CornerRadius = new CornerRadius(5 * _round), Opacity = 0, IsHitTestVisible = false };
            Grid content = new Grid();
            content.Children.Add(slot.Flash);
            content.Children.Add(inner);
            slot.Box = new Border
            {
                Width = _cardWidth,
                Height = height,
                Background = _cardBrush,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(Frame),
                CornerRadius = new CornerRadius(5 * _round),
                Child = content,
                RenderTransform = new TransformGroup { Children = { slot.Grow, slot.Shift } },
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            if (zoom > 1)
            {
                slot.Box.LayoutTransform = new ScaleTransform(zoom, zoom);
            }

            return slot;
        }

        private static void Place(Grid grid, FrameworkElement element, int column, int row, int rowSpan, VerticalAlignment align = VerticalAlignment.Center)
        {
            Grid.SetColumn(element, column);
            Grid.SetRow(element, row);
            Grid.SetRowSpan(element, rowSpan);
            element.VerticalAlignment = align;
            grid.Children.Add(element);
        }

        private FrameworkElement BuildItem(Slot slot, string key, double size)
        {
            switch (key)
            {
                case "Pos":
                    slot.Pos = Text(size, TextBrush);
                    slot.Pos.HorizontalAlignment = HorizontalAlignment.Center;
                    slot.Pos.FontWeight = FontWeights.Bold;
                    Brush fill = PositionFill();
                    if (ReferenceEquals(fill, WhiteFill))
                    {
                        slot.Pos.Foreground = DarkText;
                    }

                    slot.PosBox = new Border { Background = fill, CornerRadius = new CornerRadius(3 * _round), Height = size + 8, VerticalAlignment = VerticalAlignment.Center, Child = slot.Pos };
                    return slot.PosBox;
                case "Class":
                    slot.ClassBar = new Rectangle { Height = size + 8, RadiusX = 1.5 * _round, RadiusY = 1.5 * _round, Fill = MutedBrush, VerticalAlignment = VerticalAlignment.Center };
                    return slot.ClassBar;
                case "Name":
                    // the name, and on the right a small lap mark for cars a lap up or down (only when sorted by track)
                    slot.Name = Text(size, TextBrush);
                    slot.Laps = Text(size - 2.5, MutedBrush);
                    slot.Laps.Margin = new Thickness(4, 0, 0, 0);
                    slot.Laps.Visibility = Visibility.Collapsed;
                    DockPanel name = new DockPanel { LastChildFill = true };
                    DockPanel.SetDock(slot.Laps, Dock.Right);
                    name.Children.Add(slot.Laps);
                    name.Children.Add(slot.Name);
                    return name;
                case "Make":
                    slot.Make = Text(size - 2, MutedBrush);
                    slot.Make.TextAlignment = TextAlignment.Right;
                    return slot.Make;
                case "Gap":
                    // right-aligned at the end of its space, with the trend arrow in front of the number
                    slot.Gap = Text(size, TextBrush);
                    slot.Trend = Arrow();
                    StackPanel gap = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                    gap.Children.Add(slot.Trend);
                    gap.Children.Add(slot.Gap);
                    return new Grid { Children = { gap } };
                case "iR":
                    slot.IRating = Text(size, MutedBrush);
                    return slot.IRating;
                case "Lic":
                    // like the standings: the rating on a dark shade of the licence colour, the class letter on the full colour
                    slot.LicNumber = Text(size, Brushes.White);
                    slot.LicNumber.Margin = new Thickness(3, 0, 2, 0);
                    slot.LicNumber.Visibility = _options.GetBool("Lic.Number") ? Visibility.Visible : Visibility.Collapsed;
                    slot.LicLetter = Text(size, Brushes.White);
                    slot.LicLetter.FontWeight = FontWeights.Bold;
                    StandingsOverlay.CentreInk(slot.LicNumber, StandingsOverlay.InkDigits);
                    StandingsOverlay.CentreInk(slot.LicLetter, StandingsOverlay.InkLetters);

                    slot.LicLetter.Margin = new Thickness(3, 0, 3, 0);
                    slot.LicRight = new Border
                    {
                        CornerRadius = _options.GetBool("Lic.Number") ? new CornerRadius(0, 3 * _round, 3 * _round, 0) : new CornerRadius(3 * _round),
                        Child = slot.LicLetter
                    };
                    StackPanel parts = new StackPanel { Orientation = Orientation.Horizontal };
                    parts.Children.Add(slot.LicNumber);
                    parts.Children.Add(slot.LicRight);
                    slot.LicBox = new Border { CornerRadius = new CornerRadius(3 * _round), Child = StandingsOverlay.WithGloss(parts, 3 * _round), VerticalAlignment = VerticalAlignment.Center };
                    return slot.LicBox;
                case "Tyre":
                    slot.TyreRing = new Ellipse { Width = 14, Height = 14, StrokeThickness = 2, Stroke = MutedBrush, Fill = RingFill };
                    slot.TyreLetter = new Path { Fill = TextBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Stretch = Stretch.None };
                    Grid tyre = new Grid { Height = 14, VerticalAlignment = VerticalAlignment.Center };
                    tyre.Children.Add(slot.TyreRing);
                    tyre.Children.Add(slot.TyreLetter);
                    return tyre;
                case "Pit":
                    slot.Pit = Text(size, PitBrush);
                    slot.Pit.FontWeight = FontWeights.Bold;
                    return slot.Pit;
                case "Gain":
                    slot.Gain = Text(size, MutedBrush);
                    slot.GainArrow = Arrow();
                    StackPanel gain = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    gain.Children.Add(slot.GainArrow);
                    gain.Children.Add(slot.Gain);
                    return gain;
                default:
                    return new Border();
            }
        }

        private Brush PositionFill()
        {
            switch (_options.Get("PosBg"))
            {
                case "Subtle":
                    return SubtleFill;
                case "None":
                    return Brushes.Transparent;
                default:
                    return WhiteFill;
            }
        }

        /// <summary>The fixed ticker: cards like the cards layout, all the same size, centred on the player.</summary>
        private FrameworkElement BuildTicker()
        {
            int count = Math.Max(3, _options.GetInt("Ticker.Cars"));
            _pitch = _cardWidth + CardGap;
            _slots = new Slot[count];
            Canvas canvas = new Canvas { Height = CardHeight, Width = count * _pitch - CardGap, ClipToBounds = false };
            for (int i = 0; i < count; i++)
            {
                Slot slot = BuildCard(1);
                slot.X = i * _pitch;
                Canvas.SetLeft(slot.Box, slot.X);
                canvas.Children.Add(slot.Box);
                _slots[i] = slot;
            }

            return canvas;
        }

        /// <summary>The running ticker: the whole field twice in a row, moved to the left and wrapped, so it never ends.</summary>
        private FrameworkElement BuildScroll()
        {
            int count = Math.Max(3, _options.GetInt("Ticker.Cars"));
            _pitch = _cardWidth + CardGap;
            _slots = new Slot[0];
            _scrollStrip = new StackPanel { Orientation = Orientation.Horizontal, RenderTransform = _scrollShift };
            _scrollView = new Canvas { Width = count * _pitch - CardGap, Height = CardHeight, ClipToBounds = true };
            _scrollView.Children.Add(_scrollStrip);
            _scrollOffset = 0;
            return _scrollView;
        }

        private SideBox BuildSide(string kind, double height)
        {
            if (string.IsNullOrEmpty(kind) || kind == "Off")
            {
                return null;
            }

            SideBox side = new SideBox { Kind = kind };
            side.Label = Text(9.5, MutedBrush);
            side.Label.HorizontalAlignment = HorizontalAlignment.Center;
            side.Label.Text = SideLabel(kind);
            side.Value = Text(height > 30 ? 14 : 12, TextBrush);
            side.Value.HorizontalAlignment = HorizontalAlignment.Center;
            StackPanel panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            if (height > 30)
            {
                panel.Children.Add(side.Label);
                panel.Children.Add(side.Value);
            }
            else
            {
                // the ticker is too low for two lines: label and value side by side
                panel.Orientation = Orientation.Horizontal;
                panel.HorizontalAlignment = HorizontalAlignment.Center;
                side.Label.Margin = new Thickness(0, 0, 4, 0);
                side.Label.VerticalAlignment = VerticalAlignment.Center;
                panel.Children.Add(side.Label);
                panel.Children.Add(side.Value);
            }

            side.Box = new Border { Width = SideWidth, Height = height, Background = _cardBrush, CornerRadius = new CornerRadius(5 * _round), Child = panel };
            return side;
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

        private FrameworkElement BuildFooter()
        {
            StackPanel panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            _footLast = _options.GetBool("F.Last") ? FooterItem(panel, "LAST") : null;
            _footDelta = _options.GetBool("F.Delta") ? FooterItem(panel, "DELTA") : null;
            _footBest = _options.GetBool("F.Best") ? FooterItem(panel, "BEST") : null;
            _footer = panel;
            return new Border
            {
                Background = _cardBrush,
                CornerRadius = new CornerRadius(4 * _round),
                Height = LineHeight,
                Padding = new Thickness(10, 0, 0, 0),
                Margin = new Thickness(0, 4, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = panel
            };
        }

        private TextBlock FooterItem(Panel panel, string label)
        {
            TextBlock name = Text(10, MutedBrush);
            name.Text = label;
            name.Margin = new Thickness(0, 0, 4, 0);
            TextBlock value = Text(12, TextBrush);
            value.Margin = new Thickness(0, 0, 12, 0);
            panel.Children.Add(name);
            panel.Children.Add(value);
            return value;
        }

        private TextBlock Text(double size, Brush brush)
        {
            return new TextBlock
            {
                FontFamily = _font,
                FontWeight = _weight,
                FontSize = size,
                Foreground = brush,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
        }

        private static Path Arrow()
        {
            return new Path
            {
                Width = 8,
                Height = 8,
                StrokeThickness = 1.6,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 3, 0)
            };
        }

        // ================================================================ updating

        public void Update(StandingsSnapshot snapshot)
        {
            Field field = new Field(snapshot, _options);
            if (_header != null)
            {
                StandingsRow lapRow = field.Order.Count > 0 && !field.ByTrack ? field.Order[0] : field.Player;
                HeaderItems.Update(_headerItems, snapshot, key => _options.Get(key), true, lapRow);
                _headerBox.Visibility = _headerItems.Any(item => item.Panel.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Hidden;
            }

            UpdateSide(_left, snapshot, field);
            UpdateSide(_right, snapshot, field);
            UpdateFooter(snapshot, field);
            string mode = Mode;
            if (mode == "Scroll")
            {
                UpdateScroll(field);
                return;
            }

            List<StandingsRow> shown = mode == "Cards" ? ChooseCards(field) : ChooseTicker(field);
            double seconds = AnimationSeconds(snapshot.IsReplay);
            string style = _options.Get("Anim.Style");
            Dictionary<int, int> slots = new Dictionary<int, int>();
            Dictionary<int, int> carBefore = _lastSlots.ToDictionary(p => p.Value, p => p.Key);   // slot -> car of the last update
            for (int i = 0; i < _slots.Length; i++)
            {
                Slot slot = _slots[i];
                StandingsRow row = i < shown.Count ? shown[i] : null;
                Visibility visible = row == null ? Visibility.Hidden : Visibility.Visible;
                if (slot.Box.Visibility != visible)
                {
                    slot.Box.Visibility = visible;
                }

                if (row == null)
                {
                    continue;
                }

                slots[row.CarIdx] = i;
                int previous;
                bool changed = carBefore.TryGetValue(i, out previous) && previous != row.CarIdx;
                if (seconds > 0 && changed && (style == "Fade" || style == "Pop"))
                {
                    // Another car in this card: a picture of the old card fades out above the new one, so nothing jumps.
                    double duration = FadeSeconds();
                    CrossFade(slot, style == "Pop" ? duration * 0.5 : duration);
                    if (style == "Pop")
                    {
                        // the new card comes up from slightly smaller and springs a little past its size before it settles
                        DoubleAnimation grow = new DoubleAnimation(0.9, 1, TimeSpan.FromSeconds(duration)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 } };
                        slot.Grow.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                        slot.Grow.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
                        slot.Box.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(duration * 0.6)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
                    }
                    else
                    {
                        slot.Box.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(duration)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
                    }
                }

                FillCard(slot, row, field);

                int before;
                if (seconds > 0 && style == "Slide" && _lastSlots.TryGetValue(row.CarIdx, out before) && before != i && before < _slots.Length)
                {
                    {
                        // slide from where the car was drawn before to its new place
                        double from = _slots[before].X - slot.X;
                        slot.Shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(from, 0, TimeSpan.FromSeconds(seconds)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut } });
                    }
                }
            }

            _lastSlots = slots;
            FlashOnChange(field);
        }

        /// <summary>The cars of the cards: optionally the leader, then the cars in front of the player, the player, the cars behind.</summary>
        private List<StandingsRow> ChooseCards(Field field)
        {
            int ahead = Math.Max(1, Math.Min(4, _options.GetInt("Ahead")));
            int behind = Math.Max(1, Math.Min(4, _options.GetInt("Behind")));
            bool leader = _options.GetBool("Leader");
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

        private List<StandingsRow> ChooseTicker(Field field)
        {
            int count = _slots.Length;
            List<StandingsRow> order = field.Order;
            int start = 0;
            if (field.PlayerIndex >= 0 && order.Count > count)
            {
                start = Math.Max(0, Math.Min(order.Count - count, field.PlayerIndex - count / 2));
            }

            return order.Skip(start).Take(count).ToList();
        }

        private void FillCard(Slot slot, StandingsRow row, Field field)
        {
            if (slot.Pos != null)
            {
                Set(slot.Pos, field.PositionOf(row) > 0 ? field.PositionOf(row).ToString(CultureInfo.InvariantCulture) : "-");
            }

            Brush classBrush = StandingsOverlay.ClassBrush(row.ClassColor);
            if (slot.ClassBar != null && !ReferenceEquals(slot.ClassBar.Fill, classBrush))
            {
                slot.ClassBar.Fill = classBrush;
            }

            if (slot.Name != null)
            {
                Set(slot.Name, NameOf(row));
                UpdateLapMark(slot, row, field);
            }

            if (slot.Make != null)
            {
                Set(slot.Make, row.Make ?? string.Empty);
            }

            if (slot.IRating != null)
            {
                Set(slot.IRating, StandingsOverlay.FormatIRating(row.IRating, "K1"));
            }

            if (slot.LicBox != null)
            {
                string[] parts = (row.License ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                double rating = 0;
                bool hasRating = parts.Length > 1 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out rating);
                Set(slot.LicLetter, parts.Length > 0 ? parts[0] : string.Empty);
                Set(slot.LicNumber, hasRating ? (Math.Floor(rating * 10) / 10).ToString("0.0", CultureInfo.InvariantCulture) : string.Empty);
                Brush colour = StandingsOverlay.LicenceBrush(row.LicenseColor);
                slot.LicBox.Visibility = colour == null || parts.Length == 0 ? Visibility.Hidden : Visibility.Visible;
                Brush soft = colour == null ? null : StandingsOverlay.Soften(colour);
                if (soft != null && !ReferenceEquals(slot.LicRight.Background, soft))
                {
                    slot.LicRight.Background = soft;
                    slot.LicLetter.Foreground = StandingsOverlay.ContrastText(soft);
                    slot.LicBox.Background = _options.GetBool("Lic.Number") ? StandingsOverlay.Shade(colour) : null;
                }
            }

            if (slot.Pit != null)
            {
                Set(slot.Pit, row.OnPitRoad ? "PIT" : row.TrackSurface < 0 ? "OUT" : string.Empty);
            }

            if (slot.Gain != null)
            {
                int start = field.StartPositionOf(row);
                int now = field.PositionOf(row);
                int gained = start > 0 && now > 0 ? start - now : 0;
                Set(slot.Gain, gained != 0 ? Math.Abs(gained).ToString(CultureInfo.InvariantCulture) : string.Empty);
                Brush brush = gained > 0 ? GreenBrush : LossBrush;
                slot.Gain.Foreground = gained == 0 ? MutedBrush : brush;
                slot.GainArrow.Data = gained > 0 ? ChevronUp : gained < 0 ? ChevronDown : null;
                slot.GainArrow.Stroke = brush;
            }

            if (slot.TyreRing != null)
            {
                string type = row.TireType ?? string.Empty;
                slot.TyreRing.Visibility = type.Length > 0 ? Visibility.Visible : Visibility.Hidden;
                slot.TyreLetter.Visibility = slot.TyreRing.Visibility;
                if (type.Length > 0)
                {
                    string code;
                    Brush colour;
                    StandingsOverlay.TyreLook(type, out code, out colour);
                    slot.TyreLetter.Data = StandingsOverlay.LetterShape(code);
                    slot.TyreRing.Stroke = colour;
                }
            }

            double gap;
            bool sameLap;
            if (slot.Gap != null)
            {
                Set(slot.Gap, GapText(row, field, out gap, out sameLap));
                UpdateTrend(slot, row, field);
            }
            else
            {
                GapText(row, field, out gap, out sameLap);
            }

            // The player's card is lighter (in the ticker it is the only sign of the player). Frame: blue around the player
            // (can be switched off), the battle colour when a car is close, otherwise the class colour when all classes show.
            Brush background = row.IsPlayer ? _playerBrush : _cardBrush;
            if (!ReferenceEquals(slot.Box.Background, background))
            {
                slot.Box.Background = background;
            }

            Brush frame = Brushes.Transparent;
            double limit = _options.GetDouble("Battle");
            if (row.IsPlayer)
            {
                frame = PlayerFrame();
            }
            else if (limit > 0 && sameLap && Math.Abs(gap) < limit && field.Race)
            {
                frame = BattleColour(_options.Get("Battle.Colour"));
            }
            else if (_options.Get("Classes") == "All" && field.MultiClass)
            {
                frame = classBrush;
            }

            if (!ReferenceEquals(slot.Box.BorderBrush, frame))
            {
                slot.Box.BorderBrush = frame;
            }

        }

        private static readonly Brush GreenFrame = StandingsOverlay.Frozen(0xFF, 0x3D, 0xDC, 0x84);
        private readonly Dictionary<string, LinearGradientBrush> _flows = new Dictionary<string, LinearGradientBrush>();

        private static readonly Brush WhiteFrame = StandingsOverlay.Frozen(0xFF, 0xF9, 0xF9, 0xF9);

        /// <summary>
        /// The frame around the player's card: a plain colour, none, or a flow: "Shiftline" with the four colours of the
        /// Shiftline theme, "Blue flow" and "Green flow" with lighter and darker shades of one colour, flowing around the card
        /// (a gradient that turns slowly).
        /// </summary>
        private Brush Flow(string key, params int[] colours)
        {
            LinearGradientBrush brush;
            if (_flows.TryGetValue(key, out brush))
            {
                return brush;
            }

            brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            for (int k = 0; k < colours.Length; k++)
            {
                int c = colours[k];
                brush.GradientStops.Add(new GradientStop(Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c), k / (double)(colours.Length - 1)));
            }

            RotateTransform turn = new RotateTransform(0, 0.5, 0.5);
            brush.RelativeTransform = turn;
            turn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(4)) { RepeatBehavior = RepeatBehavior.Forever });
            _flows[key] = brush;
            return brush;
        }

        private Brush PlayerFrame()
        {
            switch (_options.Get("Frame.Player"))
            {
                case "Off":
                    return Brushes.Transparent;
                case "White":
                    return WhiteFrame;
                case "Green":
                    return GreenFrame;
                case "Shiftline":
                    return Flow("Shiftline", 0x01ABFF, 0x903AA1, 0xFF8C00, 0xD60000);
                case "BlueFlow":
                    return Flow("BlueFlow", 0x3FA9F5, 0xA8DCFF, 0x1E7FCC, 0x7CC6FF);
                case "GreenFlow":
                    return Flow("GreenFlow", 0x3DDC84, 0xB4F5CF, 0x1FA85E, 0x86EDB2);
                default:
                    return AccentBrush;
            }
        }

        private static readonly Brush LappedBrush = StandingsOverlay.Frozen(0xFF, 0x7F, 0xB2, 0xE0);
        private static readonly Brush LappingBrush = StandingsOverlay.Frozen(0xFF, 0xF0, 0x40, 0x3E);

        /// <summary>
        /// Sorted by the track, cars of another lap sit right next to the player. Like iRacing's relative: a car a lap (or more)
        /// down gets a muted blue name and "-1L", a car a lap up a red name and "+1L".
        /// </summary>
        private void UpdateLapMark(Slot slot, StandingsRow row, Field field)
        {
            StandingsRow player = field.Player;
            int laps = 0;
            if (field.ByTrack && field.Race && player != null && !row.IsPlayer && row.LapDistPct >= 0 && player.LapDistPct >= 0)
            {
                laps = (int)Math.Round(row.Progress - player.Progress);
            }

            Brush name = laps < 0 ? LappedBrush : laps > 0 ? LappingBrush : TextBrush;
            if (!ReferenceEquals(slot.Name.Foreground, name))
            {
                slot.Name.Foreground = name;
            }

            Visibility visible = laps == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (slot.Laps.Visibility != visible)
            {
                slot.Laps.Visibility = visible;
            }

            if (laps != 0)
            {
                Set(slot.Laps, (laps > 0 ? "+" : "-") + Math.Abs(laps).ToString(CultureInfo.InvariantCulture) + "L");
                slot.Laps.Foreground = name;
            }
        }

        private static readonly Brush BattleRed = StandingsOverlay.Frozen(0xFF, 0xF0, 0x3E, 0x3E);
        private static readonly Brush BattleYellow = StandingsOverlay.Frozen(0xFF, 0xFF, 0xD2, 0x3F);

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

        private void UpdateScroll(Field field)
        {
            List<StandingsRow> order = field.Order;
            int wanted = Math.Min(64, order.Count) * 2;
            if (_scrollEntries.Count != wanted)
            {
                _scrollStrip.Children.Clear();
                _scrollEntries.Clear();
                for (int i = 0; i < wanted; i++)
                {
                    Slot entry = BuildCard(1);
                    entry.Box.Margin = new Thickness(0, 0, CardGap, 0);
                    _scrollEntries.Add(entry);
                    _scrollStrip.Children.Add(entry.Box);
                }

                _scrollOffset = 0;
            }

            int half = wanted / 2;
            for (int i = 0; i < wanted; i++)
            {
                FillCard(_scrollEntries[i], order[i % half], field);
            }

            _scrollPlayer = field.PlayerIndex;
            _scrollHalfWidth = half * _pitch;
        }

        private int _scrollPlayer = -1;
        private double _scrollHalfWidth;

        private void SetRendering(bool on)
        {
            if (on == _rendering)
            {
                return;
            }

            _rendering = on;
            if (on)
            {
                _lastFrame = DateTime.MinValue;
                CompositionTarget.Rendering += OnRendering;
            }
            else
            {
                CompositionTarget.Rendering -= OnRendering;
            }
        }

        /// <summary>Moves the running ticker on every frame; it stops for a moment when the player is in the middle.</summary>
        private void OnRendering(object sender, EventArgs e)
        {
            if (_scrollView == null || _scrollHalfWidth <= 0)
            {
                return;
            }

            DateTime now = DateTime.UtcNow;
            double dt = _lastFrame == DateTime.MinValue ? 0 : Math.Min(0.1, (now - _lastFrame).TotalSeconds);
            _lastFrame = now;
            if (now < _pauseUntil)
            {
                return;
            }

            double speed;
            switch (_options.Get("Ticker.Speed"))
            {
                case "Slow":
                    speed = 25;
                    break;
                case "Fast":
                    speed = 75;
                    break;
                default:
                    speed = 45;
                    break;
            }

            _scrollOffset += speed * dt;
            if (_scrollOffset >= _scrollHalfWidth)
            {
                _scrollOffset -= _scrollHalfWidth;
                _pausedThisPass = false;
            }

            if (!_pausedThisPass && _scrollPlayer >= 0)
            {
                // the player's entry is centred in the view: hold it there for a moment
                double centre = _scrollPlayer * _pitch + _cardWidth / 2 - _scrollView.Width / 2;
                if (centre < 0)
                {
                    centre += _scrollHalfWidth;
                }

                if (_scrollOffset >= centre && _scrollOffset - speed * dt < centre)
                {
                    _scrollOffset = centre;
                    _pauseUntil = now.AddSeconds(2.5);
                    _pausedThisPass = true;
                }
            }

            _scrollShift.X = -Math.Round(_scrollOffset, 1);
        }

        private string GapText(StandingsRow row, Field field, out double gap, out bool sameLap)
        {
            gap = 0;
            sameLap = false;
            StandingsRow player = field.Player;
            if (row.IsPlayer)
            {
                // the player's own card: the gap to the leader in races, the best lap otherwise
                if (!field.Race)
                {
                    return StandingsOverlay.Lap(row.BestLap, 3);
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
            if (_options.Get("GapTo") == "Ahead")
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

        /// <summary>Green: the player was faster than this car over the last laps (closing in, or pulling away); red: slower.</summary>
        private void UpdateTrend(Slot slot, StandingsRow row, Field field)
        {
            if (slot.Trend == null)
            {
                return;
            }

            Geometry shape = null;
            Brush brush = MutedBrush;
            int laps = Math.Max(1, _options.GetInt("Trend.Laps"));
            StandingsRow player = field.Player;
            if (_options.GetBool("Trend") && !row.IsPlayer && player != null && row.RecentLaps != null && player.RecentLaps != null
                && row.RecentLaps.Length >= laps && player.RecentLaps.Length >= laps)
            {
                double sum = 0;
                for (int k = 0; k < laps; k++)
                {
                    sum += row.RecentLaps[k] - player.RecentLaps[k];   // positive: the other car was slower
                }

                if (sum > 0.05)
                {
                    shape = ChevronUp;
                    brush = GreenBrush;
                }
                else if (sum < -0.05)
                {
                    shape = ChevronDown;
                    brush = LossBrush;
                }
            }

            if (!ReferenceEquals(slot.Trend.Data, shape))
            {
                slot.Trend.Data = shape;
            }

            if (!ReferenceEquals(slot.Trend.Stroke, brush))
            {
                slot.Trend.Stroke = brush;
            }
        }

        private void FlashOnChange(Field field)
        {
            StandingsRow player = field.Player;
            int position = player == null ? 0 : field.PositionOf(player);
            if (_lastPlayerPosition > 0 && position > 0 && position != _lastPlayerPosition && _options.GetBool("Anim.Flash"))
            {
                int index;
                if (_lastSlots.TryGetValue(player.CarIdx, out index) && index < _slots.Length)
                {
                    Slot slot = _slots[index];
                    slot.Flash.Background = position < _lastPlayerPosition ? GreenBrush : LossBrush;
                    DoubleAnimation fade = new DoubleAnimation(0.55, 0, TimeSpan.FromSeconds(1.4)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                    slot.Flash.BeginAnimation(OpacityProperty, fade);
                }
            }

            _lastPlayerPosition = position;
        }

        /// <summary>Fade and pop need more time than a slide to look smooth.</summary>
        private double FadeSeconds()
        {
            switch (_options.Get("Anim.Speed"))
            {
                case "Fast":
                    return 0.5;
                case "Slow":
                    return 1.3;
                default:
                    return 0.85;
            }
        }

        /// <summary>Puts a still picture of the card above it and fades the picture out, then removes it.</summary>
        private static void CrossFade(Slot slot, double seconds)
        {
            Panel parent = slot.Box.Parent as Panel;
            if (parent == null || slot.Box.ActualWidth <= 0 || slot.Box.Visibility != Visibility.Visible)
            {
                return;
            }

            ScaleTransform zoom = slot.Box.LayoutTransform as ScaleTransform;
            double factor = zoom == null ? 1 : zoom.ScaleX;
            DpiScale dpi = VisualTreeHelper.GetDpi(slot.Box);
            double width = slot.Box.ActualWidth;
            double height = slot.Box.ActualHeight;
            System.Windows.Media.Imaging.RenderTargetBitmap bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                Math.Max(1, (int)Math.Ceiling(width * factor * dpi.DpiScaleX)), Math.Max(1, (int)Math.Ceiling(height * factor * dpi.DpiScaleY)),
                dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            DrawingVisual drawing = new DrawingVisual();
            using (DrawingContext context = drawing.RenderOpen())
            {
                context.DrawRectangle(new VisualBrush(slot.Box), null, new Rect(0, 0, width * factor, height * factor));
            }

            bitmap.Render(drawing);
            bitmap.Freeze();
            Rectangle ghost = new Rectangle { Width = width * factor, Height = height * factor, Fill = new ImageBrush(bitmap), IsHitTestVisible = false };
            Canvas.SetLeft(ghost, Canvas.GetLeft(slot.Box));
            Canvas.SetTop(ghost, Canvas.GetTop(slot.Box));
            parent.Children.Add(ghost);
            DoubleAnimation fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(seconds)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            fade.Completed += (sender, args) => parent.Children.Remove(ghost);
            ghost.BeginAnimation(OpacityProperty, fade);
        }

        private double AnimationSeconds(bool replay)
        {
            if (_options.Get("Anim.Style") == "Off" || (replay && !_options.GetBool("Anim.Replay")))
            {
                return 0;
            }

            switch (_options.Get("Anim.Speed"))
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

        private void UpdateSide(SideBox side, StandingsSnapshot snapshot, Field field)
        {
            if (side == null)
            {
                return;
            }

            StandingsRow player = field.Player;
            string value = "-";
            Brush brush = TextBrush;
            switch (side.Kind)
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
                    value = player == null ? "-" : StandingsOverlay.Lap(player.LastLap, 3);
                    break;
                case "Best":
                    value = player == null ? "-" : StandingsOverlay.Lap(player.BestLap, 3);
                    brush = player != null && player.BestLap > 0 && Math.Abs(player.BestLap - field.SessionBest) < 0.0005 ? PurpleBrush : GreenBrush;
                    break;
                case "Temp":
                    value = TempText(snapshot);
                    break;
                case "Clock":
                    value = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);
                    break;
            }

            Set(side.Value, string.IsNullOrEmpty(value) ? "-" : value);
            if (!ReferenceEquals(side.Value.Foreground, brush))
            {
                side.Value.Foreground = brush;
            }
        }

        private void UpdateFooter(StandingsSnapshot snapshot, Field field)
        {
            if (_footer == null)
            {
                return;
            }

            StandingsRow player = field.Player;
            if (_footLast != null)
            {
                Set(_footLast, player == null ? "-" : StandingsOverlay.Lap(player.LastLap, 3));
            }

            if (_footBest != null)
            {
                Set(_footBest, player == null ? "-" : StandingsOverlay.Lap(player.BestLap, 3));
                Brush brush = player != null && player.BestLap > 0 && Math.Abs(player.BestLap - field.SessionBest) < 0.0005 ? PurpleBrush : GreenBrush;
                if (!ReferenceEquals(_footBest.Foreground, brush))
                {
                    _footBest.Foreground = brush;
                }
            }

            if (_footDelta != null)
            {
                string text = "-";
                Brush brush = MutedBrush;
                if (snapshot.HasDelta)
                {
                    double delta = snapshot.DeltaToBest;
                    text = (delta > 0 ? "+" : delta < 0 ? "-" : string.Empty) + Math.Abs(delta).ToString("0.00", CultureInfo.InvariantCulture);
                    brush = delta < -0.005 ? GreenBrush : delta > 0.005 ? LossBrush : TextBrush;
                }

                Set(_footDelta, text);
                if (!ReferenceEquals(_footDelta.Foreground, brush))
                {
                    _footDelta.Foreground = brush;
                }
            }
        }

        private string NameOf(StandingsRow row)
        {
            string format = _options.Get("Name.Format");
            return format == "Code" ? ShortName(row.Name) : StandingsOverlay.FormatName(row.Name, format);
        }

        /// <summary>Three letters of the last name in capitals, as TV graphics show drivers (ROG).</summary>
        private static string ShortName(string name)
        {
            string[] parts = (name ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string last = parts.Length > 0 ? parts[parts.Length - 1] : string.Empty;
            string letters = new string(last.Where(char.IsLetter).ToArray());
            return (letters.Length >= 3 ? letters.Substring(0, 3) : letters).ToUpperInvariant();
        }

        private static void Set(TextBlock text, string value)
        {
            if (!string.Equals(text.Text, value, StringComparison.Ordinal))
            {
                text.Text = value;
            }
        }

        // ================================================================ helpers

        /// <summary>One card or ticker entry: its box, the pieces that change, and its place.</summary>
        private sealed class Slot
        {
            public readonly TranslateTransform Shift = new TranslateTransform();
            public readonly ScaleTransform Grow = new ScaleTransform(1, 1);
            public Border Box;
            public Border Flash;
            public Border PosBox;
            public TextBlock Pos;
            public Rectangle ClassBar;
            public TextBlock Name;
            public TextBlock Laps;
            public TextBlock Make;
            public TextBlock Gap;
            public Path Trend;
            public TextBlock IRating;
            public Border LicBox;
            public Border LicRight;
            public TextBlock LicNumber;
            public TextBlock LicLetter;
            public Ellipse TyreRing;
            public Path TyreLetter;
            public TextBlock Pit;
            public TextBlock Gain;
            public Path GainArrow;
            public double X;
            public bool Player;
        }

        private sealed class SideBox
        {
            public string Kind;
            public Border Box;
            public TextBlock Label;
            public TextBlock Value;
        }

        /// <summary>The cars in the order the bar shows them, the player, and what the gaps need.</summary>
        private sealed class Field
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
