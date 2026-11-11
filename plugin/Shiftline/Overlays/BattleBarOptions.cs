using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Shiftline.Plugin
{
    /// <summary>The design choices of the Battle Bar, read from and written to OverlaySettings under its own key.</summary>
    internal sealed class BattleBarOptions
    {
        public const string Key = OverlayData.BattleBarKey;

        /// <summary>What a card can show. Reuses StandingsColumn for key, label and default.</summary>
        public static readonly StandingsColumn[] AllItems =
        {
            new StandingsColumn("Pos", "Position", "P", 0, StandingsColumn.Center, true),
            new StandingsColumn("Class", "Class colour", "", 0, StandingsColumn.Left, true),
            new StandingsColumn("Name", "Name", "", 0, StandingsColumn.Left, true),
            new StandingsColumn("Make", "Make", "", 0, StandingsColumn.Center, true),
            new StandingsColumn("Gap", "Gap", "", 0, StandingsColumn.Center, true),
            new StandingsColumn("Lic", "Safety rating", "", 0, StandingsColumn.Center, false),
            new StandingsColumn("iR", "iRating", "", 0, StandingsColumn.Center, true),
            new StandingsColumn("Tyre", "Tyres", "", 0, StandingsColumn.Center, true),
            new StandingsColumn("Pit", "Pit", "", 0, StandingsColumn.Center, true),
            new StandingsColumn("Gain", "Places gained", "", 0, StandingsColumn.Center, true)
        };

        private static readonly string[] OnOff = { "1", "On", "0", "Off" };

        private static readonly string[] SideBox =
        {
            "Off", "Off", "Laps", "Laps (or time left)", "Position", "Your position", "Delta", "Live delta to your best lap",
            "Last", "Your last lap", "Best", "Your best lap", "Temp", "Track temperature", "Clock", "Time on this PC"
        };

        public static readonly StandingsChoice[] AllChoices =
        {
            new StandingsChoice("Mode", "Mode", "Layout", "Cards",
                "Cards", "Cards", "Ticker", "Ticker", "Scroll", "Running ticker"),
            new StandingsChoice("Mode", "Ahead", "Cars in front of you (cards)", "2", "1", "1", "2", "2", "3", "3", "4", "4"),
            new StandingsChoice("Mode", "Behind", "Cars behind you (cards)", "2", "1", "1", "2", "2", "3", "3", "4", "4"),
            new StandingsChoice("Mode", "Leader", "Leader always on the left (cards)", "0", OnOff),
            new StandingsChoice("Mode", "Ticker.Cars", "Cars in the ticker", "7",
                "5", "5", "7", "7", "9", "9", "11", "11", "13", "13"),
            new StandingsChoice("Mode", "Ticker.Speed", "Speed of the running ticker", "Normal", "Slow", "Slow", "Normal", "Normal", "Fast", "Fast"),
            new StandingsChoice("Field", "Sort", "Order", "Race",
                "Race", "Race position", "Track", "Position on the track (with lapped cars)"),
            new StandingsChoice("Field", "Lapped", "Cars a lap up or down (when sorted by the track)", "Show",
                "Show", "Show (red: a lap up, blue: a lap down)", "Hide", "Hide"),
            new StandingsChoice("Field", "Classes", "Classes in multi-class races", "Own",
                "Own", "Only my class", "All", "All classes, class colour as frame"),
            new StandingsChoice("Field", "GapTo", "Gap", "Player", "Player", "To you (+ behind, - in front)", "Ahead", "To the car in front"),
            new StandingsChoice("Field", "Trend", "Trend arrow at the gap (closing or pulling away)", "1", OnOff),
            new StandingsChoice("Field", "Trend.Laps", "Trend over", "2", "1", "1 lap", "2", "2 laps", "3", "3 laps"),
            new StandingsChoice("Field", "Battle", "Battle frame when a car is closer than", "1.0",
                "0", "Off", "0.5", "0.5 s", "1.0", "1.0 s", "1.5", "1.5 s", "2.0", "2.0 s"),
            new StandingsChoice("Field", "Battle.Colour", "Colour of the battle frame", "Orange", "Orange", "Orange", "Red", "Red", "Yellow", "Yellow"),
            new StandingsChoice("Field", "Frame.Player", "Frame around your own card", "Shiftline",
                "Shiftline", "Shiftline", "BlueFlow", "Blue flow", "GreenFlow", "Green flow", "Blue", "Blue", "Green", "Green", "White", "White", "Off", "Off"),
            new StandingsChoice("Field", "ShowIn", "Show in", "Always", "Always", "All sessions", "Race", "Races only", "RaceQuali", "Races and qualifying"),
            new StandingsChoice("Name", "Name.Format", "Name format", "Last",
                "FirstLast", "Joshua Rogers", "InitialLast", "J. Rogers", "Last", "Rogers", "Code", "Short code (ROG)"),
            new StandingsChoice("Name", "Lic.Number", "Safety rating: show the number next to the class letter", "1", OnOff),
            new StandingsChoice("Name", "Name.Width", "Maximum name width (longer names end with ...)", "100",
                "80", "80 px", "100", "100 px", "120", "120 px", "140", "140 px", "160", "160 px"),
            new StandingsChoice("Look", "Opacity", "Background opacity", "90",
                "100", "100 %", "90", "90 %", "80", "80 %", "70", "70 %", "60", "60 %", "50", "50 %", "40", "40 %", "30", "30 %"),
            new StandingsChoice("Look", "Font", "Font", "Segoe UI",
                "Segoe UI", "Segoe UI", "Saira SemiCondensed", "Saira Semi Condensed", "Inter", "Inter", "Arial", "Arial", "Verdana", "Verdana", "Tahoma", "Tahoma", "Consolas", "Consolas"),
            new StandingsChoice("Look", "Weight", "Font weight", "SemiBold", "Normal", "Normal", "SemiBold", "Semi-bold", "Bold", "Bold"),
            new StandingsChoice("Look", "Corners", "Boxes", "Rounded", "Rounded", "Rounded", "Square", "Square"),
            new StandingsChoice("Look", "PosBg", "Position box", "White", "White", "White, dark text", "Subtle", "Subtle grey", "None", "None"),
            new StandingsChoice("Header", "H.S1", "First", "Session", StandingsOptions.HeaderSlot),
            new StandingsChoice("Header", "H.S2", "Second", "Laps", StandingsOptions.HeaderSlot),
            new StandingsChoice("Header", "H.S3", "Third", "Time", StandingsOptions.HeaderSlot),
            new StandingsChoice("Header", "H.S4", "Fourth", "Sof", StandingsOptions.HeaderSlot),
            new StandingsChoice("Header", "H.S5", "Fifth", "Inc", StandingsOptions.HeaderSlot),
            new StandingsChoice("Header", "H.S6", "Sixth", "Temp", StandingsOptions.HeaderSlot),
            new StandingsChoice("Header", "H.S7", "Seventh", "Sim", StandingsOptions.HeaderSlot),
            new StandingsChoice("Header", "H.S8", "Eighth", "Off", StandingsOptions.HeaderSlot),
            new StandingsChoice("Header", "H.TempUnit", "Unit of the temperatures", "C",
                "C", "\u00B0C", "F", "\u00B0F", "Both", "\u00B0C and \u00B0F"),
            new StandingsChoice("Header", "H.Time12", "Format of the times", "24",
                "24", "24 hours (19:45)", "12", "12 hours (7:45 pm)"),
            new StandingsChoice("Footer", "F.Last", "LAST: your last lap", "1", OnOff),
            new StandingsChoice("Footer", "F.Delta", "Live delta to your best lap", "1", OnOff),
            new StandingsChoice("Footer", "F.Best", "BEST: your best lap", "1", OnOff),
            new StandingsChoice("Sides", "Side.Left", "Box on the left", "Laps", SideBox),
            new StandingsChoice("Sides", "Side.Right", "Box on the right", "Temp", SideBox),
            new StandingsChoice("Animation", "Anim.Style", "When cars swap places", "Slide", "Slide", "Slide", "Fade", "Fade", "Pop", "Pop", "Off", "Off"),
            new StandingsChoice("Animation", "Anim.Speed", "Speed", "Normal", "Fast", "Fast", "Normal", "Normal", "Slow", "Slow"),
            new StandingsChoice("Animation", "Anim.Flash", "Your card lights up green or red when you gain or lose a place", "1", OnOff),
            new StandingsChoice("Animation", "Anim.Replay", "Animate in replays", "0", OnOff)
        };

        public static readonly int[] ScaleChoices = StandingsOptions.ScaleChoices;

        private Dictionary<string, string> _values;

        public List<StandingsColumn> Items { get; private set; }
        public bool Header { get; private set; }
        public bool Footer { get; private set; }
        public double Scale { get; private set; }
        public StandingsFeatures Features { get; private set; } = StandingsFeatures.All;

        public static BattleBarOptions Load()
        {
            string defaults = string.Join(",", AllItems.Where(c => c.OnByDefault).Select(c => c.Key));
            HashSet<string> on = new HashSet<string>(OverlaySettings.GetOption(Key, "Items", defaults).Split(','), StringComparer.Ordinal);
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (StandingsChoice choice in AllChoices)
            {
                string value = OverlaySettings.GetOption(Key, choice.Key, choice.Fallback);
                values[choice.Key] = choice.Values.Contains(value) ? value : choice.Fallback;
            }

            int percent;
            if (!int.TryParse(OverlaySettings.GetOption(Key, "Scale", "100"), NumberStyles.Integer, CultureInfo.InvariantCulture, out percent))
            {
                percent = 100;
            }

            return new BattleBarOptions
            {
                Items = OrderedItems().Where(c => on.Contains(c.Key)).ToList(),
                Header = OverlaySettings.GetOption(Key, "Header", "1") == "1",
                Footer = OverlaySettings.GetOption(Key, "Footer", "1") == "1",
                Scale = Math.Max(0.5, Math.Min(3, percent / 100.0)),
                _values = values
            };
        }

        /// <summary>The same options without the items the current game cannot fill.</summary>
        public BattleBarOptions ForFeatures(StandingsFeatures features)
        {
            return new BattleBarOptions
            {
                Items = Items.Where(c => (StandingsOptions.FeatureOf(c.Key) & ~features) == 0).ToList(),
                Header = Header,
                Footer = Footer,
                Scale = Scale,
                Features = features,
                _values = _values
            };
        }

        /// <summary>The same options at another size (while the window is being pulled wider).</summary>
        public BattleBarOptions WithScale(double scale)
        {
            return new BattleBarOptions { Items = Items, Header = Header, Footer = Footer, Scale = Math.Max(0.5, Math.Min(3, scale)), Features = Features, _values = _values };
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

        public double GetDouble(string choice)
        {
            double value;
            return double.TryParse(Get(choice), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : 0;
        }

        public bool GetBool(string choice)
        {
            return Get(choice) == "1";
        }

        public bool Has(string item)
        {
            return Items.Any(c => c.Key == item);
        }

        // ---------------------------------------------------------------- writing

        public static List<StandingsColumn> OrderedItems()
        {
            string saved = OverlaySettings.GetOption(Key, "Order", string.Empty);
            List<StandingsColumn> ordered = new List<StandingsColumn>();
            foreach (string key in saved.Split(','))
            {
                StandingsColumn item = AllItems.FirstOrDefault(c => c.Key == key);
                if (item != null && !ordered.Contains(item))
                {
                    ordered.Add(item);
                }
            }

            for (int i = 0; i < AllItems.Length; i++)
            {
                if (!ordered.Contains(AllItems[i]))
                {
                    int after = i == 0 ? -1 : ordered.IndexOf(AllItems[i - 1]);
                    ordered.Insert(after + 1, AllItems[i]);
                }
            }

            return ordered;
        }

        public static void SetOrder(IEnumerable<string> keys)
        {
            OverlaySettings.SetOption(Key, "Order", string.Join(",", keys));
        }

        public static void ResetOrder()
        {
            SetOrder(AllItems.Select(c => c.Key));
        }

        public static bool IsItemOn(string item)
        {
            return Load().Items.Any(c => c.Key == item);
        }

        public static void SetItem(string item, bool on)
        {
            HashSet<string> keys = new HashSet<string>(Load().Items.Select(c => c.Key), StringComparer.Ordinal);
            if (on)
            {
                keys.Add(item);
            }
            else
            {
                keys.Remove(item);
            }

            OverlaySettings.SetOption(Key, "Items", string.Join(",", AllItems.Where(c => keys.Contains(c.Key)).Select(c => c.Key)));
        }

        public static void SetChoice(string choice, string value)
        {
            OverlaySettings.SetOption(Key, choice, value);
        }

        /// <summary>Puts every choice of one group (such as "Header") back to its default.</summary>
        public static void ResetGroup(string column)
        {
            foreach (StandingsChoice choice in AllChoices)
            {
                if (choice.Column == column)
                {
                    OverlaySettings.SetOption(Key, choice.Key, choice.Fallback);
                }
            }
        }

        public static void SetHeader(bool on)
        {
            OverlaySettings.SetOption(Key, "Header", on ? "1" : "0");
        }

        public static void SetFooter(bool on)
        {
            OverlaySettings.SetOption(Key, "Footer", on ? "1" : "0");
        }

        public static void SetScale(double scale)
        {
            int percent = (int)(Math.Round(scale * 100 / 5) * 5);
            OverlaySettings.SetOption(Key, "Scale", Math.Max(50, Math.Min(300, percent)).ToString(CultureInfo.InvariantCulture));
        }

        public static void Reset()
        {
            OverlaySettings.ClearOptions(Key);
        }
    }
}
