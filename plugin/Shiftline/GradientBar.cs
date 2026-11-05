using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Shiftline.Plugin
{
    /// <summary>
    /// A rounded bar that shows the colours of a theme as a gradient and follows edits of the theme live.
    /// </summary>
    public class GradientBar : Border
    {
        public static readonly DependencyProperty ThemeProperty = DependencyProperty.Register(
            "Theme", typeof(ColourTheme), typeof(GradientBar), new PropertyMetadata(null, OnThemeChanged));

        private readonly List<ColourStop> _watched = new List<ColourStop>();
        private ColourTheme _theme;

        public ColourTheme Theme
        {
            get { return (ColourTheme)GetValue(ThemeProperty); }
            set { SetValue(ThemeProperty, value); }
        }

        private static void OnThemeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((GradientBar)d).Attach(e.NewValue as ColourTheme);
        }

        private void Attach(ColourTheme theme)
        {
            if (_theme != null)
            {
                _theme.Stops.CollectionChanged -= OnStopsChanged;
            }

            _theme = theme;
            if (_theme != null)
            {
                _theme.Stops.CollectionChanged += OnStopsChanged;
            }

            Rebuild();
        }

        private void OnStopsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            Rebuild();
        }

        private void OnStopPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            Rebuild();
        }

        private void Rebuild()
        {
            foreach (ColourStop stop in _watched)
            {
                stop.PropertyChanged -= OnStopPropertyChanged;
            }

            _watched.Clear();

            if (_theme == null || _theme.Stops.Count == 0)
            {
                Background = Brushes.Transparent;
                return;
            }

            LinearGradientBrush brush = new LinearGradientBrush();
            brush.StartPoint = new Point(0, 0.5);
            brush.EndPoint = new Point(1, 0.5);
            int count = _theme.Stops.Count;
            for (int i = 0; i < count; i++)
            {
                ColourStop stop = _theme.Stops[i];
                stop.PropertyChanged += OnStopPropertyChanged;
                _watched.Add(stop);
                Color colour = (Color)ColorConverter.ConvertFromString(stop.Hex);
                brush.GradientStops.Add(new GradientStop(colour, count == 1 ? 0 : (double)i / (count - 1)));
            }

            Background = brush;
        }
    }
}
