using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Shiftline.Plugin
{
    /// <summary>What the overlay host needs from the standings.</summary>
    internal interface IStandingsView
    {
        Size DesignSize { get; }

        bool InRace { get; set; }

        void ApplyOptions(StandingsOptions options);

        void Update(StandingsSnapshot snapshot);

        int RowsForHeight(double height);
    }

    /// <summary>
    /// The standings for the HTML page: the layout (sizes, columns, looks) and one frame per update (rows and cells), both as
    /// JSON. Everything is computed here (CellOf, BuildEntries, LookOf, RequiredWidth);
    /// the page only draws.
    /// </summary>
    internal static partial class StandingsModel
    {
        internal sealed class HtmlLayout
        {
            public StandingsOptions Options;
            public bool InRace;
            public RowLook Look;
            public List<StandingsColumn> Columns;
            public Brush[] Fills;
            public bool[] Light;
            public Size DesignSize;
            public string Json;
        }

        internal static HtmlLayout HtmlLayoutOf(StandingsOptions options, bool inRace)
        {
            List<StandingsColumn> columns = options.Columns;
            RowLook look = LookOf(options);
            FontFamily overlayFont = OverlayFonts.Get(options.Get("Font"));
            FontWeight overlayWeight = WeightOf(options.Get("Weight"));
            double ratingWidth = 34, changeWidth = 16, deltaWidth = 28;
            double[] widths = new double[columns.Count];
            Brush[] fills = new Brush[columns.Count];
            bool[] light = new bool[columns.Count];
            double designWidth = 0;
            JArray columnJson = new JArray();
            for (int c = 0; c < columns.Count; c++)
            {
                StandingsColumn column = columns[c];
                string font = options.Get(column.Key + ".Font");
                string weight = options.Get(column.Key + ".Weight");
                FontFamily family = string.IsNullOrEmpty(font) || font == "Default" ? overlayFont : OverlayFonts.Get(font);
                FontWeight bold = string.IsNullOrEmpty(weight) || weight == "Default" ? overlayWeight : WeightOf(weight);
                if (column.Key == "Num" && (string.IsNullOrEmpty(weight) || weight == "Default") && options.GetInt("Num.Style") % 2 == 1)
                {
                    bold = FontWeights.Bold;
                }

                widths[c] = Math.Ceiling(Math.Max(options.WidthOf(column), RequiredWidth(column.Key, options, family, bold, inRace, ref ratingWidth, ref changeWidth, ref deltaWidth)));
                designWidth += widths[c];
                string fill = options.Get(column.Key + ".Bg");
                fills[c] = ColumnFill(fill, look);
                light[c] = fill == "White";
                columnJson.Add(new JObject
                {
                    { "key", column.Key },
                    { "w", widths[c] },
                    { "align", column.Align == StandingsColumn.Right ? "r" : column.Align == StandingsColumn.Center ? "c" : "l" },
                    { "label", (column.ShortLabel ?? string.Empty).ToUpperInvariant() },
                    { "font", string.IsNullOrEmpty(font) || font == "Default" ? null : font },
                    { "weight", WeightCss(bold) },
                    { "fill", Css(fills[c]) },
                });
            }

            designWidth = Math.Max(designWidth, 120);
            JArray header = new JArray();
            if (options.Header)
            {
                // the header must fit its longest possible content; otherwise the Name column grows
                double needed = 16;
                foreach (string slot in new[] { "H.Left", "H.C1", "H.C2", "H.C3", "H.Right" })
                {
                    string kind = options.Get(slot);
                    string label, icon, sample;
                    if (!HeaderItems.Describe(kind, key => options.Get(key), out label, out icon, out sample))
                    {
                        continue;
                    }

                    needed += (icon != null ? 16 : 0) + (label != null ? Measure(label + "  ", overlayFont, FontWeights.Normal) : 0)
                        + Measure(sample, overlayFont, FontWeights.SemiBold) + (slot == "H.Left" || slot == "H.Right" ? 10 : 16);
                    header.Add(new JObject { { "slot", slot }, { "kind", kind }, { "label", label }, { "icon", icon } });
                }

                needed = Math.Ceiling(needed);
                if (needed > designWidth)
                {
                    int widen = Math.Max(0, columns.FindIndex(c => c.Key == "Name"));
                    if (widen < columnJson.Count)
                    {
                        widths[widen] += needed - designWidth;
                        columnJson[widen]["w"] = widths[widen];
                    }

                    designWidth = needed;
                }
            }

            JArray footer = new JArray();
            if (options.Footer)
            {
                foreach (string item in new[] { "Last", "Delta", "Best", "Session" })
                {
                    if (options.GetBool("F." + item))
                    {
                        footer.Add(item);
                    }
                }
            }

            double round = options.Get("Corners") == "Square" ? 0 : 1;
            int gap = Math.Max(0, options.GetInt("RowGap"));
            double height = options.Rows * Pitch(options) + FixedHeight(options);
            JObject json = new JObject
            {
                { "type", "layout" },
                { "scale", options.Scale },
                { "width", designWidth + 16 },
                { "height", height },
                { "rows", options.Rows },
                { "rowHeight", RowHeight },
                { "gap", gap },
                { "round", round },
                { "font", options.Get("Font") },
                { "weight", WeightCss(overlayWeight) },
                { "card", Css(look.Card) },
                { "hf", Css(look.HeaderFooter) },
                { "header", options.Header ? header : null },
                { "labels", options.Labels },
                { "footer", options.Footer ? footer : null },
                { "tabStyle", options.Get("Tab.Style") },
                { "numStyle", options.GetInt("Num.Style") },
                { "smallCaps", options.Get("Name.Case") == "SmallCaps" },
                { "licNumber", options.GetBool("Lic.Number") },
                { "ratingWidth", ratingWidth },
                { "changeWidth", changeWidth },
                { "deltaWidth", deltaWidth },
                { "showGain", options.GetBool("iR.Gain") && inRace },
                { "tyreLaps", options.GetBool("Tyre.Laps") },
                { "animStyle", options.Get("Anim.Style") },
                { "columns", columnJson },
            };

            return new HtmlLayout
            {
                Options = options,
                InRace = inRace,
                Look = look,
                Columns = columns,
                Fills = fills,
                Light = light,
                // whole pixels: the preview shows the page as a bitmap 1:1, a fraction would drop a row of pixels
                DesignSize = new Size(Math.Ceiling((designWidth + 16) * options.Scale), Math.Ceiling(height * options.Scale)),
                Json = json.ToString(Formatting.None),
            };
        }

        /// <summary>One frame for the page: the rows (with an id per car for the animations), header values and the footer.</summary>
        internal static string HtmlFrame(HtmlLayout layout, StandingsSnapshot snapshot)
        {
            StandingsOptions options = layout.Options;
            List<StandingsColumn> columns = layout.Columns;
            bool relative = options.IsRelative;
            StandingsRow[] all = snapshot.Rows ?? new StandingsRow[0];
            Context context = new Context(all, snapshot.IsRace, options) { HasDelta = snapshot.HasDelta, DeltaToBest = snapshot.DeltaToBest };
            bool titles = !relative && options.GetBool("Titles") && columns.Any(c => c.Key == "Last" || c.Key == "Best" || c.Key == "Avg");
            List<Entry> shown = relative
                ? BuildRelative(all, context)
                : BuildEntries(all, options.Rows - (titles ? 1 : 0), options.Get("Classes"), options.GetBool("Tab"));
            if (titles && (shown.Count == 0 || shown[0] == null || shown[0].Tab == null))
            {
                shown.Insert(0, new Entry { Titles = true });
            }
            else if (titles)
            {
                shown = BuildEntries(all, options.Rows, options.Get("Classes"), options.GetBool("Tab"));
            }

            bool showGain = options.GetBool("iR.Gain") && layout.InRace;
            JArray rows = new JArray();
            int stripe = 0;
            for (int r = 0; r < options.Rows; r++)
            {
                Entry entry = r < shown.Count ? shown[r] : null;
                StandingsRow row = entry == null ? null : entry.Row;
                Brush rowBrush = row == null
                    ? (entry != null && entry.Titles && entry.Tab == null ? layout.Look.Plain : null)
                    : row.IsPlayer ? layout.Look.Player : (stripe % 2 == 0 ? layout.Look.Odd : layout.Look.Row);
                if (row != null)
                {
                    stripe++;
                }

                JArray cells = new JArray();
                for (int c = 0; c < columns.Count; c++)
                {
                    StandingsCell cell = CellOf(columns[c].Key, entry, context, relative, layout.Light[c], layout.Fills[c], showGain);
                    JObject json = CellJson(columns[c].Key, cell, row != null);
                    string title = entry != null && (entry.Tab != null || entry.Titles) && options.GetBool("Titles") ? TimeTitle(columns[c].Key, options) : null;
                    if (title != null)
                    {
                        json["t"] = title;
                        json["f"] = Css(MutedBrush);
                    }

                    cells.Add(json);
                }

                JObject tab = null;
                if (entry != null && entry.Tab != null && entry.Tab.Length > 0)
                {
                    tab = new JObject
                    {
                        { "t", TabText(entry.Tab, snapshot.Features, options) },
                        { "c", Css(ClassBrush(entry.Tab[0].ClassColor)) },
                        { "dark", ReferenceEquals(ReadableOn(ClassBrush(entry.Tab[0].ClassColor)), DarkText) },
                    };
                }

                rows.Add(new JObject
                {
                    { "id", row != null ? "c" + row.CarIdx.ToString(CultureInfo.InvariantCulture) : "s" + r.ToString(CultureInfo.InvariantCulture) },
                    { "car", row != null },
                    { "bg", Css(rowBrush) },
                    { "tab", tab },
                    { "cells", cells },
                });
            }

            JObject frame = new JObject
            {
                { "type", "frame" },
                { "anim", AnimationSeconds(options, snapshot.IsReplay) },
                { "cardOpacity", layout.Look.CardOpacity },
                { "rows", rows },
            };

            if (options.Header)
            {
                JObject values = new JObject();
                foreach (string slot in new[] { "H.Left", "H.C1", "H.C2", "H.C3", "H.Right" })
                {
                    values[slot] = HeaderItems.ValueOf(options.Get(slot), snapshot, key => options.Get(key), false, null);
                }

                double seconds = snapshot.TimeOfDay % 86400;
                frame["header"] = values;
                frame["day"] = seconds >= 6 * 3600 && seconds < 20 * 3600;
            }

            if (options.Footer)
            {
                StandingsRow player = context.Player;
                int decimals = options.GetInt("Last.Dec" + context.Mode);
                bool purple = player != null && player.BestLap > 0 && Math.Abs(player.BestLap - context.SessionBest) < 0.0005;
                JObject footer = new JObject
                {
                    { "Last", player == null ? "-" : Lap(player.LastLap, decimals) },
                    { "Best", player == null ? "-" : Lap(player.BestLap, decimals) },
                    { "BestColour", Css(purple ? PurpleBrush : GreenBrush) },
                    { "Session", context.SessionBest < double.MaxValue ? Lap(context.SessionBest, decimals) : "-" },
                    { "SessionColour", Css(PurpleBrush) },
                };

                if (context.HasDelta)
                {
                    double delta = context.DeltaToBest;
                    footer["Delta"] = Math.Abs(delta).ToString("0.00", CultureInfo.InvariantCulture);
                    footer["DeltaColour"] = Css(delta < -0.005 ? GreenBrush : delta > 0.005 ? LossBrush : TextBrush);
                    footer["DeltaDir"] = delta < -0.005 ? 1 : delta > 0.005 ? -1 : 0;
                }
                else
                {
                    footer["Delta"] = "-";
                    footer["DeltaColour"] = Css(MutedBrush);
                    footer["DeltaDir"] = 0;
                }

                frame["footer"] = footer;
            }

            return frame.ToString(Formatting.None);
        }

        private static JObject CellJson(string column, StandingsCell cell, bool car)
        {
            JObject json = new JObject { { "t", cell.Text }, { "f", Css(cell.Foreground) }, { "b", Css(cell.Background) } };
            switch (column)
            {
                case "Gain":
                    json["g"] = cell.Gain;
                    json["m"] = Css(cell.MarkBrush);
                    break;
                case "Pit":
                    if (car)
                    {
                        json["d"] = cell.PitDetail;
                        json["m"] = Css(cell.MarkBrush);
                    }

                    break;
                case "Tyre":
                    json["tc"] = cell.TyreCode;
                    json["tl"] = LetterSvg(cell.TyreCode);
                    json["tk"] = Css(cell.TyreColour);
                    break;
                case "iR":
                    json["hr"] = cell.HasRating;
                    json["rc"] = cell.RatingChange;
                    json["rk"] = Css(cell.RatingChangeBrush);
                    break;
                case "Lic":
                    json["ll"] = cell.LicenceLetter;
                    json["ln"] = cell.LicenceNumber;
                    json["ls"] = Css(cell.LicenceSoft);
                    json["lh"] = Css(cell.LicenceShade);
                    json["lt"] = cell.LicenceSoft == null ? Css(MutedBrush) : Css(ContrastText(cell.LicenceSoft));
                    break;
                case "Flag":
                    json["fl"] = cell.Flair == null ? null : FlagImages.CodeOf(cell.Flair);
                    break;
                case "Delta":
                    json["dl"] = new JArray(cell.Deltas ?? new string[0]);
                    json["dk"] = new JArray((cell.DeltaBrushes ?? new Brush[0]).Select(Css));
                    break;
            }

            return json;
        }

        private static readonly Dictionary<string, JObject> LetterSvgs = new Dictionary<string, JObject>();

        /// <summary>
        /// The tyre letter as an outline (the outline of the glyph, LetterShape) for an SVG: path data "p" and
        /// the box of the outline "x", "y", "w", "h". The page sizes the SVG to the outline, so the letter is
        /// centred by its ink, not by the font's line height.
        /// </summary>
        internal static JObject LetterSvg(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return null;
            }

            JObject svg;
            if (LetterSvgs.TryGetValue(code, out svg))
            {
                return svg;
            }

            // the glyph itself (LetterShape moves it to 0,0 with a transform that the path text would not carry)
            GeometryGroup moved = LetterShape(code) as GeometryGroup;
            Geometry shape = moved != null && moved.Children.Count == 1 ? moved.Children[0] : LetterShape(code);
            PathGeometry path = PathGeometry.CreateFromGeometry(shape);
            string data = path.ToString(CultureInfo.InvariantCulture);
            if (data.StartsWith("F0", StringComparison.Ordinal) || data.StartsWith("F1", StringComparison.Ordinal))
            {
                data = data.Substring(2).TrimStart();
            }

            Rect bounds = shape.Bounds;
            svg = new JObject
            {
                { "p", data },
                { "x", Math.Round(bounds.X, 2) },
                { "y", Math.Round(bounds.Y, 2) },
                { "w", Math.Round(bounds.Width, 2) },
                { "h", Math.Round(bounds.Height, 2) },
                { "evenodd", path.FillRule == FillRule.EvenOdd },
            };
            LetterSvgs[code] = svg;
            return svg;
        }

        /// <summary>Rows that fit into this height (while the window is pulled longer or shorter).</summary>
        internal static int RowsFor(StandingsOptions options, double height)
        {
            double inner = height / options.Scale - FixedHeight(options);
            return Math.Max(3, Math.Min(40, (int)Math.Round(inner / Pitch(options))));
        }

        /// <summary>A brush as CSS colour, null for none.</summary>
        internal static string Css(Brush brush)
        {
            SolidColorBrush solid = brush as SolidColorBrush;
            if (solid == null)
            {
                return null;
            }

            Color c = solid.Color;
            double alpha = c.A / 255.0 * solid.Opacity;
            return "rgba(" + c.R + "," + c.G + "," + c.B + "," + alpha.ToString("0.###", CultureInfo.InvariantCulture) + ")";
        }

        private static int WeightCss(FontWeight weight)
        {
            return weight.ToOpenTypeWeight();
        }
    }

    /// <summary>The standings (or relatives) drawn by the HTML page in an overlay window.</summary>
    internal sealed class StandingsHtmlView : IStandingsView
    {
        public const string Page = "standings.html";
        private StandingsModel.HtmlLayout _layout;
        private IHtmlSurface _window;
        private string _lastFrame;                          // sent again after a (re)load, so a still preview is not left empty

        public StandingsHtmlView() : this(OverlayData.StandingsKey)
        {
        }

        /// <param name="overlay">OverlayData.StandingsKey, or OverlayData.RelativesKey for the cars around you.</param>
        public StandingsHtmlView(string overlay)
        {
            InRace = true;
            ApplyOptions(StandingsOptions.Load(overlay));
        }

        public bool InRace { get; set; }

        public Size DesignSize
        {
            get { return _layout.DesignSize; }
        }

        /// <summary>Connects the view to its window or preview; the layout is sent again whenever the page (re)loads.</summary>
        public void Attach(IHtmlSurface window)
        {
            Detach();
            _window = window;
            _window.PageReady += OnPageReady;
            OnPageReady();              // a page that is already loaded (the shared preview) takes it right away
        }

        /// <summary>Lets go of the window or preview (the shared preview moves on to another settings page).</summary>
        public void Detach()
        {
            if (_window != null)
            {
                _window.PageReady -= OnPageReady;
                _window = null;
            }
        }

        private void OnPageReady()
        {
            SendLayout();
            if (_lastFrame != null && _window != null)
            {
                _window.Post(_lastFrame);
            }
        }

        public void ApplyOptions(StandingsOptions options)
        {
            _layout = StandingsModel.HtmlLayoutOf(options, InRace);
            SendLayout();
        }

        public void Update(StandingsSnapshot snapshot)
        {
            _lastFrame = StandingsModel.HtmlFrame(_layout, snapshot);
            if (_window != null)
            {
                _window.Post(_lastFrame);
            }
        }

        public int RowsForHeight(double height)
        {
            return StandingsModel.RowsFor(_layout.Options, height);
        }

        private void SendLayout()
        {
            if (_window != null)
            {
                _window.Post(_layout.Json);
            }
        }
    }
}
