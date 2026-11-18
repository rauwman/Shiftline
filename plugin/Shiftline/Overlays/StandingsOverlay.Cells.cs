using System;
using System.Globalization;
using System.Windows.Media;

namespace Shiftline.Plugin
{
    /// <summary>
    /// What one cell of the standings or relatives shows: computed once from the data and the options (CellOf), then drawn by
    /// WPF (Fill) or sent to the HTML page. Holds no WPF elements, only values and colours.
    /// </summary>
    internal sealed class StandingsCell
    {
        public string Text = string.Empty;
        public Brush Foreground;            // the text colour, already darkened on a light column
        public Brush Background;            // null: none
        public Brush MarkBrush;             // gain arrow and pit detail: the text colour before the light-column change

        public int Gain;                    // Gain: places gained (+) or lost (-), 0 = no arrow
        public string PitDetail = string.Empty;     // Pit: time in the pit lane or lap of the last stop

        public string TyreCode;             // Tyre: compound letter, null = no tyre
        public Brush TyreColour;

        public bool HasRating;              // iR: the car has an iRating (the pill shows)
        public bool ShowRatingChange;       // iR: the change column is switched on
        public int RatingChange;            // iR: gained (+) or lost (-), 0 = none
        public Brush RatingChangeBrush;

        public string LicenceLetter = string.Empty;     // Lic (split): class letter
        public string LicenceNumber = string.Empty;     // Lic (split): safety rating, one decimal
        public Brush LicenceSoft;           // Lic (split): letter box colour, null = no licence
        public Brush LicenceShade;          // Lic (split): rating box colour

        public string Flair;                // Flag: the country (FlagImages.For), null = none

        public string[] Deltas;             // Delta: per recent lap, newest first
        public Brush[] DeltaBrushes;
    }

    internal sealed partial class StandingsOverlay
    {
        /// <summary>
        /// The content of one cell, without any WPF element. <paramref name="light"/> and <paramref name="fill"/> are the
        /// column's look (white background, own fill); <paramref name="showGain"/> whether the iRating change is shown.
        /// </summary>
        private static StandingsCell CellOf(string column, Entry entry, Context context, bool relative, bool light, Brush fill, bool showGain)
        {
            StandingsRow row = entry == null ? null : entry.Row;
            StandingsOptions options = context.Options;
            StandingsCell cell = new StandingsCell();
            Brush background = null;
            Brush foreground = TextBrush;
            string value = string.Empty;

            if (column == "Delta")
            {
                DeltasOf(cell, row, context);
                return cell;
            }

            if (column == "Tyre" && row != null && !string.IsNullOrEmpty(row.TireType))
            {
                string code;
                Brush colour;
                TyreLook(row.TireType, out code, out colour);
                cell.TyreCode = code;
                cell.TyreColour = colour;
            }

            if (column == "Flag")
            {
                cell.Flair = row == null ? null : row.Flair;
            }

            if (column == "iR")
            {
                RatingOf(cell, row, row != null && showGain, row != null && light);
            }

            if (column == "Lic")
            {
                LicenceOf(cell, row);
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
                        background = ClassBrush(row.ClassColor);
                        break;
                    case "Gain":
                        if (entry.StartPosition > 0 && entry.Position > 0 && entry.StartPosition != entry.Position)
                        {
                            int gained = entry.StartPosition - entry.Position;
                            value = Math.Abs(gained).ToString(CultureInfo.InvariantCulture);
                            foreground = gained > 0 ? GreenBrush : LossBrush;
                            cell.Gain = gained;
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
                        if (options.Get("Lic.Mode") != "Split")
                        {
                            FormatLicence(row, options.Get("Lic.Mode"), out value, out background, out foreground);
                        }

                        break;
                    case "iR":
                        value = FormatIRating(row.IRating, options.Get("iR.Mode"));
                        foreground = MutedBrush;
                        break;
                    case "Lap":
                        value = row.LapDistPct >= 0 ? (row.LapsCompleted + 1).ToString(CultureInfo.InvariantCulture) : string.Empty;
                        foreground = MutedBrush;
                        break;
                    case "Gap":
                    case "Int":
                        if (relative)
                        {
                            // the cars around you: the gap to you, coloured by the lap the car is on
                            value = entry.GapText ?? string.Empty;
                            foreground = entry.GapBrush ?? TextBrush;
                            break;
                        }

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

                        cell.PitDetail = detail;
                        break;
                }
            }

            cell.MarkBrush = foreground;

            // The column's own background (for example behind the position), unless the cell has a coloured one already.
            if (background == null)
            {
                bool inner = column == "iR" || column == "Tyre";                // these two draw their background themselves
                Brush own = inner || row == null ? null : fill;
                background = own;
                if (row != null && light && (own != null || inner))
                {
                    // Dark text on the white background; green and red get darker so they stay readable.
                    foreground = ReferenceEquals(foreground, GreenBrush) ? DarkGreenBrush
                        : ReferenceEquals(foreground, LossBrush) ? DarkRedBrush
                        : ReferenceEquals(foreground, PurpleBrush) ? foreground
                        : DarkText;
                }
            }

            cell.Text = value;
            cell.Foreground = foreground;
            cell.Background = background;
            return cell;
        }

