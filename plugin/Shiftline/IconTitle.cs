using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Shiftline.Plugin
{
    /// <summary>A card heading with a small icon in front: IconKey names a geometry "Icon&lt;IconKey&gt;" of the style dictionary.</summary>
    public class IconTitle : StackPanel
    {
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
            "Text", typeof(string), typeof(IconTitle), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty IconKeyProperty = DependencyProperty.Register(
            "IconKey", typeof(string), typeof(IconTitle), new PropertyMetadata(null));

        public IconTitle()
        {
            Orientation = Orientation.Horizontal;
            Loaded += (sender, args) => Build();
        }

        public string Text
        {
            get { return (string)GetValue(TextProperty); }
            set { SetValue(TextProperty, value); }
        }

        public string IconKey
        {
            get { return (string)GetValue(IconKeyProperty); }
            set { SetValue(IconKeyProperty, value); }
        }

        private void Build()
        {
            if (Children.Count > 0)
            {
                return;
            }

            Geometry geometry = TryFindResource("Icon" + IconKey) as Geometry;
            Brush stroke = TryFindResource("TextMuted") as Brush ?? Brushes.Gray;
            if (geometry != null)
            {
                Canvas canvas = new Canvas { Width = 24, Height = 24 };
                canvas.Children.Add(new Path
                {
                    Data = geometry,
                    Stroke = stroke,
                    StrokeThickness = 2,
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                });
                Children.Add(new Viewbox { Width = 18, Height = 18, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, Child = canvas });
            }

            TextBlock text = new TextBlock { Text = Text, VerticalAlignment = VerticalAlignment.Center };
            Style style = TryFindResource("CardTitle") as Style;
            if (style != null)
            {
                text.Style = style;
            }

            Children.Add(text);
        }
    }
}
