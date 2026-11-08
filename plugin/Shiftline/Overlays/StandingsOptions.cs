using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Shiftline.Plugin
{
    /// <summary>A column of the standings overlay: its key (stored in the settings), its label and its width in the design.</summary>
    internal sealed class StandingsColumn
    {
        public StandingsColumn(string key, string label, double width, bool rightAligned, bool onByDefault)
        {
            Key = key;
            Label = label;
            Width = width;
            RightAligned = rightAligned;
            OnByDefault = onByDefault;
        }

        public string Key { get; }
        public string Label { get; }
        public double Width { get; }
        public bool RightAligned { get; }
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
            new StandingsColumn("Pos", "Position", 30, true, true),
            new StandingsColumn("Gain", "Places gained", 40, false, true),
            new StandingsColumn("Num", "Car number", 42, true, true),
            new StandingsColumn("Name", "Name", 160, false, true),
            new StandingsColumn("Make", "Make", 46, false, true),
            new StandingsColumn("Lic", "Safety rating", 58, false, true),
            new StandingsColumn("iR", "iRating", 48, true, true),
            new StandingsColumn("Gap", "Gap", 56, true, true),
            new StandingsColumn("Int", "Interval", 52, true, true),
            new StandingsColumn("Last", "Lap time", 72, true, true),
            new StandingsColumn("Tyre", "Tyres", 52, false, true),
            new StandingsColumn("Pit", "Pit", 66, false, true),
            new StandingsColumn("Stint", "Stint laps", 34, true, false),
            new StandingsColumn("Delta", "Gain to you", 96, true, true)
        };

        private static readonly string[] Decimals = { "3", "3 decimals", "2", "2 decimals", "1", "1 decimal" };
        private static readonly string[] OnOff = { "1", "On", "0", "Off" };

        public static readonly StandingsChoice[] AllChoices =
        {
            new StandingsChoice("Num", "Num.Style", "Look", "1",
                "0", "Normal", "1", "Bold", "2", "Italic", "3", "Bold italic", "4", "Normal in a box", "5", "Bold in a box", "6", "Italic in a box", "7", "Bold italic in a box"),
            new StandingsChoice("Name", "Name.Format", "Format", "FirstLast",
                "Full", "Joshua K Rogers", "FirstLast", "Joshua Rogers", "InitialLast", "J. Rogers", "Last", "Rogers"),
            new StandingsChoice("Name", "Name.Case", "Style", "Normal",
                "Normal", "Normal", "SmallCaps", "Small caps", "Upper", "UPPERCASE"),
            new StandingsChoice("Name", "Name.Width", "Maximum width", "160",
                "100", "100 px", "130", "130 px", "160", "160 px", "190", "190 px", "220", "220 px", "260", "260 px"),
            new StandingsChoice("Lic", "Lic.Mode", "Look", "Full",
                "Full", "Class and rating (A3.48)", "Short", "Class and rating, 1 decimal (A3.4)", "Round", "Class and whole rating (A3)", "Letter", "Class only (A)", "Number", "Rating only, in class colour (3.48)"),
            new StandingsChoice("iR", "iR.Mode", "Look", "Full",
                "Full", "Full value (4567)", "K1", "Thousands, 1 decimal (4.5k)", "K0", "Whole thousands (5k)"),
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
            new StandingsChoice("Tyre", "Tyre.Laps", "Laps on the tyres", "1", OnOff),
            new StandingsChoice("Pit", "Pit.Mode", "Look", "Info",
                "Info", "With time in the pit lane or the lap of the last stop (PIT 0:23)", "Plain", "Word only (PIT)"),
            new StandingsChoice("Delta", "Delta.Count", "Number of laps", "3",
                "1", "1 lap", "2", "2 laps", "3", "3 laps", "4", "4 laps", "5", "5 laps")
        };

        public static readonly int[] RowChoices = { 6, 8, 10, 12, 15, 20, 25 };

        private const string Key = OverlayData.StandingsKey;

        private Dictionary<string, string> _values;

        public List<StandingsColumn> Columns { get; private set; }
        public int Rows { get; private set; }
        public bool Header { get; private set; }

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
                Columns = AllColumns.Where(c => on.Contains(c.Key)).ToList(),
                Rows = Math.Max(3, Math.Min(40, rows)),
                Header = OverlaySettings.GetOption(Key, "Header", "1") == "1",
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
                    return GetBool("Tyre.Laps") ? 52 : 28;
                case "Pit":
                    return Get("Pit.Mode") == "Plain" ? 44 : 66;
                default:
                    return column.Width;
            }
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

            // Keep the order of AllColumns, so the overlay does not depend on the click order.
            OverlaySettings.SetOption(Key, "Columns", string.Join(",", AllColumns.Where(c => keys.Contains(c.Key)).Select(c => c.Key)));
        }

        public static void SetRows(int rows)
        {
            OverlaySettings.SetOption(Key, "Rows", rows.ToString(CultureInfo.InvariantCulture));
        }

        public static void SetHeader(bool on)
        {
            OverlaySettings.SetOption(Key, "Header", on ? "1" : "0");
        }
    }
}
