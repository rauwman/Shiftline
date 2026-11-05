using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Shiftline.Plugin
{
    /// <summary>
    /// A small row of LEDs that plays how a pattern looks, with the colours chosen in the settings. Each choice tile
    /// carries one of these, so you see what the choice does before you pick it.
    /// Kind: "rpm" (rev bar fill), "brake" (brake bar), "redline" (what happens at the redline) or "layout" (the whole strip
    /// as it sits on the dash with the LEDs the brake bar uses, drawn from the layout and brake position settings; still, not animated).
    /// The colours are read from the Settings object of the page: the colour theme named by ThemeSetting, the colour mode
    /// named by ModeSetting (or ModeOverride), the brake colour named by ColourSetting and the flash colour named by FlashSetting.
    /// </summary>
    public class LedPreview : FrameworkElement
    {
        public static readonly DependencyProperty LedCountProperty = DependencyProperty.Register(
            "LedCount", typeof(int), typeof(LedPreview), new FrameworkPropertyMetadata(14, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public static readonly DependencyProperty AnimateProperty = DependencyProperty.Register(
            "Animate", typeof(bool), typeof(LedPreview), new PropertyMetadata(true, OnAnimateChanged));

        public static readonly DependencyProperty StillProperty = DependencyProperty.Register(
            "Still", typeof(bool), typeof(LedPreview), new PropertyMetadata(false, OnAnimateChanged));

        public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
            "Kind", typeof(string), typeof(LedPreview), new PropertyMetadata("rpm"));

        public static readonly DependencyProperty PatternProperty = DependencyProperty.Register(
            "Pattern", typeof(string), typeof(LedPreview), new PropertyMetadata("leftRight"));

        public static readonly DependencyProperty ThemeSettingProperty = DependencyProperty.Register(
            "ThemeSetting", typeof(string), typeof(LedPreview), new PropertyMetadata("RpmThemeId"));

        public static readonly DependencyProperty ModeSettingProperty = DependencyProperty.Register(
            "ModeSetting", typeof(string), typeof(LedPreview), new PropertyMetadata("RpmColourMode"));

        public static readonly DependencyProperty ModeOverrideProperty = DependencyProperty.Register(
            "ModeOverride", typeof(string), typeof(LedPreview), new PropertyMetadata(null));

        public static readonly DependencyProperty ColourSettingProperty = DependencyProperty.Register(
            "ColourSetting", typeof(string), typeof(LedPreview), new PropertyMetadata("BrakeColour"));

        public static readonly DependencyProperty FlashSettingProperty = DependencyProperty.Register(
            "FlashSetting", typeof(string), typeof(LedPreview), new PropertyMetadata("RpmFlashColour"));

        public static readonly DependencyProperty EffectNameProperty = DependencyProperty.Register(
            "EffectName", typeof(string), typeof(LedPreview), new PropertyMetadata(null));

        public static readonly DependencyProperty Flash2SettingProperty = DependencyProperty.Register(
            "Flash2Setting", typeof(string), typeof(LedPreview), new PropertyMetadata("RpmFlashColour2"));

        private static readonly Brush OffBrush = CreateOffBrush();

        private static Brush CreateOffBrush()
        {
            Brush brush = new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF));
            brush.Freeze();
            return brush;
        }

        private const double Radius = 5;
        private const double Gap = 5;
        private const double RowHeight = 18;
        private const double LayoutRadius = 4;         // the row that shows where the brake bar sits on the whole strip
        private const double LayoutGap = 4;
        private const double CycleSeconds = 2.2;       // one fill, then a short hold, then it starts again
        private const double FillShare = 0.6;          // share of the cycle used for filling

        private static readonly DateTime Origin = DateTime.UtcNow;
        private bool _looping;
        private Settings _settings;

        public LedPreview()
        {
            IsHitTestVisible = false;
            Loaded += (sender, args) =>
            {
                Attach(DataContext as Settings);
                UpdateTimer();
            };
            Unloaded += (sender, args) =>
            {
                StopLoop();
                Attach(null);
            };
        }

        public int LedCount
        {
            get { return (int)GetValue(LedCountProperty); }
            set { SetValue(LedCountProperty, value); }
        }

        /// <summary>Plays the animation. When off, all LEDs of the row are shown switched off.</summary>
        public bool Animate
        {
            get { return (bool)GetValue(AnimateProperty); }
            set { SetValue(AnimateProperty, value); }
        }

        private static void OnAnimateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((LedPreview)d).UpdateTimer();
        }

        // The animation is redrawn with every frame the screen shows (CompositionTarget.Rendering), so it runs smoothly.
        private void StartLoop()
        {
            if (!_looping)
            {
                CompositionTarget.Rendering += OnRendering;
                _looping = true;
            }
        }

        private void StopLoop()
        {
            if (_looping)
            {
                CompositionTarget.Rendering -= OnRendering;
                _looping = false;
            }
        }

        private void OnRendering(object sender, EventArgs e)
        {
            InvalidateVisual();
        }

        private void UpdateTimer()
        {
            if (IsLoaded && Animate && !Still && Kind != "layout")
            {
                StartLoop();
            }
            else
            {
                StopLoop();
            }

            InvalidateVisual();
        }

        /// <summary>Shows the finished pattern in its colours without any movement (for example the colour mode tiles).</summary>
        public bool Still
        {
            get { return (bool)GetValue(StillProperty); }
            set { SetValue(StillProperty, value); }
        }

        public string Kind
        {
            get { return (string)GetValue(KindProperty); }
            set { SetValue(KindProperty, value); }
        }

        public string Pattern
        {
            get { return (string)GetValue(PatternProperty); }
            set { SetValue(PatternProperty, value); }
        }

        public string ThemeSetting
        {
            get { return (string)GetValue(ThemeSettingProperty); }
            set { SetValue(ThemeSettingProperty, value); }
        }

        public string ModeSetting
        {
            get { return (string)GetValue(ModeSettingProperty); }
            set { SetValue(ModeSettingProperty, value); }
        }

        public string ModeOverride
        {
            get { return (string)GetValue(ModeOverrideProperty); }
            set { SetValue(ModeOverrideProperty, value); }
        }

        public string ColourSetting
        {
            get { return (string)GetValue(ColourSettingProperty); }
            set { SetValue(ColourSettingProperty, value); }
        }

        /// <summary>With Kind "layout": the name of a side effect (for example "Tc"); the preview then lights the LEDs of its start/count settings.</summary>
        public string EffectName
        {
            get { return (string)GetValue(EffectNameProperty); }
            set { SetValue(EffectNameProperty, value); }
        }

        /// <summary>The second flash colour of the "flashAlt" redline pattern.</summary>
        public string Flash2Setting
        {
            get { return (string)GetValue(Flash2SettingProperty); }
            set { SetValue(Flash2SettingProperty, value); }
        }

        public string FlashSetting
        {
            get { return (string)GetValue(FlashSettingProperty); }
            set { SetValue(FlashSettingProperty, value); }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            if (Kind == "layout")
            {
                double pitch = LayoutRadius * 2 + LayoutGap;
                int sideCount = Math.Max(1, ReadInt("SideLeds", 5));
                int centreCount = Math.Max(2, ReadInt("CentreLeds", 16));
                return new Size(LayoutRadius * 2 + pitch * (centreCount + 1), LayoutRadius * 2 + pitch * sideCount);   // the row sits one level above the sides
            }

            return new Size(LedCount * Radius * 2 + (LedCount - 1) * Gap, RowHeight);
        }

        private void Attach(Settings settings)
        {
            if (_settings != null)
            {
                _settings.PropertyChanged -= OnSettingChanged;
            }

            _settings = settings;
            if (_settings != null)
            {
                _settings.PropertyChanged += OnSettingChanged;
            }
        }

        private void OnSettingChanged(object sender, PropertyChangedEventArgs e)
        {
            if (Kind == "layout")
            {
                InvalidateMeasure();
            }

            InvalidateVisual();
        }

        // ---------------------------------------------------------------- reading the settings

        private string ReadText(string name, string fallback)
        {
            if (_settings == null || string.IsNullOrEmpty(name))
            {
                return fallback;
            }

            PropertyInfo property = typeof(Settings).GetProperty(name);
            string value = property == null ? null : property.GetValue(_settings, null) as string;
            return value ?? fallback;
        }

        private int ReadInt(string name, int fallback)
        {
            if (_settings == null)
            {
                return fallback;
            }

            PropertyInfo property = typeof(Settings).GetProperty(name);
            return property != null && property.PropertyType == typeof(int) ? (int)property.GetValue(_settings, null) : fallback;
        }

        private bool ReadBool(string name, bool fallback)
        {
            if (_settings == null)
            {
                return fallback;
            }

            PropertyInfo property = typeof(Settings).GetProperty(name);
            return property != null && property.PropertyType == typeof(bool) ? (bool)property.GetValue(_settings, null) : fallback;
        }

        private static Color ParseColour(string hex, Color fallback)
        {
            try
            {
                return (Color)ColorConverter.ConvertFromString(hex);
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        private List<Color> ReadThemeColours()
        {
            List<Color> colours = new List<Color>();
            if (_settings != null)
            {
                string id = ReadText(ThemeSetting, null);
                ColourTheme theme = _settings.Themes.ToArray().FirstOrDefault(candidate => candidate.Id == id);
                if (theme != null)
                {
                    foreach (ColourStop stop in theme.Stops.ToArray())
                    {
                        colours.Add(ParseColour(stop.Hex, Colors.White));
                    }
                }
            }

            if (colours.Count < 2)
            {
                colours = new List<Color> { Colors.LimeGreen, Colors.Yellow, Colors.Red };
            }

            return colours;
        }

        // ---------------------------------------------------------------- colours and patterns

        private static Color Blend(Color a, Color b, double t)
        {
            return Color.FromRgb(
                (byte)Math.Round(a.R + (b.R - a.R) * t),
                (byte)Math.Round(a.G + (b.G - a.G) * t),
                (byte)Math.Round(a.B + (b.B - a.B) * t));
        }

        private static Color Sample(List<Color> colours, double t, bool gradient)
        {
            t = Math.Max(0, Math.Min(1, t));
            if (gradient)
            {
                double scaled = t * (colours.Count - 1);
                int index = Math.Min(colours.Count - 2, (int)Math.Floor(scaled));
                return Blend(colours[index], colours[index + 1], scaled - index);
            }

            return colours[Math.Min(colours.Count - 1, (int)Math.Floor(t * colours.Count))];
        }

        /// <summary>The LEDs that light at each step of a fill pattern, for a row of count LEDs.</summary>
        private static List<int[]> Steps(string pattern, int count, int sideLeds)
        {
            List<int[]> steps = new List<int[]>();
            int lo = (count - 1) / 2;
            int hi = count / 2;
            switch (pattern)
            {
                case "middle":
                    for (int k = 0; k <= lo; k++)
                    {
                        steps.Add(new[] { k, count - 1 - k }.Distinct().ToArray());
                    }

                    break;
                case "centreOut":
                    for (int k = 0; k <= lo; k++)
                    {
                        steps.Add(new[] { lo - k, hi + k }.Distinct().ToArray());
                    }

                    break;
                case "leftHalf":
                    for (int k = 0; k <= lo; k++)
                    {
                        steps.Add(new[] { k });
                    }

                    break;
                case "rightHalf":
                    for (int k = 0; k <= lo; k++)
                    {
                        steps.Add(new[] { count - 1 - k });
                    }

                    break;
                case "rightLeft":
                    for (int k = 0; k < count; k++)
                    {
                        steps.Add(new[] { count - 1 - k });
                    }

                    break;
                case "off":
                    break;
                default:
                    for (int k = 0; k < count; k++)
                    {
                        steps.Add(new[] { k });
                    }

                    break;
            }

            return steps;
        }

        private static string BrakePattern(string direction)
        {
            switch (direction)
            {
                case "rightLeft":
                    return "rightLeft";
                case "inOut":
                    return "centreOut";
                case "leftRight":
                    return "leftRight";
                default:
                    return "middle";             // Out to In: from both ends towards the middle
            }
        }

        // ---------------------------------------------------------------- drawing

        /// <summary>
        /// The strip as it sits on the dash: a column of side LEDs on the left and on the right (the innermost LED at the top,
        /// the outermost at the bottom) and the row of centre LEDs between the tops of the columns. The LEDs the brake bar
        /// uses are lit.
        /// </summary>
        /// <summary>One pedal at a part of its travel, lit in its area: the same areas as the LED script (PedalBrake, PedalThrottle, PedalClutch).</summary>
        private static void FillPedal(string area, double value, Color colour, Color?[] sides, Color?[] centre)
        {
            int length = centre.Length;
            int half = length / 2;
            int sideCount = sides.Length;
            switch (area)
            {
                case "centreMiddle":
                    int lo = (length - 1) / 2;
                    int hi = length / 2;
                    for (int k = 0; k < (int)Math.Round(value * (lo + 1)); k++)
                    {
                        centre[lo - k] = colour;
                        centre[hi + k] = colour;
                    }

                    break;
                case "centreLeft":
                    for (int i = 0; i < (int)Math.Round(value * length); i++)
                    {
                        centre[i] = colour;
                    }

                    break;
                case "centreRight":
                    for (int i = 0; i < (int)Math.Round(value * length); i++)
                    {
                        centre[length - 1 - i] = colour;
                    }

                    break;
                case "halfLeft":
                    for (int i = 0; i < (int)Math.Round(value * half); i++)
                    {
                        centre[half - 1 - i] = colour;
                    }

                    break;
                case "halfRight":
                    for (int i = 0; i < (int)Math.Round(value * (length - half)); i++)
                    {
                        centre[half + i] = colour;
                    }

                    break;
                case "centreEnds":
                    for (int i = 0; i < (int)Math.Round(value * half); i++)
                    {
                        centre[i] = colour;
                        centre[length - 1 - i] = colour;
                    }

                    break;
                case "sideLeft":
                case "sideRight":
                case "sideBoth":
                    for (int i = 0; i < (int)Math.Round(value * sideCount); i++)
                    {
                        sides[i] = colour;       // the same LEDs on both sides in the preview; the side is shown by the drawing below
                    }

                    break;
            }
        }

        private void RenderLayout(DrawingContext dc)
        {
            if (EffectName == "Pedals")
            {
                RenderPedals(dc);
                return;
            }

            int side = Math.Max(1, ReadInt("SideLeds", 5));
            int centre = Math.Max(2, ReadInt("CentreLeds", 16));
            bool[] litSide = new bool[side];          // by side slot, 0 = outermost
            bool[] litCentre = new bool[centre];
            Color brake;
            bool showLeft = true;
            bool showRight = true;
            if (!string.IsNullOrEmpty(EffectName))
            {
                int first = Math.Max(1, Math.Min(side, ReadInt("Fx" + EffectName + "Start", 1))) - 1;
                int length = Math.Max(1, Math.Min(side - first, ReadInt("Fx" + EffectName + "Count", 1)));
                for (int i = first; i < first + length; i++)
                {
                    litSide[i] = true;
                }

                showLeft = EffectName != "ErsCharge" && EffectName != "ErsDeploy" && EffectName != "P2p";
                showRight = EffectName != "ErsLapLimit" && EffectName != "ErsMode";
                string key = EffectName == "Flags" ? "FxFlagYellowColour" : "Fx" + EffectName + "Colour";
                string fallback = EffectName == "ErsCharge" || EffectName == "ErsLapLimit" ? "#008000" : "#FFFFFF";
                brake = ParseColour(ReadText(key, fallback), Colors.White);
                if (EffectName == "Flags" && ReadText("LedFunction", "rpmBrake") == "flags" && ReadBool("FxFlagsCentre", true))
                {
                    for (int i = 0; i < centre; i++)
                    {
                        litCentre[i] = true;                // with the function Flags the flag fills the top row as well
                    }
                }
            }
            else
            {
                string area = ReadText("BrakeArea", "both");
                brake = ParseColour(ReadText("BrakeColour", "#FF0000"), Colors.Red);
                showLeft = area != "right";
                showRight = area != "left";
                if (area == "top")
                {
                    for (int i = 0; i < centre; i++)
                    {
                        litCentre[i] = true;
                    }
                }
                else
                {
                    for (int i = 0; i < side; i++)
                    {
                        litSide[i] = true;
                    }
                }
            }

            double pitch = LayoutRadius * 2 + LayoutGap;
            double leftX = LayoutRadius;
            double rowFirstX = leftX + pitch;                       // on a dash the side columns sit close to the row ends, one level lower
            double rightX = rowFirstX + (centre - 1) * pitch + pitch;
            Pen outline = new Pen(new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)), 1.2);
            outline.Freeze();
            Brush dim = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
            dim.Freeze();
            Brush lit = new SolidColorBrush(brake);

            for (int slot = 0; slot < side; slot++)
            {
                double y = LayoutRadius + (side - slot) * pitch;          // the outermost LED is at the bottom, the top LED is one level below the row
                foreach (double x in new[] { leftX, rightX })
                {
                    if (litSide[slot] && ((x == leftX && showLeft) || (x == rightX && showRight)))
                    {
                        dc.DrawEllipse(lit, null, new Point(x, y), LayoutRadius, LayoutRadius);
                    }
                    else
                    {
                        dc.DrawEllipse(OffBrush, null, new Point(x, y), LayoutRadius, LayoutRadius);
                    }
                }
            }

            double rowY = LayoutRadius;
            for (int i = 0; i < centre; i++)
            {
                Point point = new Point(rowFirstX + i * pitch, rowY);
                if (litCentre[i])
                {
                    dc.DrawEllipse(lit, null, point, LayoutRadius, LayoutRadius);
                }
                else
                {
                    dc.DrawEllipse(dim, null, point, LayoutRadius, LayoutRadius);
                }
            }
        }

        /// <summary>The dash with the three pedals at about 60 % of their travel, each in its area and colour.</summary>
        private void RenderPedals(DrawingContext dc)
        {
            int side = Math.Max(1, ReadInt("SideLeds", 5));
            int centre = Math.Max(2, ReadInt("CentreLeds", 16));
            Color?[] leftSide = new Color?[side];
            Color?[] rightSide = new Color?[side];
            Color?[] row = new Color?[centre];
            const double travel = 0.6;
            string[] areas = { ReadText("PedalClutch", "off"), ReadText("PedalThrottle", "halfRight"), ReadText("PedalBrake", "halfLeft") };
            Color[] colours =
            {
                ParseColour(ReadText("FxClutchColour", "#8A2BE2"), Colors.BlueViolet),
                ParseColour(ReadText("ThrottleColour", "#00FF00"), Colors.Lime),
                ParseColour(ReadText("BrakeColour", "#FF0000"), Colors.Red)
            };
            for (int pedal = 0; pedal < 3; pedal++)             // clutch, throttle, brake: later ones are painted over earlier ones
            {
                Color?[] target = new Color?[side];
                FillPedal(areas[pedal], travel, colours[pedal], target, row);
                for (int i = 0; i < side; i++)
                {
                    if (target[i] != null)
                    {
                        if (areas[pedal] != "sideRight")
                        {
                            leftSide[i] = target[i];
                        }

                        if (areas[pedal] != "sideLeft")
                        {
                            rightSide[i] = target[i];
                        }
                    }
                }
            }

            double pitch = LayoutRadius * 2 + LayoutGap;
            double leftX = LayoutRadius;
            double rowFirstX = leftX + pitch;
            double rightX = rowFirstX + (centre - 1) * pitch + pitch;
            Pen outline = new Pen(new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)), 1.2);
            outline.Freeze();
            Brush dim = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
            dim.Freeze();
            for (int slot = 0; slot < side; slot++)
            {
                double y = LayoutRadius + (side - slot) * pitch;
                DrawLed(dc, new Point(leftX, y), leftSide[slot], outline);
                DrawLed(dc, new Point(rightX, y), rightSide[slot], outline);
            }

            for (int i = 0; i < centre; i++)
            {
                Point point = new Point(rowFirstX + i * pitch, LayoutRadius);
                if (row[i] != null)
                {
                    dc.DrawEllipse(new SolidColorBrush(row[i].Value), null, point, LayoutRadius, LayoutRadius);
                }
                else
                {
                    dc.DrawEllipse(dim, null, point, LayoutRadius, LayoutRadius);
                }
            }
        }

        private static void DrawLed(DrawingContext dc, Point point, Color? colour, Pen outline)
        {
            if (colour != null)
            {
                dc.DrawEllipse(new SolidColorBrush(colour.Value), null, point, LayoutRadius, LayoutRadius);
            }
            else
            {
                dc.DrawEllipse(OffBrush, null, point, LayoutRadius, LayoutRadius);
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (Kind == "layout")
            {
                RenderLayout(dc);
                return;
            }

            int count = Math.Max(2, LedCount);
            double elapsed = (DateTime.UtcNow - Origin).TotalSeconds % CycleSeconds;
            double fill = Math.Min(1, elapsed / (CycleSeconds * FillShare));
            double afterFill = Math.Max(0, elapsed - CycleSeconds * FillShare);
            if (Still)
            {
                fill = 1;              // a still picture of the finished pattern, in its colours
                afterFill = 0;
            }


            Color[] state = new Color[count];
            bool[] on = new bool[count];
            string kind = Kind;

            if (kind == "brake")
            {
                Color brake = ParseColour(ReadText(ColourSetting, "#FF0000"), Colors.Red);
                List<int[]> steps = Steps(BrakePattern(Pattern), count, ReadInt("SideLeds", 5));
                Light(steps, LitSteps(steps.Count, fill), step => brake, state, on);
            }
            else
            {
                List<Color> colours = ReadThemeColours();
                bool gradient = (ModeOverride ?? ReadText(ModeSetting, "blocks")) == "gradient";
                List<int[]> steps = Steps(kind == "redline" ? "leftRight" : Pattern, count, ReadInt("SideLeds", 5));
                double shown = fill;
                if (kind == "redline" && Pattern == "solid")
                {
                    shown = fill < 0.75 ? fill / 0.75 * 0.7 : 1;              // the bar jumps to full at the redline
                }

                Light(steps, LitSteps(steps.Count, shown), step => Sample(colours, steps.Count > 1 ? (double)step / (steps.Count - 1) : 0, gradient), state, on);

                if (kind == "redline" && Pattern == "flash" && fill >= 1)
                {
                    bool flashOn = ((int)Math.Floor(afterFill / 0.1)) % 2 == 0;      // flashes in the flash colour
                    Color flash = ParseColour(ReadText(FlashSetting, "#0000FF"), Colors.Blue);
                    for (int i = 0; i < count; i++)
                    {
                        on[i] = flashOn;
                        state[i] = flash;
                    }
                }

                if (kind == "redline" && Pattern == "flashSwap" && fill >= 1)
                {
                    bool swap = ((int)Math.Floor(afterFill / 0.1)) % 2 == 0;           // the whole bar in the first colour, then in the second
                    Color first = ParseColour(ReadText(FlashSetting, "#0000FF"), Colors.Blue);
                    Color second = ParseColour(ReadText(Flash2Setting, "#FF0000"), Colors.Red);
                    for (int i = 0; i < count; i++)
                    {
                        on[i] = true;
                        state[i] = swap ? first : second;
                    }
                }

                if (kind == "redline" && Pattern == "flashAlt" && fill >= 1)
                {
                    bool swap = ((int)Math.Floor(afterFill / 0.1)) % 2 == 0;           // every second LED in the other colour, swapping
                    Color first = ParseColour(ReadText(FlashSetting, "#0000FF"), Colors.Blue);
                    Color second = ParseColour(ReadText(Flash2Setting, "#FF0000"), Colors.Red);
                    for (int i = 0; i < count; i++)
                    {
                        on[i] = true;
                        state[i] = (i % 2 == 0) == swap ? first : second;
                    }
                }
            }

            if (!Animate && !Still)
            {
                // a tile that is not chosen shows its LEDs switched off
                for (int i = 0; i < count; i++)
                {
                    on[i] = false;
                }
            }

            Pen outline = new Pen(new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)), 1.4);
            outline.Freeze();
            double centreY = RowHeight / 2;
            for (int i = 0; i < count; i++)
            {
                Point centre = new Point(Radius + i * (Radius * 2 + Gap), centreY);
                if (on[i])
                {
                    dc.DrawEllipse(new SolidColorBrush(state[i]), null, centre, Radius, Radius);
                }
                else
                {
                    dc.DrawEllipse(OffBrush, null, centre, Radius, Radius);          // a switched-off LED: dark fill, no outline
                }
            }
        }

        private static int LitSteps(int stepCount, double fill)
        {
            return Math.Min(stepCount, (int)Math.Ceiling(fill * stepCount - 0.0001));
        }

        private static void Light(List<int[]> steps, int lit, Func<int, Color> colourOf, Color[] state, bool[] on)
        {
            for (int k = 0; k < lit && k < steps.Count; k++)
            {
                Color colour = colourOf(k);
                foreach (int led in steps[k])
                {
                    state[led] = colour;
                    on[led] = true;
                }
            }
        }
    }
}