        /// <summary>Time gained (green) or lost (red) against the player, per recent lap, newest first.</summary>
        private static void DeltasOf(StandingsCell cell, StandingsRow row, Context context)
        {
            int count = Math.Max(1, Math.Min(MaxDelta, context.Options.GetInt("Delta.Count")));
            StandingsRow player = context.Player;
            cell.Deltas = new string[count];
            cell.DeltaBrushes = new Brush[count];
            for (int k = 0; k < count; k++)
            {
                string value = string.Empty;
                Brush brush = MutedBrush;
                if (row != null && player != null && !row.IsPlayer && row.RecentLaps != null && player.RecentLaps != null
                    && k < row.RecentLaps.Length && k < player.RecentLaps.Length)
                {
                    double delta = row.RecentLaps[k] - player.RecentLaps[k];   // positive: the other car was slower, you gained
                    value = Math.Abs(delta).ToString("0.0", CultureInfo.InvariantCulture);
                    brush = delta > 0.05 ? GreenBrush : delta < -0.05 ? LossBrush : MutedBrush;
                }

                cell.Deltas[k] = value;
                cell.DeltaBrushes[k] = brush;
            }
        }

        /// <summary>The iRating pill and its change (arrow and number), darker on a light column.</summary>
        private static void RatingOf(StandingsCell cell, StandingsRow row, bool show, bool onLight)
        {
            cell.HasRating = row != null && row.IRating > 0;
            cell.ShowRatingChange = show;
            bool has = show && row.HasIRatingChange && row.IRatingChange != 0;
            cell.RatingChange = has ? row.IRatingChange : 0;
            cell.RatingChangeBrush = has && row.IRatingChange > 0 ? (onLight ? DarkGreenBrush : GreenBrush) : (onLight ? DarkRedBrush : LossBrush);
        }

        /// <summary>The licence in two parts: class letter on the class colour, safety rating on a darker shade.</summary>
        private static void LicenceOf(StandingsCell cell, StandingsRow row)
        {
            string[] parts = (row == null ? string.Empty : row.License ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            double rating = 0;
            bool hasRating = parts.Length > 1 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out rating);
            Brush colour = row == null ? null : LicenceBrush(row.LicenseColor);
            cell.LicenceLetter = parts.Length > 0 ? parts[0] : string.Empty;
            cell.LicenceNumber = hasRating ? (Math.Floor(rating * 10) / 10).ToString("0.0", CultureInfo.InvariantCulture) : string.Empty;
            cell.LicenceShade = colour == null ? null : Shade(colour);
            cell.LicenceSoft = colour == null ? null : Soften(colour);
        }
    }
}
