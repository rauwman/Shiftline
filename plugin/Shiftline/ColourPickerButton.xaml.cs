using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Shiftline.Plugin
{
    /// <summary>
    /// A colour swatch that opens a colour picker: a field to drag a dot through (saturation and brightness), a hue bar and
    /// a row of quick colours, an opacity slider and a hex field. The Colour property holds "#RRGGBB" and binds two-way. LEDs cannot be
    /// see-through, so the opacity dims the colour towards black; the stored colour is the dimmed result.
    /// </summary>
    public partial class ColourPickerButton : UserControl
    {
        public static readonly DependencyProperty ColourProperty = DependencyProperty.Register(
            "Colour", typeof(string), typeof(ColourPickerButton),
            new FrameworkPropertyMetadata("#FFFFFF", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnColourChanged));

        private const double FieldWidth = 236;
        private const double FieldHeight = 130;
        private const double HueWidth = 236;

        private static readonly string[] PaletteColours =
        {
            "#FF0000", "#FF4500", "#FF8C00", "#FFB300", "#FFFF00", "#B8E600",
            "#00FF00", "#00C060", "#00FFD0", "#00BFFF", "#1E90FF", "#0000FF",
            "#4B0082", "#8A2BE2", "#FF00FF", "#FF1493", "#FF6EB4", "#A0522D",
            "#FFFFFF", "#D0D0D0", "#909090", "#505050", "#FFD700", "#40E0D0"
        };

        private double _hue;                 // 0..360
        private double _saturation;          // 0..1
        private double _value = 1;           // 0..1
        private double _opacity = 1;         // 0..1, dims the colour towards black
        private bool _updatingOpacity;
        private string _produced;            // the colour this control wrote itself, so it does not recompute the dot from it

        public ColourPickerButton()
        {
            InitializeComponent();
            BuildPalette();
            Refresh();
        }

        public string Colour
        {
            get { return (string)GetValue(ColourProperty); }
            set { SetValue(ColourProperty, value); }
        }

        private static void OnColourChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((ColourPickerButton)d).Refresh();
        }

        // ---------------------------------------------------------------- quick colours

        private void BuildPalette()
        {
            foreach (string hex in PaletteColours)
            {
                Button chip = new Button();
                chip.Width = 30;
                chip.Height = 22;
                chip.Margin = new Thickness(2);
                chip.Padding = new Thickness(0);
                chip.Cursor = Cursors.Hand;
                chip.Background = new SolidColorBrush(ParseColour(hex));
                chip.BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
                chip.Tag = hex;
                chip.Click += Chip_Click;
                Palette.Children.Add(chip);
            }
        }

        private void Chip_Click(object sender, RoutedEventArgs e)
        {
            _produced = null;                                  // a quick colour: place the dot from the colour
            SetCurrentValue(ColourProperty, (string)((Button)sender).Tag);
        }

        // ---------------------------------------------------------------- the colour field and the hue bar

        private void Field_MouseDown(object sender, MouseButtonEventArgs e)
        {
            FieldArea.CaptureMouse();
            PickFromField(e.GetPosition(FieldArea));
        }

        private void Field_MouseMove(object sender, MouseEventArgs e)
        {
            if (FieldArea.IsMouseCaptured)
            {
                PickFromField(e.GetPosition(FieldArea));
            }
        }

        private void Field_MouseUp(object sender, MouseButtonEventArgs e)
        {
            FieldArea.ReleaseMouseCapture();
        }

        private void Hue_MouseDown(object sender, MouseButtonEventArgs e)
        {
            HueArea.CaptureMouse();
            PickFromHue(e.GetPosition(HueArea));
        }

        private void Hue_MouseMove(object sender, MouseEventArgs e)
        {
            if (HueArea.IsMouseCaptured)
            {
                PickFromHue(e.GetPosition(HueArea));
            }
        }

        private void Hue_MouseUp(object sender, MouseButtonEventArgs e)
        {
            HueArea.ReleaseMouseCapture();
        }

        private void PickFromField(Point position)
        {
            _saturation = Clamp(position.X / FieldWidth);
            _value = 1 - Clamp(position.Y / FieldHeight);
            Write();
        }

        private void PickFromHue(Point position)
        {
            _hue = Clamp(position.X / HueWidth) * 360;
            Write();
        }

        private void Write()
        {
            Color colour = FromHsv(_hue, _saturation, _value);
            colour = Color.FromRgb((byte)Math.Round(colour.R * _opacity), (byte)Math.Round(colour.G * _opacity), (byte)Math.Round(colour.B * _opacity));
            _produced = string.Format("#{0:X2}{1:X2}{2:X2}", colour.R, colour.G, colour.B);
            SetCurrentValue(ColourProperty, _produced);
            Refresh();
        }

        // ---------------------------------------------------------------- opacity and hex code

        private void SetOpacitySlider(double percent)
        {
            _updatingOpacity = true;
            OpacitySlider.Value = percent;
            OpacityText.Text = Math.Round(percent) + " %";
            _updatingOpacity = false;
        }

        private void Opacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingOpacity || OpacityText == null || HexBox == null)
            {
                return;
            }

            _opacity = e.NewValue / 100;
            OpacityText.Text = Math.Round(e.NewValue) + " %";
            Write();
        }

        private void HexBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CommitHex();
            }
        }

        private void HexBox_LostFocus(object sender, RoutedEventArgs e)
        {
            CommitHex();
        }

        /// <summary>Takes the typed hex code when it is a valid colour (#RRGGBB, with or without the #); otherwise shows the current colour again.</summary>
        private void CommitHex()
        {
            string text = (HexBox.Text ?? string.Empty).Trim();
            if (!text.StartsWith("#"))
            {
                text = "#" + text;
            }

            if (Regex.IsMatch(text, "^#[0-9A-Fa-f]{6}$"))
            {
                _produced = null;
                SetCurrentValue(ColourProperty, text.ToUpperInvariant());
            }

            HexBox.Text = Colour;
        }

        // ---------------------------------------------------------------- showing the colour

        private void Refresh()
        {
            if (Swatch == null)
            {
                return;
            }

            Color colour = ParseColour(Colour);
            if (!string.Equals(Colour, _produced, StringComparison.OrdinalIgnoreCase))
            {
                ToHsv(colour, ref _hue, out _saturation, out _value);
                _opacity = 1;                                  // a colour that came from elsewhere is a full colour
                SetOpacitySlider(100);
            }

            Swatch.Background = new SolidColorBrush(colour);
            if (!HexBox.IsKeyboardFocusWithin)
            {
                HexBox.Text = Colour;
            }

            LinearGradientBrush field = new LinearGradientBrush();
            field.StartPoint = new Point(0, 0);
            field.EndPoint = new Point(1, 0);
            field.GradientStops.Add(new GradientStop(Colors.White, 0));
            field.GradientStops.Add(new GradientStop(FromHsv(_hue, 1, 1), 1));
            FieldHue.Fill = field;

            Canvas.SetLeft(FieldMarker, _saturation * FieldWidth - FieldMarker.Width / 2);
            Canvas.SetTop(FieldMarker, (1 - _value) * FieldHeight - FieldMarker.Height / 2);
            Canvas.SetLeft(HueMarker, _hue / 360 * HueWidth - HueMarker.Width / 2);
        }

        // ---------------------------------------------------------------- colour maths

        private static double Clamp(double value)
        {
            return Math.Max(0, Math.Min(1, value));
        }

        private static Color ParseColour(string hex)
        {
            try
            {
                return (Color)ColorConverter.ConvertFromString(hex);
            }
            catch (Exception)
            {
                return Colors.White;
            }
        }

        private static Color FromHsv(double hue, double saturation, double value)
        {
            double h = (hue % 360) / 60;
            int sector = (int)Math.Floor(h);
            double fraction = h - sector;
            double p = value * (1 - saturation);
            double q = value * (1 - saturation * fraction);
            double t = value * (1 - saturation * (1 - fraction));
            double r;
            double g;
            double b;
            switch (sector)
            {
                case 0:
                    r = value; g = t; b = p;
                    break;
                case 1:
                    r = q; g = value; b = p;
                    break;
                case 2:
                    r = p; g = value; b = t;
                    break;
                case 3:
                    r = p; g = q; b = value;
                    break;
                case 4:
                    r = t; g = p; b = value;
                    break;
                default:
                    r = value; g = p; b = q;
                    break;
            }

            return Color.FromRgb((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
        }

        /// <summary>Reads hue, saturation and value from a colour. The hue stays as it was for greys and black, where it is undefined.</summary>
        private static void ToHsv(Color colour, ref double hue, out double saturation, out double value)
        {
            double r = colour.R / 255.0;
            double g = colour.G / 255.0;
            double b = colour.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;
            value = max;
            saturation = max <= 0 ? 0 : delta / max;
            if (delta > 0)
            {
                double h;
                if (max == r)
                {
                    h = ((g - b) / delta) % 6;
                }
                else if (max == g)
                {
                    h = (b - r) / delta + 2;
                }
                else
                {
                    h = (r - g) / delta + 4;
                }

                hue = h * 60;
                if (hue < 0)
                {
                    hue += 360;
                }
            }
        }
    }
}
