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

        /// <summary>The overlay this choice belongs to; null when both the standings and the relatives use it.</summary>
        public string Only { get; set; }
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
            new StandingsColumn("Flag", "Country flag", "", 30, StandingsColumn.Center, false),
            new StandingsColumn("Name", "Name", "Driver", 130, StandingsColumn.Left, true),
            new StandingsColumn("Make", "Make", "Make", 44, StandingsColumn.Center, true),
            new StandingsColumn("Lic", "Safety rating", "SR", 40, StandingsColumn.Center, true),
            new StandingsColumn("iR", "iRating", "iR", 44, StandingsColumn.Right, true),
            new StandingsColumn("Gap", "Gap", "Gap", 38, StandingsColumn.Center, true),
            new StandingsColumn("Int", "Interval", "Int", 38, StandingsColumn.Center, true),
            new StandingsColumn("Last", "Lap time", "Last", 70, StandingsColumn.Right, true),
            new StandingsColumn("Best", "Best lap", "Best", 70, StandingsColumn.Right, true),
            new StandingsColumn("Avg", "Average lap", "Avg", 70, StandingsColumn.Right, false),
            new StandingsColumn("Tyre", "Tyres", "Tyre", 44, StandingsColumn.Left, true),
            new StandingsColumn("Pit", "Pit", "Pit", 60, StandingsColumn.Center, true),
            new StandingsColumn("Stint", "Stint laps", "Stint", 36, StandingsColumn.Right, false),
            new StandingsColumn("Delta", "Gain to you", "Gain", 96, StandingsColumn.Center, true)
        };

        /// <summary>The columns of the relatives: the same cells as the standings, with the gap to you instead of to the leader.</summary>
        public static readonly StandingsColumn[] RelativeColumns =
        {
            new StandingsColumn("Class", "Class colour", "", 8, StandingsColumn.Left, true),
            new StandingsColumn("Pos", "Position", "P", 28, StandingsColumn.Center, true),
            new StandingsColumn("Num", "Car number", "#", 38, StandingsColumn.Center, true),
            new StandingsColumn("Flag", "Country flag", "", 30, StandingsColumn.Center, false),
            new StandingsColumn("Name", "Name", "Driver", 130, StandingsColumn.Left, true),
            new StandingsColumn("Make", "Make", "Make", 44, StandingsColumn.Center, false),
            new StandingsColumn("Lic", "Safety rating", "SR", 40, StandingsColumn.Center, true),
            new StandingsColumn("iR", "iRating", "iR", 44, StandingsColumn.Right, true),
            new StandingsColumn("Gap", "Gap to you", "Gap", 38, StandingsColumn.Center, true),
            new StandingsColumn("Lap", "Current lap", "Lap", 36, StandingsColumn.Right, false),
            new StandingsColumn("Tyre", "Tyres", "Tyre", 44, StandingsColumn.Left, false),
            new StandingsColumn("Pit", "Pit", "Pit", 60, StandingsColumn.Center, false),
            new StandingsColumn("Stint", "Stint laps", "Stint", 36, StandingsColumn.Right, false)
        };

        /// <summary>The columns an overlay offers.</summary>
        public static StandingsColumn[] ColumnsOf(string overlay)
        {
            return overlay == OverlayData.RelativesKey ? RelativeColumns : AllColumns;
        }

        /// <summary>Choices that only one of the two overlays uses.</summary>
        public static bool AppliesTo(StandingsChoice choice, string overlay)
        {
            if (choice.Only != null)
            {
                return choice.Only == overlay;
            }

            return overlay == OverlayData.RelativesKey ? !StandingsOnly.Contains(choice.Key) : true;
        }

        private static readonly HashSet<string> StandingsOnly = new HashSet<string>
        {
            "Gap.DecRace", "Gap.DecPQ", "Gap.ShowRace", "Gap.ShowPQ", "Int.DecRace", "Int.DecPQ", "Int.ShowRace", "Int.ShowPQ",
            "Last.DecRace", "Last.DecPQ", "Avg.Laps", "Delta.Count", "Titles", "Classes", "Tab", "Tab.Count", "Tab.Sof", "Tab.Style"
        };

        /// <summary>Defaults that differ for the relatives: a shorter header and footer.</summary>
        private static readonly Dictionary<string, string> RelativeFallbacks = new Dictionary<string, string>
        {
            { "F.Session", "0" }, { "Name.Width", "130" }, { "Gap.Weight", "Bold" }
        };

        private static string FallbackOf(StandingsChoice choice, string overlay)
        {
            string value;
            return overlay == OverlayData.RelativesKey && RelativeFallbacks.TryGetValue(choice.Key, out value) ? value : choice.Fallback;
        }

        private static readonly string[] Decimals = { "3", "3 decimals", "2", "2 decimals", "1", "1 decimal" };
        private static readonly string[] OnOff = { "1", "On", "0", "Off" };

        // The header has fixed places, each with a choice of content, so it can never get too long.
        internal static readonly string[] HeaderSlot =
        {
            "Off", "Nothing", "Clock", "PC time", "Sim", "Sim time", "Temp", "Track temperature", "Sof", "Strength of field",
            "Laps", "Laps (the leader's lap and the total)", "Time", "Time left", "Elapsed", "Session time (elapsed and total)",
            "Session", "Session (R, Q or P)", "Track", "Track name", "Car", "Your car", "Inc", "Your incidents",
            "Oil", "Oil temperature (when the game delivers it)", "Water", "Water temperature (when the game delivers it)",
            "Air", "Air temperature (when the game delivers it)", "Humidity", "Air humidity (when the game delivers it)"
        };

        // Static fields are set up in the order they are written: these must come before AllChoices, which uses them.

        /// <summary>Columns that can get a background of their own, with the default for each.</summary>
        private static readonly Dictionary<string, string> BackgroundDefaults = new Dictionary<string, string>
        {
            { "Pos", "White" }, { "Gain", "None" }, { "Num", "None" }, { "Name", "None" }, { "Make", "None" }, { "iR", "White" },
            { "Gap", "None" }, { "Int", "None" }, { "Last", "None" }, { "Best", "Subtle" }, { "Avg", "Subtle" }, { "Tyre", "Subtle" }, { "Stint", "None" }
        };

        /// <summary>Columns that are drawn in a weight of their own unless the user chooses one (the others follow the overlay).</summary>
        private static readonly Dictionary<string, string> WeightDefaults = new Dictionary<string, string>
        {
            { "iR", "SemiBold" },
            { "Lic", "SemiBold" }
        };

        public static readonly StandingsChoice[] AllChoices = BaseChoices().Concat(ColumnLookChoices()).ToArray();

        /// <summary>Font, weight and background per column; "Default" follows the choices for the whole overlay.</summary>
        private static IEnumerable<StandingsChoice> ColumnLookChoices()
        {
            foreach (StandingsColumn column in AllColumns)
            {
                if (column.Key == "Class" || column.Key == "Flag")
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
                string weightFallback;
                if (!WeightDefaults.TryGetValue(column.Key, out weightFallback))
                {
                    weightFallback = "Default";
                }

                yield return new StandingsChoice(column.Key, column.Key + ".Weight", "Font weight", weightFallback,
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
            new StandingsChoice("Lic", "Lic.Mode", "Look", "Split",
                "Split", "Rating and class side by side (3.4 A)", "Full", "Class and rating (A3.48)", "Short", "Class and rating, 1 decimal (A3.4)", "Round", "Class and whole rating (A3)", "Letter", "Class only (A)", "Number", "Rating only, in class colour (3.48)"),
            new StandingsChoice("Lic", "Lic.Number", "Safety rating number (side by side look)", "1", OnOff),
            new StandingsChoice("iR", "iR.Mode", "Look", "K1",
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
            LookSettings.ForOverlay("Opacity", "Background opacity of the rows", LookSettings.FollowGeneral),
            LookSettings.ForOverlay("HfOpacity", "Background opacity of the header and footer", LookSettings.FollowGeneral),
            new StandingsChoice("Look", "RowGap", "Space between rows", "0",
                "0", "None (one panel)", "1", "1 px", "2", "2 px", "3", "3 px", "4", "4 px"),
            LookSettings.ForOverlay("Font", "Font", LookSettings.FollowGeneral),
            LookSettings.ForOverlay("Corners", "Background boxes", LookSettings.FollowGeneral),
            new StandingsChoice("Look", "Titles", "Titles over the lap times (LAST, BEST, AVG)", "1", OnOff),
            LookSettings.ForOverlay("Weight", "Font weight", LookSettings.FollowGeneral),
            new StandingsChoice("Header", "H.Left", "Left side", "Clock", HeaderSlot),
            new StandingsChoice("Header", "H.C1", "Centre, first", "Temp", HeaderSlot),
            new StandingsChoice("Header", "H.C2", "Centre, second", "Sof", HeaderSlot),
            new StandingsChoice("Header", "H.C3", "Centre, third", "Laps", HeaderSlot),
            new StandingsChoice("Header", "H.Right", "Right side", "Sim", HeaderSlot),
            new StandingsChoice("Header", "H.TempUnit", "Unit of the temperatures", "C",
                "C", "\u00B0C", "F", "\u00B0F", "Both", "\u00B0C and \u00B0F"),
            new StandingsChoice("Header", "H.Time12", "Format of the two times", "24",
                "24", "24 hours (19:45)", "12", "12 hours (7:45 pm)"),
            new StandingsChoice("Footer", "F.Last", "LAST: your last lap", "1", OnOff),
            new StandingsChoice("Footer", "F.Delta", "Live delta to your best lap", "1", OnOff),
            new StandingsChoice("Footer", "F.Best", "BEST: your best lap", "1", OnOff),
            new StandingsChoice("Footer", "F.Session", "SESSION: best lap of the session", "1", OnOff),
            new StandingsChoice("General", "Classes", "Classes in multi-class races", "Grouped",
                "Own", "Only my class", "ByClass", "All cars, sorted by class", "Grouped", "My class first, then the leaders of the other classes", "Mixed", "All classes mixed (overall order)"),
            new StandingsChoice("General", "Tab", "Class tab above each class block (when classes are split)", "1", OnOff),
            new StandingsChoice("General", "Tab.Count", "Number of cars in the tab", "0", OnOff),
            new StandingsChoice("General", "Tab.Sof", "Strength of field of the class in the tab", "0", OnOff),
            new StandingsChoice("General", "Tab.Style", "Look of the class tab", "Subtle",
                "Subtle", "Transparent", "Solid", "Full colour"),
            new StandingsChoice("General", "Rel.Ahead", "Cars in front of you", "3", "1", "1", "2", "2", "3", "3", "4", "4", "5", "5", "6", "6") { Only = OverlayData.RelativesKey },
            new StandingsChoice("General", "Rel.Behind", "Cars behind you", "3", "1", "1", "2", "2", "3", "3", "4", "4", "5", "5", "6", "6") { Only = OverlayData.RelativesKey },
            new StandingsChoice("Gap", "Rel.Dec", "Decimals of the gap to you", "1", Decimals) { Only = OverlayData.RelativesKey },
            new StandingsChoice("Animation", "Anim.Style", "Style of the change", "Slide",
                "Slide", "Slide (the row moves)", "Pop", "Pop (the row grows at its new place)", "Fade", "Fade (the row fades in at its new place)"),
            new StandingsChoice("Animation", "Anim.Car", "When a car changes position, in the car", "Normal",
                "Off", "Off", "Fast", "Fast", "Normal", "Normal", "Slow", "Slow"),
            new StandingsChoice("Animation", "Anim.Replay", "When a car changes position, in replays", "Off",
                "Off", "Off", "Fast", "Fast", "Normal", "Normal", "Slow", "Slow")
            };
        }

        public static readonly int[] RowChoices = Enumerable.Range(3, 38).ToArray();

        public static readonly int[] ScaleChoices = Enumerable.Range(10, 51).Select(i => i * 5).ToArray();   // 50 % to 300 %

        private Dictionary<string, string> _values;

        /// <summary>The overlay these options belong to (the standings or the relatives).</summary>
        public string Key { get; private set; } = OverlayData.StandingsKey;

        public bool IsRelative
        {
            get { return Key == OverlayData.RelativesKey; }
        }

        public List<StandingsColumn> Columns { get; private set; }
        public int Rows { get; private set; }
        public bool Header { get; private set; }
        public bool Footer { get; private set; }
        public bool Labels { get; private set; }
        public double Scale { get; private set; }

        /// <summary>The same options with another number of rows (while the window is being resized).</summary>
        public StandingsOptions WithRows(int rows)
        {
            return new StandingsOptions { Key = Key, Columns = Columns, Rows = Math.Max(3, Math.Min(40, rows)), Header = Header, Footer = Footer, Labels = Labels, Scale = Scale, Features = Features, _values = _values };
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
                case "Flag":
                    return StandingsFeatures.Flags;
                default:
                    return StandingsFeatures.None;
            }
        }

        /// <summary>The same options without the columns the current game cannot fill.</summary>
        public StandingsOptions ForFeatures(StandingsFeatures features)
        {
            List<StandingsColumn> columns = Columns.Where(c => (FeatureOf(c.Key) & ~features) == 0).ToList();
            return new StandingsOptions { Key = Key, Columns = columns, Rows = Rows, Header = Header, Footer = Footer, Labels = Labels, Scale = Scale, Features = features, _values = _values };
        }

        /// <summary>What the game delivers; set by ForFeatures, everything otherwise.</summary>
        public StandingsFeatures Features { get; private set; } = StandingsFeatures.All;

        /// <summary>The same options at another size (while the window is being pulled wider).</summary>
        public StandingsOptions WithScale(double scale)
        {
            return new StandingsOptions { Key = Key, Columns = Columns, Rows = Rows, Header = Header, Footer = Footer, Labels = Labels, Scale = Math.Max(0.5, Math.Min(3, scale)), Features = Features, _values = _values };
        }

        /// <summary>All columns in the order the user arranged them (new columns go to the place they have in AllColumns).</summary>
        public static List<StandingsColumn> OrderedColumns(string overlay = OverlayData.StandingsKey)
        {
            StandingsColumn[] all = ColumnsOf(overlay);
            string saved = OverlaySettings.GetOption(overlay, "Order", string.Empty);
            List<StandingsColumn> ordered = new List<StandingsColumn>();
            foreach (string key in saved.Split(','))
            {
                StandingsColumn column = all.FirstOrDefault(c => c.Key == key);
                if (column != null && !ordered.Contains(column))
                {
                    ordered.Add(column);
                }
            }

            for (int i = 0; i < all.Length; i++)
            {
                if (!ordered.Contains(all[i]))
                {
                    // insert after the column that comes before it by default, or at the start
                    int after = i == 0 ? -1 : ordered.IndexOf(all[i - 1]);
                    ordered.Insert(after + 1, all[i]);
                }
            }

            return ordered;
        }

        /// <summary>Saves the order of the columns (keys of all columns, first to last).</summary>
        public static void SetOrder(IEnumerable<string> keys, string overlay = OverlayData.StandingsKey)
        {
            OverlaySettings.SetOption(overlay, "Order", string.Join(",", keys));
        }

        /// <summary>Puts the columns back into their default order; which columns are on stays.</summary>
        public static void ResetOrder(string overlay = OverlayData.StandingsKey)
        {
            SetOrder(ColumnsOf(overlay).Select(c => c.Key), overlay);
        }

        public static StandingsOptions Load(string overlay = OverlayData.StandingsKey)
        {
            string defaults = string.Join(",", ColumnsOf(overlay).Where(c => c.OnByDefault).Select(c => c.Key));
            HashSet<string> on = new HashSet<string>(OverlaySettings.GetOption(overlay, "Columns", defaults).Split(','), StringComparer.Ordinal);
            int rows;
            if (!int.TryParse(OverlaySettings.GetOption(overlay, "Rows", "12"), NumberStyles.Integer, CultureInfo.InvariantCulture, out rows))
            {
                rows = 12;
            }

            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (StandingsChoice choice in AllChoices)
            {
                // a look setting that follows General gets the value from there
                values[choice.Key] = LookSettings.Resolve(choice.Key, Raw(choice, overlay));
            }

            if (overlay == OverlayData.RelativesKey)
            {
                // the rows follow from the cars in front and behind, and you in the middle
                int ahead, behind;
                int.TryParse(values["Rel.Ahead"], NumberStyles.Integer, CultureInfo.InvariantCulture, out ahead);
                int.TryParse(values["Rel.Behind"], NumberStyles.Integer, CultureInfo.InvariantCulture, out behind);
                rows = Math.Max(1, ahead) + Math.Max(1, behind) + 1;
            }

            return new StandingsOptions
            {
                Key = overlay,
                Columns = OrderedColumns(overlay).Where(c => on.Contains(c.Key)).ToList(),
                Rows = Math.Max(3, Math.Min(40, rows)),
                Header = OverlaySettings.GetOption(overlay, "Header", "1") == "1",
                Footer = OverlaySettings.GetOption(overlay, "Footer", "1") == "1",
                Labels = OverlaySettings.GetOption(overlay, "Labels", "0") == "1",
                Scale = ScaleOf(OverlaySettings.GetOption(overlay, "Scale", "100")),
                _values = values
            };
        }

        /// <summary>What is chosen for the overlay, as it is stored: for a look setting that follows General this is "Global".</summary>
        public static string Raw(StandingsChoice choice, string overlay)
        {
            string fallback = FallbackOf(choice, overlay);
            string value = OverlaySettings.GetOption(overlay, choice.Key, fallback);
            return choice.Values.Contains(value) ? value : fallback;
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
                case "Gap":
                    return IsRelative ? 46 : column.Width;
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
        public static void Reset(string overlay = OverlayData.StandingsKey)
        {
            OverlaySettings.ClearOptions(overlay);
        }

        /// <summary>The places of a header: left, three in the centre and right (Standings), or eight in a row (Battle Bar).</summary>
        public static bool IsHeaderSlot(string key)
        {
            return key == "H.Left" || key == "H.C1" || key == "H.C2" || key == "H.C3" || key == "H.Right"
                || key == "H.S1" || key == "H.S2" || key == "H.S3" || key == "H.S4" || key == "H.S5"
                || key == "H.S6" || key == "H.S7" || key == "H.S8";
        }

        /// <summary>False for "Off"; every other entry of a header place may be used only once.</summary>
        public static bool IsContent(string value)
        {
            return value != "Off";
        }

        /// <summary>Puts every choice of one group (such as "Header") of an overlay back to its default.</summary>
        public static void ResetGroup(string column, string overlay = OverlayData.StandingsKey)
        {
            foreach (StandingsChoice choice in AllChoices)
            {
                if (choice.Column == column && AppliesTo(choice, overlay))
                {
                    OverlaySettings.SetOption(overlay, choice.Key, FallbackOf(choice, overlay));
                }
            }
        }

        /// <summary>
        /// Text in the last pair of brackets becomes a tooltip: "Session (R, Q or P)" is shown as "Session" with the
        /// hint on hover. Keeps the settings lines short.
        /// </summary>
        public static string SplitHint(string text, out string hint)
        {
            hint = null;
            if (text == null || !text.EndsWith(")", StringComparison.Ordinal))
            {
                return text;
            }

            int open = text.LastIndexOf(" (", StringComparison.Ordinal);
            if (open <= 0)
            {
                return text;
            }

            hint = text.Substring(open + 2, text.Length - open - 3);
            return text.Substring(0, open);
        }

        public static void SetChoice(string choice, string value, string overlay = OverlayData.StandingsKey)
        {
            OverlaySettings.SetOption(overlay, choice, value);
        }

        public static bool IsColumnOn(string column, string overlay = OverlayData.StandingsKey)
        {
            return Load(overlay).Columns.Any(c => c.Key == column);
        }

        public static void SetColumn(string column, bool on, string overlay = OverlayData.StandingsKey)
        {
            HashSet<string> keys = new HashSet<string>(Load(overlay).Columns.Select(c => c.Key), StringComparer.Ordinal);
            if (on)
            {
                keys.Add(column);
            }
            else
            {
                keys.Remove(column);
            }

            // The order lives in its own option, so the overlay does not depend on the click order.
            OverlaySettings.SetOption(overlay, "Columns", string.Join(",", ColumnsOf(overlay).Where(c => keys.Contains(c.Key)).Select(c => c.Key)));
        }

        public static void SetRows(int rows, string overlay = OverlayData.StandingsKey)
        {
            OverlaySettings.SetOption(overlay, "Rows", rows.ToString(CultureInfo.InvariantCulture));
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

        public static void SetLabels(bool on, string overlay = OverlayData.StandingsKey)
        {
            OverlaySettings.SetOption(overlay, "Labels", on ? "1" : "0");
        }

        /// <summary>Saves a size, rounded to 5 %.</summary>
        public static void SetScale(double scale, string overlay = OverlayData.StandingsKey)
        {
            int percent = (int)Math.Round(Math.Max(0.5, Math.Min(3, scale)) * 20) * 5;
            SetScale(percent, overlay);
        }

        public static void SetScale(int percent, string overlay = OverlayData.StandingsKey)
        {
            OverlaySettings.SetOption(overlay, "Scale", percent.ToString(CultureInfo.InvariantCulture));
        }

        public static void SetFooter(bool on, string overlay = OverlayData.StandingsKey)
        {
            OverlaySettings.SetOption(overlay, "Footer", on ? "1" : "0");
        }

        public static void SetHeader(bool on, string overlay = OverlayData.StandingsKey)
        {
            OverlaySettings.SetOption(overlay, "Header", on ? "1" : "0");
        }
    }
}
