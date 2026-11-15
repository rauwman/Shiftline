using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Shiftline.Plugin
{
    /// <summary>
    /// The thin guide lines of edit mode: while an overlay is dragged and snaps to an edge, a centre or another overlay, a line
    /// across its screen shows where. One click-through window over the whole screen, created when it is first needed.
    /// </summary>
    internal static class OverlayGuides
    {
        private static readonly Brush LineBrush = Frozen(Color.FromArgb(0xD0, 0x3F, 0xA9, 0xF5));
        private static Window _window;
        private static Line _across;
        private static Line _down;

        public static void Show(OverlayWindow owner, double? x, double? y)
        {
            if (x == null && y == null)
            {
                Hide();
                return;
            }

            Rect screen = owner.CurrentScreen();
            if (screen.IsEmpty)
            {
                return;
            }

            if (_window == null)
            {
                Create();
            }

            _window.Left = screen.Left;
            _window.Top = screen.Top;
            _window.Width = screen.Width;
            _window.Height = screen.Height;
            Place(_down, x.HasValue, x.GetValueOrDefault() - screen.Left, 0, x.GetValueOrDefault() - screen.Left, screen.Height);
            Place(_across, y.HasValue, 0, y.GetValueOrDefault() - screen.Top, screen.Width, y.GetValueOrDefault() - screen.Top);
            if (!_window.IsVisible)
            {
                _window.Show();
            }
        }

        public static void Hide()
        {
            if (_window != null && _window.IsVisible)
            {
                _window.Hide();
            }
        }

        private static void Place(Line line, bool shown, double x1, double y1, double x2, double y2)
        {
            line.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            line.X1 = x1;
            line.Y1 = y1;
            line.X2 = x2;
            line.Y2 = y2;
        }

        private static void Create()
        {
            _across = new Line { Stroke = LineBrush, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 } };
            _down = new Line { Stroke = LineBrush, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 } };
            Canvas canvas = new Canvas { IsHitTestVisible = false };
            canvas.Children.Add(_across);
            canvas.Children.Add(_down);
            _window = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                IsHitTestVisible = false,
                Content = canvas
            };
            _window.SourceInitialized += (sender, args) =>
            {
                // click-through and never active, like the overlays outside edit mode
                IntPtr hwnd = new WindowInteropHelper(_window).Handle;
                NativeMethods.SetExStyle(hwnd, NativeMethods.GetExStyle(hwnd) | NativeMethods.WsExTransparent | NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate);
            };
        }

        private static Brush Frozen(Color color)
        {
            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
