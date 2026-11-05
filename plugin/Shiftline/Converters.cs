using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Data;

namespace Shiftline.Plugin
{
    /// <summary>
    /// Tells the LED page which settings tabs apply to the chosen LED function: parameter "rpm", "brake", "pedals" or "fuel".
    /// Other tabs (layout, effects, brightness, idle) are always available.
    /// </summary>
    public class FunctionTabConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string function = value as string;
            switch (parameter as string)
            {
                case "rpm":
                    return function == "rpmBrake" || function == "rpmOnly";
                case "brake":
                    return function == "rpmBrake" || function == "brakeOnly";
                case "pedals":
                    return function == "brakeThrottle";
                case "fuel":
                    return function == "fuel";
                default:
                    return true;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    /// <summary>True while a text setting does NOT equal the converter parameter (for switching controls off).</summary>
    public class StringNotEqualsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !string.Equals(value as string, parameter as string, StringComparison.Ordinal);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    /// <summary>
    /// Lets a RadioButton select one text value of a setting: IsChecked is true while the setting equals the
    /// converter parameter, and checking the button writes the parameter back to the setting.
    /// </summary>
    public class StringEqualsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.Equals(value as string, parameter as string, StringComparison.Ordinal);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool && (bool)value ? parameter : Binding.DoNothing;
        }
    }

    /// <summary>"n of m on" for a group header: counts the true values among the bound switches.</summary>
    public class OnCountConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            int on = values.Count(v => v is bool && (bool)v);
            return on + " of " + values.Length + " on";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            return new object[targetTypes.Length];
        }
    }

    /// <summary>Visible while a switch is OFF (the opposite of BooleanToVisibilityConverter).</summary>
    public class InverseBoolVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool && (bool)value ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    /// <summary>Turns a colour setting ("#RRGGBB" or "#AARRGGBB") into a brush for small colour swatches.</summary>
    public class ColourBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                string text = value as string;
                if (!string.IsNullOrEmpty(text))
                {
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString(text));
                }
            }
            catch (FormatException)
            {
            }

            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
