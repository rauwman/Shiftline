using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
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
        private static readonly Dictionary<string, Brush> LicenceBrushes = new Dictionary<string, Brush>(StringComparer.OrdinalIgnoreCase);

        private readonly bool _interactive;
        private StandingsOptions _options;
        private TextBlock _header;
        private Border[] _rowBackgrounds = new Border[0];
        private Border[,] _cellBoxes = new Border[0, 0];
        private TextBlock[,] _cells = new TextBlock[0, 0];
        private Ellipse[] _tyreRings = new Ellipse[0];
        private TextBlock[] _tyreLetters = new TextBlock[0];
        private TextBlock[,] _deltas = new TextBlock[0, 0];
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
                double height = _options.Rows * RowHeight + (_options.Header ? HeaderHeight + 4 : 0) + 12;
                return new Size(_designWidth + 16, height);
            }
        }

        /// <summary>Rebuilds the rows and columns. Call on the UI thread after the options changed.</summary>
        public void ApplyOptions(StandingsOptions options)
        {
            _options = options;
            List<StandingsColumn> columns = options.Columns;
            int rows = options.Rows;
            int numStyle = options.GetInt("Num.Style");
            bool smallCaps = options.Get("Name.Case") == "SmallCaps";

            Grid grid = new Grid();
            _designWidth = 0;
            foreach (StandingsColumn column in columns)
            {
                double width = options.WidthOf(column);
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
                _designWidth += width;
            }

            _designWidth = Math.Max(_designWidth, 120);
            int firstRow = 0;
            if (options.Header)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HeaderHeight + 4) });
                _header = new TextBlock { Foreground = TextBrush, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis };
                Border headerBox = new Border { Background = HeaderBrush, CornerRadius = new CornerRadius(5), Height = HeaderHeight, VerticalAlignment = VerticalAlignment.Top, Child = _header };
                Grid.SetColumnSpan(headerBox, Math.Max(1, columns.Count));
                grid.Children.Add(headerBox);
                firstRow = 1;
            }
            else
            {
                _header = null;
            }

            _rowBackgrounds = new Border[rows];
            _cellBoxes = new Border[rows, columns.Count];
            _cells = new TextBlock[rows, columns.Count];
            _tyreRings = new Ellipse[rows];
            _tyreLetters = new TextBlock[rows];
            _deltas = new TextBlock[rows, MaxDelta];
            for (int r = 0; r < rows; r++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(RowHeight) });
                Border background = new Border { CornerRadius = new CornerRadius(3), Background = r % 2 == 1 ? OddRowBrush : null };
                Grid.SetRow(background, firstRow + r);
                Grid.SetColumnSpan(background, Math.Max(1, columns.Count));
                grid.Children.Add(background);
                _rowBackgrounds[r] = background;

                for (int c = 0; c < columns.Count; c++)
                {
                    string key = columns[c].Key;
                    TextBlock text = NewText(columns[c].RightAligned);
                    UIElement content = text;
                    if (key == "Num")
                    {
                        text.FontWeight = numStyle % 2 == 1 ? FontWeights.Bold : FontWeights.Normal;
                        text.FontStyle = numStyle % 4 >= 2 ? FontStyles.Italic : FontStyles.Normal;
                    }
                    else if (key == "Name" && smallCaps)
                    {
                        Typography.SetCapitals(text, FontCapitals.SmallCaps);
                    }
                    else if (key == "Tyre")
                    {
                        content = BuildTyre(r, text);
                    }
                    else if (key == "Delta")
                    {
                        content = BuildDelta(r, text);
                    }

                    Border box = new Border
                    {
                        Margin = new Thickness(2, 3, 2, 3),
                        Padding = new Thickness(3, 0, 3, 0),
                        CornerRadius = new CornerRadius(3),
                        Child = content,
                        Background = _interactive ? Brushes.Transparent : null
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

            Border panel = new Border { Background = PanelBrush, CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 6, 8, 6), Width = _designWidth + 16, Child = grid };
            Child = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Child = panel };
        }

        private static TextBlock NewText(bool rightAligned)
        {
            return new TextBlock
            {
                Foreground = TextBrush,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = rightAligned ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
        }

        private UIElement BuildTyre(int r, TextBlock laps)
        {
            Ellipse ring = new Ellipse { Width = 16, Height = 16, StrokeThickness = 2.5, Stroke = MutedBrush, Fill = NumberBoxBrush };
            TextBlock letter = new TextBlock { FontSize = 9, FontWeight = FontWeights.Bold, Foreground = TextBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Grid icon = new Grid { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
            icon.Children.Add(ring);
            icon.Children.Add(letter);
            _tyreRings[r] = ring;
            _tyreLetters[r] = letter;
            laps.Margin = new Thickness(5, 0, 0, 0);
            laps.Foreground = MutedBrush;
            StackPanel panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(icon);
            panel.Children.Add(laps);
            return panel;
        }

        private UIElement BuildDelta(int r, TextBlock first)
        {
            StackPanel panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            int count = Math.Max(1, Math.Min(MaxDelta, _options.GetInt("Delta.Count")));
            for (int k = 0; k < count; k++)
            {
                TextBlock text = k == 0 ? first : NewText(true);
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
            List<int> shown = Choose(all, _options.Rows);
            for (int r = 0; r < _options.Rows; r++)
            {
                int index = r < shown.Count ? shown[r] : -1;
                StandingsRow row = index >= 0 ? all[index] : null;
                Brush rowBrush = row != null && row.IsPlayer ? PlayerRowBrush : (r % 2 == 1 ? OddRowBrush : null);
                if (!ReferenceEquals(_rowBackgrounds[r].Background, rowBrush))
                {
                    _rowBackgrounds[r].Background = rowBrush;
                }

                for (int c = 0; c < columns.Count; c++)
                {
                    Fill(r, c, columns[c].Key, row, index, context);
                }
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

        private void Fill(int r, int c, string column, StandingsRow row, int index, Context context)
        {
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

            if (row != null)
            {
                switch (column)
                {
                    case "Pos":
                        value = row.Position > 0 ? row.Position.ToString(CultureInfo.InvariantCulture) : "-";
                        foreground = MutedBrush;
                        break;
                    case "Gain":
                        if (row.StartPosition > 0 && row.Position > 0 && row.StartPosition != row.Position)
                        {
                            int gained = row.StartPosition - row.Position;
                            value = (gained > 0 ? "▲" : "▼") + Math.Abs(gained).ToString(CultureInfo.InvariantCulture);
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
                        break;
                    case "Gap":
                    case "Int":
                        if (options.GetBool(column + ".Show" + context.Mode))
                        {
                            StandingsRow reference = index > 0 ? (column == "Gap" ? context.Rows[0] : context.Rows[index - 1]) : null;
                            value = Gap(row, index, context, reference, options.GetInt(column + ".Dec" + context.Mode));
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
                        FormatPit(row, options.Get("Pit.Mode") != "Plain", out value, out background, out foreground);
                        break;
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

        private void FillTyre(int r, StandingsRow row, StandingsOptions options)
        {
            Ellipse ring = _tyreRings[r];
            TextBlock letter = _tyreLetters[r];
            if (ring == null)
            {
                return;
            }

            string type = row == null ? string.Empty : row.TireType ?? string.Empty;
            Visibility visible = type.Length > 0 ? Visibility.Visible : Visibility.Hidden;
            if (ring.Visibility != visible)
            {
                ring.Visibility = visible;
                letter.Visibility = visible;
            }

            if (type.Length == 0)
            {
                return;
            }

            string code;
            Brush colour;
            TyreLook(type, out code, out colour);
            SetText(letter, code);
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

        private static void FormatPit(StandingsRow row, bool withInfo, out string value, out Brush background, out Brush foreground)
        {
            background = null;
            foreground = MutedBrush;
            value = string.Empty;
            if (row.Towing)
            {
                value = "TOW";
                background = LossBrush;
                foreground = Brushes.White;
            }
            else if (row.OnPitRoad)
            {
                value = withInfo && row.PitLaneSeconds >= 0 ? "PIT " + MinutesSeconds(row.PitLaneSeconds) : "PIT";
                background = PitBrush;
                foreground = DarkText;
            }
            else if (row.TrackSurface < 0)
            {
                value = "OUT";
            }
            else if (withInfo && row.PitLap > 0)
            {
                value = "PIT L" + row.PitLap.ToString(CultureInfo.InvariantCulture);
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
        private static string Gap(StandingsRow row, int index, Context context, StandingsRow reference, int decimals)
        {
            if (reference == null || index <= 0)
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
                parts.Add("Track " + temp.ToString("0", CultureInfo.InvariantCulture) + "°C");
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

        private static Brush Frozen(byte a, byte r, byte g, byte b)
        {
            SolidColorBrush brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}
