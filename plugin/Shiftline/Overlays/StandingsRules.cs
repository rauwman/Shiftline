using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
    /// The rules of the standings and the relatives, without any drawing: which cars fill the rows (BuildEntries,
    /// BuildRelative), what each cell shows (CellOf), colours, column widths and formats. StandingsHtmlView sends them to the
    /// page (web/standings.js), which draws them.
    /// </summary>
    internal static partial class StandingsModel
    {
        private const double RowHeight = 24;
        private const double HeaderHeight = 26;
        private const double LabelHeight = 18;
        private const double FooterHeight = 26;
        private const int AroundPlayer = 3;           // rows kept for the player and the cars right before and after
        private const int MaxDelta = 5;

        private static readonly Brush TextBrush = Frozen(0xFF, 0xE8, 0xEC, 0xF1);
        private static readonly Brush MutedBrush = Frozen(0xFF, 0x8E, 0x98, 0xA6);
        private static readonly Brush PanelBrush = Frozen(0xE6, 0x0E, 0x12, 0x19);
        private static readonly Brush PlayerRowBrush = Frozen(0x55, 0x3F, 0xA9, 0xF5);
        private static readonly Brush PurpleBrush = Frozen(0xFF, 0xC0, 0x7C, 0xFF);
        private static readonly Brush GreenBrush = Frozen(0xFF, 0x5E, 0xE0, 0x7A);
        private static readonly Brush LossBrush = Frozen(0xFF, 0xF0, 0x5A, 0x4A);
        private static readonly Brush PitBrush = Frozen(0xFF, 0xFF, 0xB0, 0x20);
        private static readonly Brush NumberBoxBrush = Frozen(0xFF, 0x05, 0x06, 0x08);
        private static readonly Brush DarkText = Frozen(0xFF, 0x10, 0x12, 0x16);
        private static readonly Brush WetBrush = Frozen(0xFF, 0x3F, 0xA9, 0xF5);
        private static readonly Brush WhiteFill = Frozen(0xFF, 0xF9, 0xF9, 0xF9);
        // Best lap and average lap get their own text colours so they read apart from the lap time. Both are light enough to stay
        // readable on the grey backgrounds; on the white background they turn dark like every other value.
        private static readonly Brush BestLapBrush = Frozen(0xFF, 0xC4, 0xCB, 0xD6);
        private static readonly Brush AverageBrush = Frozen(0xFF, 0x9F, 0xD0, 0xFF);
        private static readonly Brush DarkGreenBrush = Frozen(0xFF, 0x1B, 0x8A, 0x3A);
        private static readonly Brush DarkRedBrush = Frozen(0xFF, 0xC6, 0x28, 0x28);
        private static readonly Geometry ChevronUp = FrozenGeometry("M 0.5,5.5 L 4,2 L 7.5,5.5");
        private static readonly Geometry ChevronDown = FrozenGeometry("M 0.5,2 L 4,5.5 L 7.5,2");
        private static readonly Dictionary<string, Brush> LicenceBrushes = new Dictionary<string, Brush>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, Geometry> LetterShapes = new Dictionary<string, Geometry>();

        /// <summary>Height of one row plus the gap below it.</summary>
        private static double Pitch(StandingsOptions options)
        {
            return RowHeight + Math.Max(0, options.GetInt("RowGap"));
        }

        private static double FixedHeight(StandingsOptions options)
        {
            return (options.Header ? HeaderHeight + 4 : 0) + (options.Footer ? FooterHeight + 4 : 0) + (options.Labels ? LabelHeight : 0) + 12;
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

        private const double FlagWidth = 18;
        private const double FlagHeight = 13.5;

        /// <summary>
        /// A thin dark edge around text (a shadow without offset): keeps white letters readable on light colours such as the
        /// yellow of a C licence. Shared by every text that uses it.
        /// </summary>
        internal static readonly System.Windows.Media.Effects.DropShadowEffect TextOutline = CreateOutline();

        /// <summary>A thin glass edge around the whole pill, brightest at the top, like the edge of the plugin's cards.</summary>
        private static readonly Brush LicenceEdge = FrozenGradient(
            Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF), 0,
            Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF), 0.45,
            Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF), 1);

        private static Brush FrozenGradient(Color top, double topAt, Color middle, double middleAt, Color bottom, double bottomAt)
        {
            LinearGradientBrush brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            brush.GradientStops.Add(new GradientStop(top, topAt));
            brush.GradientStops.Add(new GradientStop(middle, middleAt));
            brush.GradientStops.Add(new GradientStop(bottom, bottomAt));
            brush.Freeze();
            return brush;
        }

        /// <summary>
        /// The content of a licence pill with the glass edge laid over it. The edge covers both parts together, so the darker
        /// rating and the brighter letter read as one block of the same height. Shared with the Battle Bar.
        /// </summary>
        internal static Grid WithGloss(UIElement content, double radius)
        {
            Grid grid = new Grid();
            grid.Children.Add(content);
            grid.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(radius),
                BorderBrush = LicenceEdge,
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false
            });
            return grid;
        }

        /// <summary>The weight of the safety rating column: its own choice, else the weight of the overlay.</summary>
        private static FontWeight LicenceWeight(StandingsOptions options)
        {
            string weight = options.Get("Lic.Weight");
            return WeightOf(string.IsNullOrEmpty(weight) || weight == "Default" ? options.Get("Weight") : weight);
        }

        /// <summary>One step heavier, for the class letter next to the number.</summary>
        private static FontWeight Heavier(FontWeight weight)
        {
            return weight == FontWeights.Normal ? FontWeights.SemiBold : FontWeights.Bold;
        }

        internal const string InkDigits = "0123456789";
        internal const string InkLetters = "ABCDPRW";

        /// <summary>
        /// Centres the visible glyphs of a text instead of its line box. A line box keeps room below the baseline for letters
        /// such as g or p; digits and capitals never use it, so they look a little low. This measures the real ink of sample
        /// glyphs in the text's own font and moves the text by the difference (a render transform, the layout does not change).
        /// Measured again when the text is loaded or its font size changes, so every font gets its own value.
        /// </summary>
        internal static void CentreInk(TextBlock text, string sample)
        {
            RoutedEventHandler measure = (s, e) => text.RenderTransform = InkShift(text, sample);
            text.Loaded += measure;
            text.SizeChanged += (s, e) =>
            {
                if (e.HeightChanged)
                {
                    measure(s, null);
                }
            };
        }

        private static readonly Dictionary<string, Transform> InkShifts = new Dictionary<string, Transform>();

        private static Transform InkShift(TextBlock text, string sample)
        {
            string key = text.FontFamily.Source + "|" + text.FontWeight + "|" + text.FontSize.ToString(CultureInfo.InvariantCulture) + "|" + sample;
            Transform shift;
            if (InkShifts.TryGetValue(key, out shift))
            {
                return shift;
            }

            double pixelsPerDip = VisualTreeHelper.GetDpi(text).PixelsPerDip;
            FormattedText formatted = new FormattedText(sample, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize, Brushes.White, pixelsPerDip);
            Rect ink = formatted.BuildGeometry(new Point(0, 0)).Bounds;
            double lineHeight = text.FontFamily.LineSpacing * text.FontSize;
            double offset = ink.IsEmpty ? 0 : lineHeight / 2 - (ink.Top + ink.Bottom) / 2;
            TranslateTransform move = new TranslateTransform(0, offset);
            move.Freeze();
            InkShifts[key] = move;
            return move;
        }

        private static readonly Brush LightLetter = Frozen(0xFF, 0xF4, 0xF6, 0xF9);
        private static readonly Brush DarkLetter = Frozen(0xFF, 0x1A, 0x1D, 0x23);
        private static readonly Dictionary<Brush, Brush> Softened = new Dictionary<Brush, Brush>();
        private static readonly Dictionary<Brush, Brush> Contrasts = new Dictionary<Brush, Brush>();

        /// <summary>The licence colour toned down a little (85 % colour, 15 % dark), so it sits closer to the shade beside it.</summary>
        internal static Brush Soften(Brush colour)
        {
            Brush soft;
            if (!Softened.TryGetValue(colour, out soft))
            {
                Color c = ((SolidColorBrush)colour).Color;
                soft = Frozen(0xFF, (byte)(c.R * 0.85 + 0x12 * 0.15), (byte)(c.G * 0.85 + 0x18 * 0.15), (byte)(c.B * 0.85 + 0x2A * 0.15));
                Softened[colour] = soft;
            }

            return soft;
        }

        /// <summary>
        /// Near-white or dark grey text, whichever has the higher contrast with the background by the WCAG formula
        /// (relative luminance, (L1 + 0.05) / (L2 + 0.05)): light on blue and green, dark on yellow and orange.
        /// </summary>
        internal static Brush ContrastText(Brush background)
        {
            Brush text;
            if (!Contrasts.TryGetValue(background, out text))
            {
                double bg = Luminance(((SolidColorBrush)background).Color);
                double withLight = (Luminance(((SolidColorBrush)LightLetter).Color) + 0.05) / (bg + 0.05);
                double withDark = (bg + 0.05) / (Luminance(((SolidColorBrush)DarkLetter).Color) + 0.05);
                text = withLight >= withDark ? LightLetter : DarkLetter;
                Contrasts[background] = text;
            }

            return text;
        }

        private static double Luminance(Color c)
        {
            return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
        }

        private static double Linear(byte channel)
        {
            double v = channel / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        private static System.Windows.Media.Effects.DropShadowEffect CreateOutline()
        {
            System.Windows.Media.Effects.DropShadowEffect outline = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                ShadowDepth = 0,
                BlurRadius = 2.5,
                Opacity = 0.9
            };
            outline.Freeze();
            return outline;
        }

        private static readonly Dictionary<Brush, Brush> Shades = new Dictionary<Brush, Brush>();

        /// <summary>The licence colour mixed with the dark of the overlay: the background of the rating in the licence pill.</summary>
        internal static Brush Shade(Brush colour)
        {
            Brush shade;
            if (!Shades.TryGetValue(colour, out shade))
            {
                Color c = ((SolidColorBrush)colour).Color;
                // 60 % licence colour, 40 % the dark of the overlay, solid: a deep version of the colour, close to the letter part
                shade = Frozen(0xFF, (byte)(c.R * 0.6 + 0x12 * 0.4), (byte)(c.G * 0.6 + 0x18 * 0.4), (byte)(c.B * 0.6 + 0x2A * 0.4));
                Shades[colour] = shade;
            }

            return shade;
        }

        /// <summary>
        /// The width a column needs for its longest plausible value in this font. The iRating and delta columns also give the
        /// width of their parts (rating, change, one delta).
        /// </summary>
        internal static double RequiredWidth(string column, StandingsOptions options, FontFamily family, FontWeight weight, bool inRace,
            ref double ratingWidth, ref double changeWidth, ref double deltaWidth)
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
                case "Flag":
                    return FlagWidth + Cell;
                case "Make":
                    return Measure("WWW", family, weight) + Cell;
                case "Lic":
                    return options.Get("Lic.Mode") == "Split"
                        ? (options.GetBool("Lic.Number") ? Measure("0.0", family, weight) + 6 : 0) + Measure("W", family, Heavier(weight)) + 10 + 4
                        : Measure("W0.00", family, weight) + Cell + 2;
                case "iR":
                    // normal values: below 10k and a change of at most two digits (the pill grows for the rare larger one)
                    string longest = options.Get("iR.Mode") == "K1" ? "0.0k" : options.Get("iR.Mode") == "K0" ? "0k" : "0000";
                    ratingWidth = Math.Ceiling(Measure(longest, family, weight)) + 2;
                    changeWidth = Math.Ceiling(Measure("00", family, weight)) + 2;
                    return options.GetBool("iR.Gain") && inRace
                        ? ratingWidth + 12 + changeWidth + 6 + Cell
                        : ratingWidth + 6 + Cell;
                case "Lap":
                    return Measure("000", family, weight) + Cell;
                case "Gap":
                case "Int":
                    if (options.IsRelative)
                    {
                        int own = options.GetInt("Rel.Dec");
                        return Measure(own >= 3 ? "-000.000" : own == 2 ? "-000.00" : "-000.0", family, weight) + 6;
                    }

                    // a little less room than other cells: two centred numbers side by side looked far apart
                    return Measure(gapDecimals >= 3 ? "000.000" : gapDecimals == 2 ? "000.00" : "000.0", family, weight) + 4;
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
                    deltaWidth = Math.Max(28, Measure("00.0", family, weight) + 6);
                    return deltaWidth * Math.Max(1, options.GetInt("Delta.Count")) + Cell;
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

        internal static double AnimationSeconds(StandingsOptions options, bool replay)
        {
            switch (options.Get(replay ? "Anim.Replay" : "Anim.Car"))
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
            public StandingsRow[] Tab;      // not a car but the tab of a class block: the cars of that class
            public bool Titles;             // not a car but the row with the titles over the lap times
            public string GapText;          // relatives: the gap to the player, "-" in front and "+" behind
            public Brush GapBrush;          // relatives: colour of the gap, by the lap the car is on
        }

        private static Entry TabOf(IEnumerable<StandingsRow> rows)
        {
            return new Entry { Tab = rows.ToArray() };
        }

        private static readonly Dictionary<Brush, Brush> SoftBrushes = new Dictionary<Brush, Brush>();

        private static readonly Dictionary<byte, Brush> DarkBoxes = new Dictionary<byte, Brush>();

        /// <summary>The dark of the overlay with the opacity that makes card and box together cover about 90 %.</summary>
        private static Brush DarkBox(double card)
        {
            byte alpha = card >= 0.9 ? (byte)0 : (byte)Math.Round(255 * (1 - 0.1 / (1 - card)));
            Brush box;
            if (!DarkBoxes.TryGetValue(alpha, out box))
            {
                box = alpha == 0 ? null : Frozen(alpha, 0x0E, 0x12, 0x19);
                DarkBoxes[alpha] = box;
            }

            return box;
        }

        /// <summary>The backgrounds of card, rows and columns for these options (shared by WPF and HTML).</summary>
        internal sealed class RowLook
        {
            public Brush Card;          // behind header, table and footer
            public Brush Row;           // the darker rows
            public Brush Odd;           // the first car and every second one
            public Brush Plain;         // a row with only the titles
            public Brush Player;        // your row
            public Brush Subtle;        // column colour "Subtle"
            public Brush Light;         // column colour "Light"
            public Brush HeaderFooter;
            public double CardOpacity;
        }

        internal static RowLook LookOf(StandingsOptions options)
        {
            // Three backgrounds with their own opacity: the card behind everything, the bar of each row, header and footer.
            byte alpha = (byte)Math.Round(255 * Math.Max(0, Math.Min(100, options.GetInt("Opacity"))) / 100.0);
            byte hfAlpha = (byte)Math.Round(255 * Math.Max(0, Math.Min(100, options.GetInt("HfOpacity"))) / 100.0);
            byte rowAlpha = (byte)Math.Round(255 * Math.Max(0, Math.Min(100, options.GetInt("RowOpacity"))) / 100.0);
            Brush dark = rowAlpha == 0 ? null : Frozen(rowAlpha, 0x0E, 0x12, 0x19);
            Brush light = rowAlpha == 0 ? null : Frozen(rowAlpha, 0x22, 0x29, 0x36);
            RowLook look = new RowLook
            {
                Card = alpha == 0 ? null : Frozen(alpha, 0x0E, 0x12, 0x19),
                Plain = options.Get("RowStyle") == "Mine" ? null : dark,
                CardOpacity = alpha / 255.0,
                HeaderFooter = Frozen(hfAlpha, 0x16, 0x1C, 0x26),
                // your row: the row bar tinted blue, at least as strong as the other rows (alone it keeps the old see-through blue)
                Player = rowAlpha == 0 ? PlayerRowBrush : Frozen(Math.Max(rowAlpha, (byte)0x55), 0x1F, 0x4E, 0x70),
                // column colours: lighter steps of the row bar with its opacity, so they always go with the rows
                Subtle = rowAlpha == 0 ? null : Frozen(rowAlpha, 0x2A, 0x32, 0x40),
                Light = rowAlpha == 0 ? null : Frozen(rowAlpha, 0x42, 0x4B, 0x5A),
            };
            switch (options.Get("RowStyle"))
            {
                case "Solid":
                    look.Odd = dark;
                    look.Row = dark;
                    break;
                case "Reversed":
                    look.Odd = dark;
                    look.Row = light;
                    break;
                case "Mine":
                    look.Odd = null;
                    look.Row = null;
                    break;
                default:
                    look.Odd = light;        // the first car is the lighter one
                    look.Row = dark;
                    break;
            }

            return look;
        }

        /// <summary>The fill of a column for its "Bg" option.</summary>
        internal static Brush ColumnFill(string fill, RowLook look)
        {
            return fill == "White" ? WhiteFill : fill == "Light" ? look.Light : fill == "Subtle" ? look.Subtle : null;
        }

        private static Brush SoftBrush(Brush colour)
        {
            Brush soft;
            if (!SoftBrushes.TryGetValue(colour, out soft))
            {
                Color c = ((SolidColorBrush)colour).Color;
                soft = Frozen(0x55, c.R, c.G, c.B);
                SoftBrushes[colour] = soft;
            }

            return soft;
        }

        /// <summary>In a tab row the room right of the tab names the lap time columns, so they can be told apart.</summary>
        /// <summary>The title over a lap time column (LAST, BEST, AVG 5), or null for other columns.</summary>
        internal static string TimeTitle(string column, StandingsOptions options)
        {
            switch (column)
            {
                case "Last":
                    return "LAST";
                case "Best":
                    return "BEST";
                case "Avg":
                    return "AVG " + (options.GetInt("Avg.Laps") == 3 ? "3" : "5");
                default:
                    return null;
            }
        }

        /// <summary>The class tab of a row: class name in the class colour, optionally the number of cars and the class SOF.</summary>
        /// <summary>The text of a class tab: the class name, optionally the number of cars and the strength of field.</summary>
        internal static string TabText(StandingsRow[] cars, StandingsFeatures features, StandingsOptions options)
        {
            StandingsRow first = cars[0];
            string text = string.IsNullOrEmpty(first.ClassName) ? "Class" : first.ClassName;
            if (options.GetBool("Tab.Count"))
            {
                text += "  \u00B7  " + cars.Length.ToString(CultureInfo.InvariantCulture);
            }

            int sof = (features & StandingsFeatures.IRating) != 0 ? SessionTracker.StrengthOfField(cars) : 0;
            if (options.GetBool("Tab.Sof") && sof > 0)
            {
                text += "  \u00B7  " + (sof / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + "k";
            }

            return text;
        }

        /// <summary>
        /// The rows to show. Mixed: the overall order. Own: only the player's class. Grouped: the player's class first, then the
        /// leaders of every other class, each block separated by an empty row. Inside the player's block the leaders are shown,
        /// and when the player is further back the last rows show the player and the cars right before and after.
        /// </summary>
        private static List<Entry> BuildEntries(StandingsRow[] all, int count, string mode, bool tabs)
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
                    if (k > 0 || tabs)
                    {
                        if (entries.Count + 2 > count)
                        {
                            break;
                        }

                        // with tabs each block starts with its tab, otherwise an empty row separates the blocks
                        entries.Add(tabs ? TabOf(classes[k]) : null);
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
            int ownRows = Math.Max(3, count - others * (OtherRows + 1) - (tabs ? 1 : 0));
            if (tabs)
            {
                entries.Add(TabOf(own));
            }

            AddBlock(entries, own.ToArray(), ownRows, true);
            foreach (List<StandingsRow> other in classes)
            {
                if (ReferenceEquals(other, own) || entries.Count + 2 > count)
                {
                    continue;
                }

                entries.Add(tabs ? TabOf(other) : null);
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

        private static void SetArrow(Path arrow, int gain, Brush brush)
        {
            if (arrow == null)
            {
                return;
            }

            Geometry shape = gain > 0 ? ChevronUp : gain < 0 ? ChevronDown : null;
            if (!ReferenceEquals(arrow.Data, shape))
            {
                arrow.Data = shape;
            }

            if (!ReferenceEquals(arrow.Stroke, brush))
            {
                arrow.Stroke = brush;
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
        private static readonly Brush NoClassBrush = FrozenColour(0x3F, 0xA9, 0xF5);

        private static Brush FrozenColour(byte r, byte g, byte b)
        {
            SolidColorBrush brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        /// <summary>
        /// The colour of a class. Without a class colour, or when the game sends plain white or black (offline races, a single
        /// class), the accent blue of Shiftline is shown instead.
        /// </summary>
        internal static Brush ClassBrush(string value)
        {
            SolidColorBrush brush = LicenceBrush(value) as SolidColorBrush;
            if (brush == null || brush.Color == Color.FromRgb(0xFF, 0xFF, 0xFF) || brush.Color == Color.FromRgb(0, 0, 0))
            {
                return NoClassBrush;
            }

            return brush;
        }

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
