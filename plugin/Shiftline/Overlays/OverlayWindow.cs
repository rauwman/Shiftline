using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace Shiftline.Plugin
{
    /// <summary>
    /// A borderless, transparent window that floats above the game. In run mode it lets every mouse click through and never takes
    /// the focus. In edit mode it shows a frame and its name, can be dragged, and resized at the corner. Its position is saved as
    /// parts of the screen it is on, so it lands in the same spot when the resolution changes.
    /// </summary>
    internal sealed class OverlayWindow : Window
    {
        private const double MinSize = 60;

        private static readonly Brush FrameBrush = Frozen(Color.FromArgb(0xFF, 0x3F, 0xA9, 0xF5));
        private static readonly Brush LabelBackground = Frozen(Color.FromArgb(0xE0, 0x3F, 0xA9, 0xF5));

        private readonly string _key;
        private readonly Size _defaultSize;
        private readonly Border _frame;
        private readonly Border _label;
        private readonly Thumb _grip;
        private IntPtr _hwnd;
        private bool _edit;
        private bool _placed;

        public OverlayWindow(string key, string title, UIElement content, Size defaultSize)
        {
            _key = key;
            _defaultSize = defaultSize;

            Title = "Shiftline " + title;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            Width = defaultSize.Width;
            Height = defaultSize.Height;

            _frame = new Border { BorderBrush = FrameBrush, BorderThickness = new Thickness(2), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            _label = new Border
            {
                Background = LabelBackground,
                Padding = new Thickness(6, 2, 6, 2),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
                Child = new TextBlock { Text = title, Foreground = Brushes.White, FontSize = 12 }
            };
            _grip = new Thumb
            {
                Width = 16,
                Height = 16,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Cursor = Cursors.SizeNWSE,
                Background = FrameBrush,
                Visibility = Visibility.Collapsed
            };
            _grip.DragDelta += (sender, args) =>
            {
                Width = Math.Max(MinSize, Width + args.HorizontalChange);
                Height = Math.Max(MinSize, Height + args.VerticalChange);
            };
            _grip.DragCompleted += (sender, args) => SavePlacement();

            Grid root = new Grid { Background = Brushes.Transparent };
            root.Children.Add(content);
            root.Children.Add(_frame);
            root.Children.Add(_label);
            root.Children.Add(_grip);
            Content = root;

            MouseLeftButtonDown += (sender, args) =>
            {
                if (!_edit)
                {
                    return;
                }

                try
                {
                    DragMove();
                }
                catch (InvalidOperationException)
                {
                    return;                 // the button was already released
                }

                SavePlacement();
            };

            SourceInitialized += (sender, args) =>
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                ApplyStyles();
            };

            // The DPI is known only once the window exists, so the saved position is applied when it is shown for the first time.
            ContentRendered += (sender, args) =>
            {
                if (!_placed)
                {
                    _placed = true;
                    RestorePlacement();
                }
            };
        }

        public string Key
        {
            get { return _key; }
        }

        public void SetEditMode(bool edit)
        {
            _edit = edit;
            Visibility shown = edit ? Visibility.Visible : Visibility.Collapsed;
            _frame.Visibility = shown;
            _label.Visibility = shown;
            _grip.Visibility = shown;
            ApplyStyles();
        }

        private void ApplyStyles()
        {
            if (_hwnd == IntPtr.Zero)
            {
                return;
            }

            int style = NativeMethods.GetExStyle(_hwnd) | NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate;
            style = _edit ? style & ~NativeMethods.WsExTransparent : style | NativeMethods.WsExTransparent;
            NativeMethods.SetExStyle(_hwnd, style);
        }

        /// <summary>The screen area in device-independent units, as WPF measures Left, Top, Width and Height.</summary>
        private Rect ScreenRect(Forms.Screen screen)
        {
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            return new Rect(screen.Bounds.Left / dpi.DpiScaleX, screen.Bounds.Top / dpi.DpiScaleY,
                            screen.Bounds.Width / dpi.DpiScaleX, screen.Bounds.Height / dpi.DpiScaleY);
        }

        /// <summary>Moves the window to its saved place, or to the default place on the main screen when it has none.</summary>
        public void RestorePlacement()
        {
            OverlayPlacement placement = OverlaySettings.GetPlacement(_key);
            Forms.Screen screen = placement == null ? null : Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == placement.Screen);
            if (screen == null)
            {
                screen = Forms.Screen.PrimaryScreen;
                placement = null;           // the saved screen is gone: start again on the main screen
            }

            Rect area = ScreenRect(screen);
            if (placement == null || placement.W <= 0 || placement.H <= 0)
            {
                Width = _defaultSize.Width;
                Height = _defaultSize.Height;
                Left = area.Left + area.Width * 0.75;
                Top = area.Top + area.Height * 0.15;
                return;
            }

            Width = Math.Max(MinSize, placement.W * area.Width);
            Height = Math.Max(MinSize, placement.H * area.Height);
            Left = area.Left + Clamp01(placement.X) * area.Width;
            Top = area.Top + Clamp01(placement.Y) * area.Height;
        }

        private void SavePlacement()
        {
            if (_hwnd == IntPtr.Zero)
            {
                return;
            }

            Forms.Screen screen = Forms.Screen.FromHandle(_hwnd);
            Rect area = ScreenRect(screen);
            OverlaySettings.SetPlacement(_key, new OverlayPlacement
            {
                Screen = screen.DeviceName,
                X = (Left - area.Left) / area.Width,
                Y = (Top - area.Top) / area.Height,
                W = Width / area.Width,
                H = Height / area.Height
            });
        }

        private static double Clamp01(double value)
        {
            return Math.Max(0, Math.Min(0.98, value));
        }

        private static Brush Frozen(Color color)
        {
            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
