using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Shiftline.Plugin
{
    /// <summary>
    /// The small bar above an overlay in edit mode with six align buttons, drawn like the align tools of design programs: a line
    /// for the edge or the centre and two bars that line up on it. Its own little window, so it never covers the overlay.
    /// </summary>
    internal sealed class OverlayAlignBar : Window
    {
        public const double BarWidth = 6 * 40 + 11 + 14;
        public const double BarHeight = 42;

        private static readonly Brush BarBrush = Frozen(Color.FromArgb(0xF0, 0x16, 0x1C, 0x26));
        private static readonly Brush EdgeBrush = Frozen(Color.FromArgb(0xFF, 0x3F, 0xA9, 0xF5));
        private static readonly Brush HoverBrush = Frozen(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
        private static readonly Brush ShapeBrush = Frozen(Color.FromArgb(0xFF, 0xE8, 0xEC, 0xF1));

        // 16 x 16: the line the overlay lines up on (accent colour) and two bars that sit on it
        private const string LeftLine = "M 1.5,1 L 1.5,15";
        private const string LeftBars = "M 4,3 H 14 V 6 H 4 Z M 4,10 H 10 V 13 H 4 Z";
        private const string CentreXLine = "M 8,1 L 8,15";
        private const string CentreXBars = "M 2.5,3 H 13.5 V 6 H 2.5 Z M 5,10 H 11 V 13 H 5 Z";
        private const string RightLine = "M 14.5,1 L 14.5,15";
        private const string RightBars = "M 2,3 H 12 V 6 H 2 Z M 6,10 H 12 V 13 H 6 Z";
        private const string TopLine = "M 1,1.5 L 15,1.5";
        private const string TopBars = "M 3,4 H 6 V 14 H 3 Z M 10,4 H 13 V 10 H 10 Z";
        private const string CentreYLine = "M 1,8 L 15,8";
        private const string CentreYBars = "M 3,2.5 H 6 V 13.5 H 3 Z M 10,5 H 13 V 11 H 10 Z";
        private const string BottomLine = "M 1,14.5 L 15,14.5";
        private const string BottomBars = "M 3,2 H 6 V 12 H 3 Z M 10,6 H 13 V 12 H 10 Z";

        private readonly Action<string> _align;

        public OverlayAlignBar(Action<string> align)
        {
            _align = align;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            Width = BarWidth;
            Height = BarHeight;

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            buttons.Children.Add(AlignButton(LeftLine, LeftBars, "Align left", "Left"));
            buttons.Children.Add(AlignButton(CentreXLine, CentreXBars, "Centre horizontally", "CentreX"));
            buttons.Children.Add(AlignButton(RightLine, RightBars, "Align right", "Right"));
            buttons.Children.Add(new Border { Width = 1, Margin = new Thickness(5, 8, 5, 8), Background = ShapeBrush, Opacity = 0.3 });
            buttons.Children.Add(AlignButton(TopLine, TopBars, "Align top", "Top"));
            buttons.Children.Add(AlignButton(CentreYLine, CentreYBars, "Centre vertically", "CentreY"));
            buttons.Children.Add(AlignButton(BottomLine, BottomBars, "Align bottom", "Bottom"));
            Content = new Border
            {
                Background = BarBrush,
                BorderBrush = EdgeBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 0, 6, 0),
                Child = buttons
            };

            SourceInitialized += (sender, args) =>
            {
                // clickable, but never takes the focus and stays out of Alt+Tab
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                NativeMethods.SetExStyle(hwnd, NativeMethods.GetExStyle(hwnd) | NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate);
            };
        }

        private FrameworkElement AlignButton(string line, string bars, string tip, string where)
        {
            Grid icon = new Grid { Width = 16, Height = 16, LayoutTransform = new ScaleTransform(1.4, 1.4) };
            icon.Children.Add(new Path { Data = Geometry.Parse(bars), Fill = ShapeBrush });
            icon.Children.Add(new Path { Data = Geometry.Parse(line), Stroke = EdgeBrush, StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
            Border button = new Border
            {
                Width = 38,
                Height = 34,
                Margin = new Thickness(1, 0, 1, 0),
                CornerRadius = new CornerRadius(5),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = tip,
                Child = icon
            };
            button.MouseEnter += (sender, args) => button.Background = HoverBrush;
            button.MouseLeave += (sender, args) => button.Background = Brushes.Transparent;
            button.MouseLeftButtonUp += (sender, args) =>
            {
                _align(where);
                args.Handled = true;
            };
            return button;
        }

        private static Brush Frozen(Color color)
        {
            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
