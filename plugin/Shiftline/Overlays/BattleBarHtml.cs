using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Shiftline.Plugin
{
    /// <summary>What the overlay host and the settings page need from the Battle Bar.</summary>
    internal interface IBattleBarView
    {
        Size DesignSize { get; }

        void ApplyOptions(BattleBarOptions options);

        void Update(StandingsSnapshot snapshot);
    }

    /// <summary>
    /// The Battle Bar for the HTML page: the layout (sizes, items, looks) and one frame per update (the cards with their
    /// values and colours), both as JSON, from the rules of BattleBarModel; the page only draws and animates.
    /// </summary>
    internal static partial class BattleBarModel
    {
        // ---------------------------------------------------------------- sizes

        internal static bool OnItem(BattleBarOptions options, string key)
        {
            return options.Items.Any(c => c.Key == key);
        }

        internal static List<string> BottomItemsOf(BattleBarOptions options)
        {
            return options.Items.Select(c => c.Key).Where(k => !LeftItems.Contains(k) && !TopItems.Contains(k)).ToList();
        }

        /// <summary>One line when only position, class, name, make and gap are on; two lines when more is shown.</summary>
        internal static double CardHeightOf(BattleBarOptions options)
        {
            return BottomItemsOf(options).Count > 0 ? TwoLineHeight : OneLineHeight;
        }

        internal static double GapWidthOf(BattleBarOptions options)
        {
            return options.GetBool("Trend") ? 46 : 34;
        }

        internal static double NameWidthOf(BattleBarOptions options)
        {
            return options.Get("Name.Format") == "Code" ? 40 : Math.Max(60, options.GetInt("Name.Width"));
        }

        /// <summary>Width of a normal card (unscaled): frame, padding, the left block and the first line.</summary>
        internal static double CardWidthOf(BattleBarOptions options)
        {
            double left = (OnItem(options, "Pos") ? 24 + 5 : 0) + (OnItem(options, "Class") ? 3 + 7 : 0);
            double line = OnItem(options, "Name") ? NameWidthOf(options) : 0;
            if (OnItem(options, "Flag"))
            {
                line += FlagWidth + FlagGap;
            }

            if (OnItem(options, "Make"))
            {
                line += (line > 0 ? Space : 0) + MakeWidth;
            }

            if (OnItem(options, "Gap"))
            {
                line += (line > 0 ? 4 : 0) + GapWidthOf(options);
            }

            if (line == 0)
            {
                line = 90;                  // only second-line items: room for them under nothing
            }

            return Math.Ceiling(2 * Frame + 2 * 7 + left + line);
        }

        // ---------------------------------------------------------------- HTML layout

        internal sealed class HtmlLayout
        {
            public BattleBarOptions Options;
            public Brush Card;
            public Brush Player;
            public int Slots;
            public Size DesignSize;
            public string Json;
        }

        private static byte Alpha(BattleBarOptions options, string key)
        {
            return (byte)Math.Round(255 * Math.Max(0, Math.Min(100, options.GetInt(key))) / 100.0);
        }

        internal static HtmlLayout HtmlLayoutOf(BattleBarOptions options)
        {
            // the big tile around everything has the overlay opacity; the cards and side boxes the table opacity
            Brush panel = StandingsOverlay.Frozen(Alpha(options, "Opacity"), 0x0E, 0x12, 0x19);
            byte alpha = Alpha(options, "RowOpacity");
            Brush card = StandingsOverlay.Frozen(alpha, 0x0E, 0x12, 0x19);
            Brush hf = StandingsOverlay.Frozen(Alpha(options, "HfOpacity"), 0x0E, 0x12, 0x19);
            Brush player = StandingsOverlay.Frozen((byte)Math.Max((int)alpha, 0xC0), 0x16, 0x2A, 0x3E);
            string mode = options.Get("Mode");
            double cardWidth = CardWidthOf(options);
            double cardHeight = CardHeightOf(options);
            double rowHeight = mode == "Cards" ? Math.Ceiling(cardHeight * PlayerZoom) : cardHeight;

            // the places of the cards: the player's card in the cards layout is drawn larger, the leader stands a little apart
            JArray xs = new JArray();
            int playerSlot = -1;
            double stripWidth;
            int slots = 0;
            if (mode == "Cards")
            {
                int ahead = Math.Max(1, Math.Min(4, options.GetInt("Ahead")));
                int behind = Math.Max(1, Math.Min(4, options.GetInt("Behind")));
                bool leader = options.GetBool("Leader");
                slots = (leader ? 1 : 0) + ahead + 1 + behind;
                playerSlot = (leader ? 1 : 0) + ahead;
                double x = 0;
                for (int i = 0; i < slots; i++)
                {
                    xs.Add(x);
                    x += cardWidth * (i == playerSlot ? PlayerZoom : 1) + (leader && i == 0 ? LeaderGap : CardGap);
                }

                stripWidth = x - CardGap;
            }
            else
            {
                int count = Math.Max(3, options.GetInt("Ticker.Cars"));
                double pitch = cardWidth + CardGap;
                if (mode == "Ticker")
                {
                    slots = count;
                    for (int i = 0; i < count; i++)
                    {
                        xs.Add(i * pitch);
                    }
                }

                stripWidth = count * pitch - CardGap;
            }

            string left = options.Get("Side.Left");
            string right = options.Get("Side.Right");
            bool hasLeft = !string.IsNullOrEmpty(left) && left != "Off";
            bool hasRight = !string.IsNullOrEmpty(right) && right != "Off";
            double width = stripWidth + (hasLeft ? SideWidth + CardGap : 0) + (hasRight ? SideWidth + CardGap : 0);

            JArray header = new JArray();
            double headerWidth = 0;
            if (options.Header)
            {
                // measured with the WPF text engine, as the page lays the items out the same way
                StackPanel measure = new StackPanel { Orientation = Orientation.Horizontal };
                System.Windows.Documents.TextElement.SetFontFamily(measure, OverlayFonts.Get(options.Get("Font")));
                foreach (string slot in new[] { "H.S1", "H.S2", "H.S3", "H.S4", "H.S5", "H.S6", "H.S7", "H.S8" })
                {
                    string kind = options.Get(slot);
                    HeaderItem item = HeaderItems.Create(kind, key => options.Get(key), TextBrush, MutedBrush, 12, new Thickness(6, 0, 6, 0));
                    string label, icon, sample;
                    if (item == null || !HeaderItems.Describe(kind, key => options.Get(key), out label, out icon, out sample))
                    {
                        continue;
                    }

                    item.ValueRun.Text = item.Sample;
                    measure.Children.Add(item.Panel);
                    header.Add(new JObject
                    {
                        { "slot", slot },
                        { "kind", kind },
                        { "label", label },
                        { "icon", icon },
                        { "max", kind == "Track" ? 110 : kind == "Car" ? 120 : 0 },
                    });
                }

                measure.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                headerWidth = Math.Ceiling(measure.DesiredSize.Width) + 8;
            }

            JArray footer = new JArray();
            if (options.Footer)
            {
                foreach (string item in new[] { "Last", "Delta", "Best" })
                {
                    if (options.GetBool("F." + item))
                    {
                        footer.Add(item);
                    }
                }
            }

            double designWidth = Math.Max(width, headerWidth) + 16;
            double designHeight = rowHeight + 16 + (options.Header ? LineHeight + 4 : 0) + (options.Footer ? LineHeight + 4 : 0);
            double scale = options.Scale * BaseScale;
            Brush posFill = PositionFillOf(options);
            JArray right2 = new JArray(new[] { "Pit", "Tyre", "Gain" }.Where(k => OnItem(options, k)));
            JObject json = new JObject
            {
                { "type", "battle-layout" },
                { "scale", scale },
                { "width", designWidth },
                { "height", designHeight },
                { "mode", mode },
                { "round", options.Get("Corners") == "Square" ? 0 : 1 },
                { "font", options.Get("Font") },
                { "weight", StandingsOverlay.WeightOf(options.Get("Weight")).ToOpenTypeWeight() },
                { "panel", StandingsOverlay.Css(panel) },
                { "card", StandingsOverlay.Css(card) },
                { "hf", StandingsOverlay.Css(hf) },
                { "cardW", cardWidth },
                { "cardH", cardHeight },
                { "rowH", rowHeight },
                { "twoLines", BottomItemsOf(options).Count > 0 },
                { "zoom", PlayerZoom },
                { "gap", CardGap },
                { "xs", xs },
                { "playerSlot", playerSlot },
                { "stripW", stripWidth },
                { "pitch", cardWidth + CardGap },
                { "pos", OnItem(options, "Pos") },
                { "cls", OnItem(options, "Class") },
                { "flag", OnItem(options, "Flag") },
                { "name", OnItem(options, "Name") },
                { "make", OnItem(options, "Make") },
                { "gapOn", OnItem(options, "Gap") },
                { "gapW", GapWidthOf(options) },
                { "ir", OnItem(options, "iR") },
                { "lic", OnItem(options, "Lic") },
                { "licNumber", options.GetBool("Lic.Number") },
                { "right", right2 },
                { "posFill", StandingsOverlay.Css(posFill) },
                { "posDark", ReferenceEquals(posFill, WhiteFill) },
                { "header", options.Header ? header : null },
                { "headerW", headerWidth },
                { "left", hasLeft ? new JObject { { "kind", left }, { "label", SideLabel(left) } } : null },
                { "rightSide", hasRight ? new JObject { { "kind", right }, { "label", SideLabel(right) } } : null },
                { "footer", options.Footer ? footer : null },
                { "style", options.Get("Anim.Style") },
                { "flash", options.GetBool("Anim.Flash") },
                { "speed", options.Get("Ticker.Speed") == "Slow" ? 25 : options.Get("Ticker.Speed") == "Fast" ? 75 : 45 },
                { "playerFrame", PlayerFrameJson(options.Get("Frame.Player")) },
                { "battle", StandingsOverlay.Css(BattleColour(options.Get("Battle.Colour"))) },
            };

            return new HtmlLayout
            {
                Options = options,
                Card = card,
                Player = player,
                Slots = slots,
                DesignSize = new Size(designWidth * scale, designHeight * scale),
                Json = json.ToString(Formatting.None),
            };
        }

        private static Brush PositionFillOf(BattleBarOptions options)
        {
            switch (options.Get("PosBg"))
            {
                case "Subtle":
                    return SubtleFill;
                case "None":
                    return Brushes.Transparent;
                default:
                    return WhiteFill;
            }
        }

        /// <summary>The frame of the player's card: a colour, a turning gradient ("flow"), or null for none.</summary>
        private static JObject PlayerFrameJson(string kind)
        {
            switch (kind)
            {
                case "Off":
                    return null;
                case "White":
                    return new JObject { { "c", StandingsOverlay.Css(WhiteFrame) } };
                case "Green":
                    return new JObject { { "c", StandingsOverlay.Css(GreenFrame) } };
                case "Shiftline":
                    return new JObject { { "flow", new JArray("#01ABFF", "#903AA1", "#FF8C00", "#D60000") } };
                case "BlueFlow":
                    return new JObject { { "flow", new JArray("#3FA9F5", "#A8DCFF", "#1E7FCC", "#7CC6FF") } };
                case "GreenFlow":
                    return new JObject { { "flow", new JArray("#3DDC84", "#B4F5CF", "#1FA85E", "#86EDB2") } };
                default:
                    return new JObject { { "c", StandingsOverlay.Css(AccentBrush) } };
            }
        }

        // ---------------------------------------------------------------- HTML frame

        internal static string HtmlFrame(HtmlLayout layout, StandingsSnapshot snapshot)
        {
            BattleBarOptions options = layout.Options;
            Field field = new Field(snapshot, options);
            string mode = options.Get("Mode");
            JObject frame = new JObject
            {
                { "type", "battle-frame" },
                { "anim", AnimationSecondsOf(options, snapshot.IsReplay) },
                { "fade", FadeSecondsOf(options) },
            };

            if (mode == "Scroll")
            {
                JArray order = new JArray();
                foreach (StandingsRow row in field.Order.Take(64))
                {
                    order.Add(CardJson(layout, row, field));
                }

                frame["order"] = order;
                frame["playerIndex"] = field.PlayerIndex < 64 ? field.PlayerIndex : -1;
            }
            else
            {
                List<StandingsRow> shown = mode == "Cards" ? ChooseCards(options, field) : ChooseTicker(field, layout.Slots);
                JArray cards = new JArray();
                for (int i = 0; i < layout.Slots; i++)
                {
                    StandingsRow row = i < shown.Count ? shown[i] : null;
                    cards.Add(row == null ? null : CardJson(layout, row, field));
                }

                frame["cards"] = cards;
            }

            StandingsRow player = field.Player;
            frame["playerPos"] = player == null ? 0 : field.PositionOf(player);
            if (options.Header)
            {
                StandingsRow lapRow = field.Order.Count > 0 && !field.ByTrack ? field.Order[0] : field.Player;
                JObject values = new JObject();
                foreach (string slot in new[] { "H.S1", "H.S2", "H.S3", "H.S4", "H.S5", "H.S6", "H.S7", "H.S8" })
                {
                    values[slot] = HeaderItems.ValueOf(options.Get(slot), snapshot, key => options.Get(key), true, lapRow);
                }

                double seconds = snapshot.TimeOfDay % 86400;
                frame["header"] = values;
                frame["day"] = seconds >= 6 * 3600 && seconds < 20 * 3600;
            }

            JObject sides = new JObject();
            foreach (string side in new[] { "Side.Left", "Side.Right" })
            {
                string kind = options.Get(side);
                if (string.IsNullOrEmpty(kind) || kind == "Off")
                {
                    continue;
                }

                Brush brush;
                string value = SideValue(kind, snapshot, field, out brush);
                sides[side == "Side.Left" ? "left" : "right"] = new JObject
                {
                    { "v", string.IsNullOrEmpty(value) ? "-" : value },
                    { "c", StandingsOverlay.Css(brush) },
                };
            }

            frame["sides"] = sides;
            if (options.Footer)
            {
                bool purple = player != null && player.BestLap > 0 && Math.Abs(player.BestLap - field.SessionBest) < 0.0005;
                JObject footer = new JObject
                {
                    { "Last", player == null ? "-" : StandingsOverlay.Lap(player.LastLap, 3) },
                    { "Best", player == null ? "-" : StandingsOverlay.Lap(player.BestLap, 3) },
                    { "BestColour", StandingsOverlay.Css(purple ? PurpleBrush : GreenBrush) },
                    { "Delta", "-" },
                    { "DeltaColour", StandingsOverlay.Css(MutedBrush) },
                };
                if (snapshot.HasDelta)
                {
                    double delta = snapshot.DeltaToBest;
                    footer["Delta"] = (delta > 0 ? "+" : delta < 0 ? "-" : string.Empty) + Math.Abs(delta).ToString("0.00", CultureInfo.InvariantCulture);
                    footer["DeltaColour"] = StandingsOverlay.Css(delta < -0.005 ? GreenBrush : delta > 0.005 ? LossBrush : TextBrush);
                }

                frame["footer"] = footer;
            }

            return frame.ToString(Formatting.None);
        }

        /// <summary>One card: every value and colour it shows.</summary>
        private static JObject CardJson(HtmlLayout layout, StandingsRow row, Field field)
        {
            BattleBarOptions options = layout.Options;
            int position = field.PositionOf(row);
            JObject card = new JObject
            {
                { "id", "c" + row.CarIdx.ToString(CultureInfo.InvariantCulture) },
                { "pos", position > 0 ? position.ToString(CultureInfo.InvariantCulture) : "-" },
                { "cls", StandingsOverlay.Css(StandingsOverlay.ClassBrush(row.ClassColor)) },
                { "bg", StandingsOverlay.Css(row.IsPlayer ? layout.Player : layout.Card) },
            };

            if (OnItem(options, "Name"))
            {
                int laps = LapsApart(row, field);
                card["name"] = NameOf(options, row);
                card["nameC"] = StandingsOverlay.Css(laps < 0 ? LappedBrush : laps > 0 ? LappingBrush : TextBrush);
                card["laps"] = laps == 0 ? string.Empty : (laps > 0 ? "+" : "-") + Math.Abs(laps).ToString(CultureInfo.InvariantCulture) + "L";
            }

            if (OnItem(options, "Flag"))
            {
                card["flag"] = row.Flair == null ? null : FlagImages.CodeOf(row.Flair);
            }

            if (OnItem(options, "Make"))
            {
                card["make"] = row.Make ?? string.Empty;
            }

            if (OnItem(options, "iR"))
            {
                card["ir"] = StandingsOverlay.FormatIRating(row.IRating, "K1");
            }

            if (OnItem(options, "Lic"))
            {
                string[] parts = (row.License ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                double rating = 0;
                bool hasRating = parts.Length > 1 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out rating);
                Brush colour = StandingsOverlay.LicenceBrush(row.LicenseColor);
                if (colour != null && parts.Length > 0)
                {
                    Brush soft = StandingsOverlay.Soften(colour);
                    card["lic"] = new JObject
                    {
                        { "l", parts[0] },
                        { "n", hasRating ? (Math.Floor(rating * 10) / 10).ToString("0.0", CultureInfo.InvariantCulture) : string.Empty },
                        { "s", StandingsOverlay.Css(soft) },
                        { "t", StandingsOverlay.Css(StandingsOverlay.ContrastText(soft)) },
                        { "h", StandingsOverlay.Css(StandingsOverlay.Shade(colour)) },
                    };
                }
            }

            if (OnItem(options, "Pit"))
            {
                card["pit"] = row.OnPitRoad ? "PIT" : row.TrackSurface < 0 ? "OUT" : string.Empty;
            }

            if (OnItem(options, "Gain"))
            {
                int start = field.StartPositionOf(row);
                int gained = start > 0 && position > 0 ? start - position : 0;
                card["gain"] = gained;
            }

            if (OnItem(options, "Tyre"))
            {
                string type = row.TireType ?? string.Empty;
                if (type.Length > 0)
                {
                    string code;
                    Brush colour;
                    StandingsOverlay.TyreLook(type, out code, out colour);
                    card["tyre"] = new JObject { { "c", code }, { "k", StandingsOverlay.Css(colour) }, { "l", StandingsOverlay.LetterSvg(code) } };
                }
            }

            double gap;
            bool sameLap;
            string gapText = GapText(options, row, field, out gap, out sameLap);
            if (OnItem(options, "Gap"))
            {
                card["gap"] = gapText;
                card["trend"] = TrendOf(options, row, field);
            }

            card["frame"] = FrameKind(options, row, field, gap, sameLap);
            return card;
        }
    }

    /// <summary>The Battle Bar drawn by the HTML page, in an overlay window or in the preview of the settings page.</summary>
    internal sealed class BattleBarHtmlView : IBattleBarView
    {
        public const string Page = "battlebar.html";
        private BattleBarModel.HtmlLayout _layout;
        private IHtmlSurface _window;
        private string _lastFrame;                          // sent again after a (re)load, so a still preview is not left empty

        public BattleBarHtmlView()
        {
            ApplyOptions(BattleBarOptions.Load());
        }

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

        public void Detach()
        {
            if (_window != null)
            {
                _window.PageReady -= OnPageReady;
                _window = null;
            }
        }

        public void ApplyOptions(BattleBarOptions options)
        {
            _layout = BattleBarModel.HtmlLayoutOf(options);
            _lastFrame = null;
            SendLayout();
        }

        public void Update(StandingsSnapshot snapshot)
        {
            _lastFrame = BattleBarModel.HtmlFrame(_layout, snapshot);
            if (_window != null)
            {
                _window.Post(_lastFrame);
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

        private void SendLayout()
        {
            if (_window != null)
            {
                _window.Post(_layout.Json);
            }
        }
    }
}
