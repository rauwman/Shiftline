using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Shiftline.Plugin
{
    /// <summary>A column of the standings overlay: its key (stored in the settings), its label and its width in the design.</summary>
    internal sealed class StandingsColumn
    {
        public const int Left = 0;
        public const int Center = 1;
        public const int Right = 2;

        public StandingsColumn(string key, string label, string shortLabel, double width, int align, bool onByDefault)
        {
            Key = key;
            Label = label;
            ShortLabel = shortLabel;
            Width = width;
            Align = align;
            OnByDefault = onByDefault;
        }

        public string Key { get; }
        public string Label { get; }          // on the settings page
        public string ShortLabel { get; }     // in the label row of the overlay
        public double Width { get; }
        public int Align { get; }
        public bool OnByDefault { get; }
    }

    /// <summary>One design choice of a column: a key, the values with their labels, and the default.</summary>
    internal sealed class StandingsChoice
    {
        public StandingsChoice(string column, string key, string label, string fallback, params string[] valuesAndLabels)
        {
            Column = column;
            Key = key;
            Label = label;
            Fallback = fallback;
            Values = new string[valuesAndLabels.Length / 2];
            Labels = new string[valuesAndLabels.Length / 2];
            for (int i = 0; i < Values.Length; i++)
            {
                Values[i] = valuesAndLabels[i * 2];
                Labels[i] = valuesAndLabels[i * 2 + 1];
            }
        }

        public string Column { get; }
        public string Key { get; }
        public string Label { get; }
        public string Fallback { get; }
        public string[] Values { get; }
        public string[] Labels { get; }
    }

    /// <summary>The design choices of the standings overlay, read from and written to OverlaySettings.</summary>
    internal sealed class StandingsOptions
    {
        public static readonly StandingsColumn[] AllColumns =
        {
            new StandingsColumn("Class", "Class colour", "", 8, StandingsColumn.Left, true),
            new StandingsColumn("Pos", "Position", "P", 28, StandingsColumn.Center, true),
            new StandingsColumn("Gain", "Places gained", "+/-", 34, StandingsColumn.Left, true),
            new StandingsColumn("Num", "Car number", "#", 38, StandingsColumn.Center, true),
            new StandingsColumn("Name", "Name", "Driver", 130, StandingsColumn.Left, true),
            new StandingsColumn("Make", "Make", "Make", 44, StandingsColumn.Center, true),
            new StandingsColumn("Lic", "Safety rating", "SR", 54, StandingsColumn.Center, true),
            new StandingsColumn("iR", "iRating", "iR", 44, StandingsColumn.Right, true),
            new StandingsColumn("Gap", "Gap", "Gap", 48, StandingsColumn.Center, true),
            new StandingsColumn("Int", "Interval", "Int", 48, StandingsColumn.Center, true),
            new StandingsColumn("Last", "Lap time", "Last", 70, StandingsColumn.Right, true),
            new StandingsColumn("Best", "Best lap", "Best", 70, StandingsColumn.Right, false),
            new StandingsColumn("Avg", "Average lap", "Avg", 70, StandingsColumn.Right, false),
            new StandingsColumn("Tyre", "Tyres", "Tyre", 44, StandingsColumn.Left, true),
            new StandingsColumn("Pit", "Pit", "Pit", 60, StandingsColumn.Center, true),
            new StandingsColumn("Stint", "Stint laps", "Stint", 36, StandingsColumn.Right, false),
            new StandingsColumn("Delta", "Gain to you", "Gain", 96, StandingsColumn.Center, true)
        };

        private static readonly string[] Decimals = { "3", "3 decimals", "2", "2 decimals", "1", "1 decimal" };
        private static readonly string[] OnOff = { "1", "On", "0", "Off" };

        // Static fields are set up in the order they are written: these must come before AllChoices, which uses them.

        /// <summary>Columns that can get a background of their own, with the default for each.</summary>
        private static readonly Dictionary<string, string> BackgroundDefaults = new Dictionary<string, string>
        {
            { "Pos", "White" }, { "Gain", "None" }, { "Num", "None" }, { "Name", "None" }, { "Make", "None" }, { "iR", "White" },
            { "Gap", "None" }, { "Int", "None" }, { "Last", "None" }, { "Best", "Subtle" }, { "Avg", "Subtle" }, { "Tyre", "Subtle" }, { "Stint", "None" }
        };

        public static readonly StandingsChoice[] AllChoices = BaseChoices().Concat(ColumnLookChoices()).ToArray();

        /// <summary>Font, weight and background per column; "Default" follows the choices for the whole overlay.</summary>
        private static IEnumerable<StandingsChoice> ColumnLookChoices()
        {
            foreach (StandingsColumn column in AllColumns)
            {
                if (column.Key == "Class")
                {
                    continue;
                }

                string fallback;
                if (BackgroundDefaults.TryGetValue(column.Key, out fallback))
                {
                    yield return new StandingsChoice(column.Key, column.Key + ".Bg", "Background", fallback,
                        "None", "None", "Subtle", "Subtle grey", "Light", "Light grey", "White", "White, dark text");
                }

                yield return new StandingsChoice(column.Key, column.Key + ".Font", "Font", "Default",
                    "Default", "Same as the overlay", "Segoe UI", "Segoe UI", "Saira SemiCondensed", "Saira Semi Condensed", "Inter", "Inter",
                    "Arial", "Arial", "Verdana", "Verdana", "Tahoma", "Tahoma", "Consolas", "Consolas");
                yield return new StandingsChoice(column.Key, column.Key + ".Weight", "Font weight", "Default",
                    "Default", "Same as the overlay", "Normal", "Normal", "SemiBold", "Semi-bold", "Bold", "Bold");
            }
        }

        private static StandingsChoice[] BaseChoices()
        {
            return new[]
            {
            new StandingsChoice("Num", "Num.Style", "Look", "1",
                "0", "Normal", "1", "Bold", "2", "Italic", "3", "Bold italic", "4", "Normal in a box", "5", "Bold in a box", "6", "Italic in a box", "7", "Bold italic in a box"),
            new StandingsChoice("Name", "Name.Format", "Format", "FirstLast",
                "Full", "Joshua K Rogers", "FirstLast", "Joshua Rogers", "InitialLast", "J. Rogers", "Last", "Rogers"),
            new StandingsChoice("Name", "Name.Case", "Style", "Normal",
                "Normal", "Normal", "SmallCaps", "Small caps", "Upper", "UPPERCASE"),
            new StandingsChoice("Name", "Name.Width", "Maximum width", "130",
                "100", "100 px", "130", "130 px", "160", "160 px", "190", "190 px", "220", "220 px", "260", "260 px"),
            new StandingsChoice("Lic", "Lic.Mode", "Look", "Full",
                "Full", "Class and rating (A3.48)", "Short", "Class and rating, 1 decimal (A3.4)", "Round", "Class and whole rating (A3)", "Letter", "Class only (A)", "Number", "Rating only, in class colour (3.48)"),
            new StandingsChoice("iR", "iR.Mode", "Look", "Full",
                "Full", "Full value (4567)", "K1", "Thousands, 1 decimal (4.5k)", "K0", "Whole thousands (5k)"),
            new StandingsChoice("iR", "iR.Gain", "Expected change if the race ended now (estimate)", "1", OnOff),
            new StandingsChoice("Gap", "Gap.DecRace", "Decimals in races", "1", Decimals),
            new StandingsChoice("Gap", "Gap.DecPQ", "Decimals in practice and qualifying", "3", Decimals),
            new StandingsChoice("Gap", "Gap.ShowRace", "Show in races", "1", OnOff),
            new StandingsChoice("Gap", "Gap.ShowPQ", "Show in practice and qualifying", "1", OnOff),
            new StandingsChoice("Int", "Int.DecRace", "Decimals in races", "1", Decimals),
            new StandingsChoice("Int", "Int.DecPQ", "Decimals in practice and qualifying", "3", Decimals),
            new StandingsChoice("Int", "Int.ShowRace", "Show in races", "1", OnOff),
            new StandingsChoice("Int", "Int.ShowPQ", "Show in practice and qualifying", "1", OnOff),
            new StandingsChoice("Last", "Last.DecRace", "Decimals in races", "3", Decimals),
            new StandingsChoice("Last", "Last.DecPQ", "Decimals in practice and qualifying", "3", Decimals),
            new StandingsChoice("Avg", "Avg.Laps", "Average over", "5", "3", "the last 3 laps", "5", "the last 5 laps"),
            new StandingsChoice("Tyre", "Tyre.Laps", "Laps on the tyres", "1", OnOff),
            new StandingsChoice("Pit", "Pit.Mode", "Look", "Info",
                "Info", "With time in the pit lane or the lap of the last stop (PIT 0:23)", "Plain", "Word only (PIT)"),
            new StandingsChoice("Delta", "Delta.Count", "Number of laps", "3",
                "1", "1 lap", "2", "2 laps", "3", "3 laps", "4", "4 laps", "5", "5 laps"),
            new StandingsChoice("Look", "Opacity", "Background opacity", "90",
                "100", "100 %", "90", "90 %", "80", "80 %", "70", "70 %", "60", "60 %", "50", "50 %", "40", "40 %", "30", "30 %", "20", "20 %", "0", "0 % (no background)"),
            new StandingsChoice("Look", "RowGap", "Space between rows", "0",
                "0", "None (one panel)", "1", "1 px", "2", "2 px", "3", "3 px", "4", "4 px"),
            new StandingsChoice("Look", "Font", "Font", "Segoe UI",
                "Segoe UI", "Segoe UI", "Saira SemiCondensed", "Saira Semi Condensed", "Inter", "Inter", "Arial", "Arial", "Verdana", "Verdana", "Tahoma", "Tahoma", "Consolas", "Consolas"),
            new StandingsChoice("Look", "Corners", "Background boxes", "Rounded",
                "Rounded", "Rounded", "Square", "Square"),
            new StandingsChoice("Look", "Weight", "Font weight", "Normal",
                "Normal", "Normal", "SemiBold", "Semi-bold", "Bold", "Bold"),
            new StandingsChoice("Header", "H.Session", "Session (R, Q, P)", "1", OnOff),
            new StandingsChoice("Header", "H.Track", "Track", "1", OnOff),
            new StandingsChoice("Header", "H.Car", "Your car", "1", OnOff),
            new StandingsChoice("Header", "H.Laps", "Laps or time left", "1", OnOff),
            new StandingsChoice("Header", "H.Temp", "Track temperature", "1", OnOff),
            new StandingsChoice("Header", "H.Sof", "Strength of field", "1", OnOff),
            new StandingsChoice("Header", "H.Inc", "Your incidents", "1", OnOff),
            new StandingsChoice("Header", "H.SimTime", "Time of day in the sim (sun or moon)", "1", OnOff),
            new StandingsChoice("Header", "H.Clock", "Time on this PC (clock)", "1", OnOff),
            new StandingsChoice("Footer", "F.Last", "LAST: your last lap", "1", OnOff),
            new StandingsChoice("Footer", "F.Delta", "Live delta to your best lap", "1", OnOff),
            new StandingsChoice("Footer", "F.Best", "BEST: your best lap", "1", OnOff),
            new StandingsChoice("Footer", "F.Session", "SESSION: best lap of the session", "1", OnOff),
            new StandingsChoice("General", "Classes", "Classes in multi-class races", "Own",
                "Own", "Only my class", "ByClass", "All cars, sorted by class", "Grouped", "My class first, then the leaders of the other classes", "Mixed", "All classes mixed (overall order)"),
            new StandingsChoice("Animation", "Anim.Car", "When a car changes position, in the car", "Normal",
                "Off", "Off", "Fast", "Fast", "Normal", "Normal", "Slow", "Slow"),
            new StandingsChoice("Animation", "Anim.Replay", "When a car changes position, in replays", "Off",
                "Off", "Off", "Fast", "Fast", "Normal", "Normal", "Slow", "Slow")
            };
        }

        public static readonly int[] RowChoices = Enumerable.Range(3, 38).ToArray();

        public static readonly int[] ScaleChoices = Enumerable.Range(10, 51).Select(i => i * 5).ToArray();   // 50 % to 300 %

        private const string Key = OverlayData.StandingsKey;

        private Dictionary<string, string> _values;

        public List<StandingsColumn> Columns { get; private set; }
        public int Rows { get; private set; }
        public bool Header { get; private set; }
        public bool Footer { get; private set; }
        public bool Labels { get; private set; }
        public double Scale { get; private set; }

        /// <summary>The same options with another number of rows (while the window is being resized).</summary>
        public StandingsOptions WithRows(int rows)
        {
            return new StandingsOptions { Columns = Columns, Rows = Math.Max(3, Math.Min(40, rows)), Header = Header, Footer = Footer, Labels = Labels, Scale = Scale, Features = Features, _values = _values };
        }

        /// <summary>The columns that need something the game does not deliver, for example the iRating.</summary>
        public static StandingsFeatures FeatureOf(string column)
        {
            switch (column)
            {
                case "iR":
                    return StandingsFeatures.IRating;
                case "Lic":
                    return StandingsFeatures.Licence;
                case "Tyre":
                    return StandingsFeatures.Tyres;
                default:
                    return StandingsFeatures.None;
            }
        }

        /// <summary>The same options without the columns the current game cannot fill.</summary>
        public StandingsOptions ForFeatures(StandingsFeatures features)
        {
            List<StandingsColumn> columns = Columns.Where(c => (FeatureOf(c.Key) & ~features) == 0).ToList();
            return new StandingsOptions { Columns = columns, Rows = Rows, Header = Header, Footer = Footer, Labels = Labels, Scale = Scale, Features = features, _values = _values };
        }

        /// <summary>What the game delivers; set by ForFeatures, everything otherwise.</summary>
        public StandingsFeatures Features { get; private set; } = StandingsFeatures.All;

        /// <summary>The same options at another size (while the window is being pulled wider).</summary>
        public StandingsOptions WithScale(double scale)
        {
            return new StandingsOptions { Columns = Columns, Rows = Rows, Header = Header, Footer = Footer, Labels = Labels, Scale = Math.Max(0.5, Math.Min(3, scale)), Features = Features, _values = _values };
        }

        /// <summary>All columns in the order the user arranged them (new columns go to the place they have in AllColumns).</summary>
        public static List<StandingsColumn> OrderedColumns()
        {
            string saved = OverlaySettings.GetOption(Key, "Order", string.Empty);
            List<StandingsColumn> ordered = new List<StandingsColumn>();
            foreach (string key in saved.Split(','))
            {
                StandingsColumn column = AllColumns.FirstOrDefault(c => c.Key == key);
                if (column != null && !ordered.Contains(column))
                {
                    ordered.Add(column);
                }
            }

            for (int i = 0; i < AllColumns.Length; i++)
            {
                if (!ordered.Contains(AllColumns[i]))
                {
                    // insert after the column that comes before it by default, or at the start
                    int after = i == 0 ? -1 : ordered.IndexOf(AllColumns[i - 1]);
                    ordered.Insert(after + 1, AllColumns[i]);
                }
            }

            return ordered;
        }

        /// <summary>Saves the order of the columns (keys of all columns, first to last).</summary>
        public static void SetOrder(IEnumerable<string> keys)
        {
            OverlaySettings.SetOption(Key, "Order", string.Join(",", keys));
        }

        /// <summary>Puts the columns back into their default order; which columns are on stays.</summary>
        public static void ResetOrder()
        {
            SetOrder(AllColumns.Select(c => c.Key));
        }

        public static StandingsOptions Load()
        {
            string defaults = string.Join(",", AllColumns.Where(c => c.OnByDefault).Select(c => c.Key));
            HashSet<string> on = new HashSet<string>(OverlaySettings.GetOption(Key, "Columns", defaults).Split(','), StringComparer.Ordinal);
            int rows;
            if (!int.TryParse(OverlaySettings.GetOption(Key, "Rows", "12"), NumberStyles.Integer, CultureInfo.InvariantCulture, out rows))
            {
                rows = 12;
            }

            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (StandingsChoice choice in AllChoices)
            {
                string value = OverlaySettings.GetOption(Key, choice.Key, choice.Fallback);
                values[choice.Key] = choice.Values.Contains(value) ? value : choice.Fallback;
            }

            return new StandingsOptions
            {
                Columns = OrderedColumns().Where(c => on.Contains(c.Key)).ToList(),
                Rows = Math.Max(3, Math.Min(40, rows)),
                Header = OverlaySettings.GetOption(Key, "Header", "1") == "1",
                Footer = OverlaySettings.GetOption(Key, "Footer", "1") == "1",
                Labels = OverlaySettings.GetOption(Key, "Labels", "0") == "1",
                Scale = ScaleOf(OverlaySettings.GetOption(Key, "Scale", "100")),
                _values = values
            };
        }

        public string Get(string choice)
        {
            string value;
            return _values.TryGetValue(choice, out value) ? value : string.Empty;
        }

        public int GetInt(string choice)
        {
            int value;
            return int.TryParse(Get(choice), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : 0;
        }

        public bool GetBool(string choice)
        {
            return Get(choice) == "1";
        }

        /// <summary>The width a column takes with the current choices (the name and the gain list can change it).</summary>
        public double WidthOf(StandingsColumn column)
        {
            switch (column.Key)
            {
                case "Name":
                    return Math.Max(60, GetInt("Name.Width"));
                case "Delta":
                    return 32 * Math.Max(1, GetInt("Delta.Count"));
                case "Tyre":
                    return GetBool("Tyre.Laps") ? 50 : 26;
                case "Pit":
                    return Get("Pit.Mode") == "Plain" ? 40 : 56;
                case "iR":
                    return GetBool("iR.Gain") ? 74 : column.Width;
                default:
                    return column.Width;
            }
        }

        /// <summary>Forgets every design choice of the standings overlay (switch, position and update rate stay).</summary>
        public static void Reset()
        {
            OverlaySettings.ClearOptions(Key);
        }

        public static void SetChoice(string choice, string value)
        {
            OverlaySettings.SetOption(Key, choice, value);
        }

        public static bool IsColumnOn(string column)
        {
            return Load().Columns.Any(c => c.Key == column);
        }

        public static void SetColumn(string column, bool on)
        {
            HashSet<string> keys = new HashSet<string>(Load().Columns.Select(c => c.Key), StringComparer.Ordinal);
            if (on)
            {
                keys.Add(column);
            }
            else
            {
                keys.Remove(column);
            }

            // The order lives in its own option, so the overlay does not depend on the click order.
            OverlaySettings.SetOption(Key, "Columns", string.Join(",", AllColumns.Where(c => keys.Contains(c.Key)).Select(c => c.Key)));
        }

        public static void SetRows(int rows)
        {
            OverlaySettings.SetOption(Key, "Rows", rows.ToString(CultureInfo.InvariantCulture));
        }

        private static double ScaleOf(string text)
        {
            int percent;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out percent))
            {
                percent = 100;
            }

            return Math.Max(50, Math.Min(300, percent)) / 100.0;
        }

        public static void SetLabels(bool on)
        {
            OverlaySettings.SetOption(Key, "Labels", on ? "1" : "0");
        }

        /// <summary>Saves a size, rounded to 5 %.</summary>
        public static void SetScale(double scale)
        {
            int percent = (int)Math.Round(Math.Max(0.5, Math.Min(3, scale)) * 20) * 5;
            SetScale(percent);
        }

        public static void SetScale(int percent)
        {
            OverlaySettings.SetOption(Key, "Scale", percent.ToString(CultureInfo.InvariantCulture));
        }

        public static void SetFooter(bool on)
        {
            OverlaySettings.SetOption(Key, "Footer", on ? "1" : "0");
        }

        public static void SetHeader(bool on)
        {
            OverlaySettings.SetOption(Key, "Header", on ? "1" : "0");
        }
    }
}
