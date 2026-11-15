using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Shiftline.Plugin
{
    /// <summary>One content of a header place: an icon or a short label, then the value.</summary>
    internal sealed class HeaderItem
    {
        public string Kind;
        public StackPanel Panel;
        public Path Icon;
        public TextBlock Value;
        public Run ValueRun;
        public string Sample;       // the longest plausible value, to measure how wide the header has to be
    }

    /// <summary>
    /// The contents a header place can show, shared by the overlays that have such a header (Standings, Relatives, Battle Bar):
    /// icons, labels, values and the formats of times and temperatures. The kinds are the values of the header choices.
    /// </summary>
    internal static class HeaderItems
    {
        private static readonly Geometry ClockShape = StandingsOverlay.FrozenGeometry("M 6,1 A 5,5 0 1 1 5.99,1 Z M 6,3.2 L 6,6 L 8,7.2");
        private static readonly Geometry ThermoShape = StandingsOverlay.FrozenGeometry("M 4.6,7.2 L 4.6,2.4 A 1.4,1.4 0 0 1 7.4,2.4 L 7.4,7.2 A 2.6,2.6 0 1 1 4.6,7.2 Z M 6,5 L 6,8.4");
        private static readonly Geometry FlagShape = StandingsOverlay.FrozenGeometry("M 2.5,10.5 L 2.5,1.5 M 2.5,2 L 10,2 L 8.2,4.9 L 10,7.8 L 2.5,7.8");
        private static readonly Geometry HourglassShape = StandingsOverlay.FrozenGeometry("M 2.5,1 L 9.5,1 M 2.5,11 L 9.5,11 M 3,1 L 3,3 L 6,6 L 3,9 L 3,11 M 9,1 L 9,3 L 6,6 L 9,9 L 9,11");
        private static readonly Geometry SunShape = StandingsOverlay.FrozenGeometry("M 0.8,9.5 L 11.2,9.5 M 3,9.5 A 3,3 0 0 1 9,9.5 M 6,2 L 6,3.6 M 1.6,4.4 L 2.8,5.5 M 10.4,4.4 L 9.2,5.5");
        private static readonly Geometry MoonShape = StandingsOverlay.FrozenGeometry("M 6.5,1.2 A 4.8,4.8 0 1 0 10.8,8.2 A 3.8,3.8 0 0 1 6.5,1.2 Z M 9.2,1.6 L 9.2,3.8 M 8.1,2.7 L 10.3,2.7");

        private static FontFamily _narrowFont;

        /// <summary>A time of day in the chosen format: 19:45, or 7:45 pm.</summary>
        public static string TimeText(DateTime time, Func<string, string> option)
        {
            return option("H.Time12") == "12"
                ? time.ToString("h:mm tt", CultureInfo.InvariantCulture).ToLowerInvariant()
                : time.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        /// <summary>A temperature in the chosen unit.</summary>
        public static string Temperature(double celsius, Func<string, string> option)
        {
            string unit = option("H.TempUnit");
            string c = celsius.ToString("0", CultureInfo.InvariantCulture) + "°C";
            string f = (celsius * 9 / 5 + 32).ToString("0", CultureInfo.InvariantCulture) + "°F";
            return unit == "F" ? f : unit == "Both" ? c + " / " + f : c;
        }

        /// <summary>
        /// Builds one item: the icon or the label (a short word in a narrow font, as a run of the value so both sit on one
        /// baseline), then the value. Null for "Off" and for an unknown kind.
        /// </summary>
        public static HeaderItem Create(string kind, Func<string, string> option, Brush text, Brush muted, double size, Thickness margin)
        {
            string label = null;
            Geometry icon = null;
            string sample;
            double maxWidth = 0;
            switch (kind)
            {
                case "Clock": icon = ClockShape; sample = "00:00 pm"; break;
                case "Sim": icon = SunShape; sample = "00:00 pm"; break;
                case "Oil": label = "OIL"; sample = Temperature(100, option); break;
                case "Water": label = "WATER"; sample = Temperature(100, option); break;
                case "Air": label = "AIR"; sample = Temperature(100, option); break;
                case "Humidity": label = "HUM"; sample = "100 %"; break;
                case "Temp": icon = ThermoShape; sample = Temperature(100, option); break;
                case "Sof": label = "SOF"; sample = "0.0k"; break;
                case "Laps": icon = FlagShape; sample = "000/000"; break;
                case "Time": icon = HourglassShape; sample = "0:00:00"; break;
                case "Elapsed": icon = ClockShape; sample = "0:00:00 / 0:00:00"; break;
                case "Session": sample = "R"; break;
                case "Track": sample = "Interlagos"; maxWidth = 110; break;
                case "Car": sample = "Porsche 911 GT3"; maxWidth = 120; break;
                case "Inc": label = "INC"; sample = "00/00"; break;
                default: return null;
            }

            HeaderItem item = new HeaderItem { Kind = kind, Sample = sample };
            item.Panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = margin };
            if (icon != null)
            {
                item.Icon = new Path
                {
                    Width = 12,
                    Height = 12,
                    Stroke = muted,
                    StrokeThickness = 1.3,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 4, 0),
                    Data = kind == "Sim" ? null : icon        // the sun or the moon is set by the first update
                };
                item.Panel.Children.Add(item.Icon);
            }

            item.Value = new TextBlock { Foreground = text, FontSize = size, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            if (label != null)
            {
                if (_narrowFont == null)
                {
                    _narrowFont = OverlayFonts.Get("Bahnschrift");
                }

                item.Value.Inlines.Add(new Run(label + "  ") { FontFamily = _narrowFont, FontStretch = FontStretches.Condensed, FontWeight = FontWeights.Normal, Foreground = muted });
            }

            item.ValueRun = new Run();
            item.Value.Inlines.Add(item.ValueRun);
            if (maxWidth > 0)
            {
                item.Value.MaxWidth = maxWidth;
                item.Value.TextTrimming = TextTrimming.CharacterEllipsis;
            }

            item.Panel.Children.Add(item.Value);
            return item;
        }

        /// <summary>
        /// Fills the items; what the game does not deliver is hidden. lapRow is the car whose lap "Laps" shows when the
        /// session has a lap limit or none is set (null: the leader).
        /// </summary>
        public static void Update(IEnumerable<HeaderItem> items, StandingsSnapshot snapshot, Func<string, string> option, bool sofInThousands, StandingsRow lapRow)
        {
            foreach (HeaderItem item in items)
            {
                string text = ValueOf(item.Kind, snapshot, option, sofInThousands, lapRow);
                Visibility shown = text != null ? Visibility.Visible : Visibility.Collapsed;
                if (item.Panel.Visibility != shown)
                {
                    item.Panel.Visibility = shown;
                }

                if (text == null)
                {
                    continue;
                }

                if (item.ValueRun.Text != text)
                {
                    item.ValueRun.Text = text;
                }

                if (item.Kind == "Sim")
                {
                    double seconds = snapshot.TimeOfDay % 86400;
                    bool day = seconds >= 6 * 3600 && seconds < 20 * 3600;
                    Geometry shape = day ? SunShape : MoonShape;
                    if (!ReferenceEquals(item.Icon.Data, shape))
                    {
                        item.Icon.Data = shape;
                    }
                }
            }
        }

        private static string ValueOf(string kind, StandingsSnapshot snapshot, Func<string, string> option, bool sofInThousands, StandingsRow lapRow)
        {
            switch (kind)
            {
                case "Clock":
                    return TimeText(DateTime.Now, option);
                case "Sim":
                    return snapshot.TimeOfDay >= 0 ? TimeText(DateTime.Today.AddSeconds(snapshot.TimeOfDay % 86400), option) : null;
                case "Oil":
                    return double.IsNaN(snapshot.OilTemp) ? null : Temperature(snapshot.OilTemp, option);
                case "Water":
                    return double.IsNaN(snapshot.WaterTemp) ? null : Temperature(snapshot.WaterTemp, option);
                case "Air":
                    return double.IsNaN(snapshot.AirTemp) ? null : Temperature(snapshot.AirTemp, option);
                case "Humidity":
                    return double.IsNaN(snapshot.Humidity) ? null : Math.Round(snapshot.Humidity).ToString("0", CultureInfo.InvariantCulture) + " %";
                case "Temp":
                    double temp;
                    return double.TryParse((snapshot.TrackTemp ?? string.Empty).Split(' ')[0], NumberStyles.Float, CultureInfo.InvariantCulture, out temp) ? Temperature(temp, option) : null;
                case "Sof":
                    if (snapshot.StrengthOfField <= 0)
                    {
                        return null;
                    }

                    return sofInThousands
                        ? (snapshot.StrengthOfField / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + "k"
                        : snapshot.StrengthOfField.ToString(CultureInfo.InvariantCulture);
                case "Laps":
                    StandingsRow[] rows = snapshot.Rows ?? new StandingsRow[0];
                    StandingsRow counted = lapRow ?? (rows.Length > 0 ? rows[0] : null);
                    int lap = counted == null ? 0 : Math.Max(0, counted.LapsCompleted) + 1;
                    if (snapshot.LapsTotal > 0)
                    {
                        return Math.Min(lap, snapshot.LapsTotal) + "/" + snapshot.LapsTotal;
                    }

                    return lap > 0 ? lap.ToString(CultureInfo.InvariantCulture) : null;
                case "Time":
                    return snapshot.TimeRemaining > 0 && snapshot.TimeRemaining < 360000
                        ? TimeSpan.FromSeconds(snapshot.TimeRemaining).ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
                        : null;
                case "Elapsed":
                    if (snapshot.TimeTotal <= 0 || snapshot.TimeTotal >= 360000 || snapshot.TimeRemaining < 0)
                    {
                        return null;
                    }

                    double done = Math.Max(0, snapshot.TimeTotal - snapshot.TimeRemaining);
                    return TimeSpan.FromSeconds(done).ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) + " / " + TimeSpan.FromSeconds(snapshot.TimeTotal).ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture);
                case "Session":
                    string type = snapshot.SessionType ?? string.Empty;
                    return type.IndexOf("Race", StringComparison.OrdinalIgnoreCase) >= 0 ? "R"
                        : type.IndexOf("Qual", StringComparison.OrdinalIgnoreCase) >= 0 ? "Q"
                        : type.Length > 0 ? "P" : "-";
                case "Track":
                    return string.IsNullOrEmpty(snapshot.TrackShort) ? null : snapshot.TrackShort;
                case "Car":
                    return string.IsNullOrEmpty(snapshot.CarName) ? null : snapshot.CarName;
                case "Inc":
                    string limit = snapshot.IncidentLimit ?? string.Empty;
                    return (snapshot.Features & StandingsFeatures.Incidents) != 0
                        ? snapshot.Incidents + (limit.Length > 0 && char.IsDigit(limit[0]) ? "/" + limit : string.Empty)
                        : null;
                default:
                    return null;
            }
        }
    }

    /// <summary>
    /// For the settings pages: the places of a header can only show what no other place shows. Entries that are taken are grey
    /// and cannot be picked.
    /// </summary>
    internal static class HeaderSlotLock
    {
        /// <summary>Returns the action that updates the drop-downs; call it once and after every change. The combo boxes carry their StandingsChoice in Tag and ComboBoxItems as items.</summary>
        public static Action Wire(List<ComboBox> slots)
        {
            return () =>
            {
                HashSet<string> taken = new HashSet<string>();
                foreach (ComboBox slot in slots)
                {
                    StandingsChoice choice = (StandingsChoice)slot.Tag;
                    if (slot.SelectedIndex >= 0 && StandingsOptions.IsContent(choice.Values[slot.SelectedIndex]))
                    {
                        taken.Add(choice.Values[slot.SelectedIndex]);
                    }
                }

                foreach (ComboBox slot in slots)
                {
                    StandingsChoice choice = (StandingsChoice)slot.Tag;
                    for (int i = 0; i < slot.Items.Count; i++)
                    {
                        string value = choice.Values[i];
                        bool free = !StandingsOptions.IsContent(value) || i == slot.SelectedIndex || !taken.Contains(value);
                        ComboBoxItem entry = (ComboBoxItem)slot.Items[i];
                        entry.IsEnabled = free;
                        entry.Opacity = free ? 1 : 0.35;
                    }
                }
            };
        }
    }
}
