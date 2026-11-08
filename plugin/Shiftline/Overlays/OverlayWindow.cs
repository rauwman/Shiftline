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
        private readonly Thumb _sideGrip;
        private IntPtr _hwnd;
        private bool _edit;
        private bool _placed;
        private double _aspect;
        private bool _fixedWidth;
        private Point _dragStartMouse;
        private Size _dragStartSize;

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
            // The grip moves with the window while it is dragged, so its own deltas feed back into each other and the window
            // flickers. Instead the size is computed from where the mouse is on screen, against where the drag started.
            _grip.DragStarted += (sender, args) =>
            {
                _dragStartMouse = MouseOnScreen();
                _dragStartSize = new Size(Width, Height);
            };
            _grip.DragDelta += (sender, args) =>
            {
                Point mouse = MouseOnScreen();
                if (_fixedWidth)
                {
                    // Only the length changes: the owner turns the height into whole rows and sets the size itself.
                    double pulled = Math.Max(MinSize, _dragStartSize.Height + (mouse.Y - _dragStartMouse.Y));
                    Action<double> dragging = HeightDragging;
                    if (dragging != null)
                    {
                        dragging(pulled);
                    }

                    return;
                }

                double width = _dragStartSize.Width + (mouse.X - _dragStartMouse.X);
                double height = _dragStartSize.Height + (mouse.Y - _dragStartMouse.Y);
                if (_aspect > 0)
                {
                    // The content keeps its proportions, so the window does too: the larger of both moves decides the size.
                    width = Math.Max(width, height * _aspect);
                    height = width / _aspect;
                }

                width = Math.Max(MinSize, width);
                height = Math.Max(MinSize, height);
                if (Math.Abs(width - Width) >= 1 || Math.Abs(height - Height) >= 1)
                {
                    Width = width;
                    Height = height;
                }
            };
            _grip.DragCompleted += (sender, args) =>
            {
                if (_fixedWidth)
                {
                    Action<double> dragged = HeightDragged;
                    if (dragged != null)
                    {
                        dragged(Height);
                    }
                }

                SavePlacement();
            };

            // In fixed-size mode the right edge changes the size of the content (the owner turns the width into a scale).
            _sideGrip = new Thumb
            {
                Width = 8,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Stretch,
                Cursor = Cursors.SizeWE,
                Background = FrameBrush,
                Opacity = 0.8,
                Visibility = Visibility.Collapsed
            };
            _sideGrip.DragStarted += (sender, args) =>
            {
                _dragStartMouse = MouseOnScreen();
                _dragStartSize = new Size(Width, Height);
            };
            _sideGrip.DragDelta += (sender, args) =>
            {
                double pulled = Math.Max(MinSize, _dragStartSize.Width + (MouseOnScreen().X - _dragStartMouse.X));
                Action<double> dragging = WidthDragging;
                if (dragging != null)
                {
                    dragging(pulled / _dragStartSize.Width);
                }
            };
            _sideGrip.DragCompleted += (sender, args) =>
            {
                Action dragged = WidthDragged;
                if (dragged != null)
                {
                    dragged();
                }
            };

            Grid root = new Grid { Background = Brushes.Transparent };
            root.Children.Add(content);
            root.Children.Add(_frame);
            root.Children.Add(_label);
            root.Children.Add(_grip);
            root.Children.Add(_sideGrip);
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

        /// <summary>While the bottom edge is pulled in fixed-width mode: the new window height.</summary>
        public event Action<double> HeightDragging;

        /// <summary>After the bottom edge was released in fixed-width mode: the final window height.</summary>
        public event Action<double> HeightDragged;

        /// <summary>While the right edge is pulled in fixed-size mode: the new width as a factor of the width at the start.</summary>
        public event Action<double> WidthDragging;

        /// <summary>After the right edge was released in fixed-size mode.</summary>
        public event Action WidthDragged;

        /// <summary>
        /// Fixed size set by the owner. The width never changes by hand; the bottom edge can be pulled to make the overlay longer
        /// or shorter, and the owner answers with a new size.
        /// </summary>
        public void SetFixedSize(Size size)
        {
            if (!_fixedWidth)
            {
                _fixedWidth = true;
                _grip.Width = double.NaN;
                _grip.Height = 8;
                _grip.HorizontalAlignment = HorizontalAlignment.Stretch;
                _grip.Cursor = Cursors.SizeNS;
                _grip.Opacity = 0.8;
                _grip.Margin = new Thickness(0, 0, 8, 0);
                _sideGrip.Visibility = _edit ? Visibility.Visible : Visibility.Collapsed;
            }

            Width = size.Width;
            Height = size.Height;
        }

        public string Key
        {
            get { return _key; }
        }

        /// <summary>
        /// Width divided by height of the content. The window keeps this shape when it is resized, so the content always fills it.
        /// Changing it keeps the current width and adapts the height.
        /// </summary>
        public void SetAspect(double aspect)
        {
            if (aspect <= 0 || Math.Abs(aspect - _aspect) < 0.0001)
            {
                return;
            }

            _aspect = aspect;
            Height = Math.Max(MinSize, Width / aspect);
        }

        public void SetEditMode(bool edit)
        {
            _edit = edit;
            Visibility shown = edit ? Visibility.Visible : Visibility.Collapsed;
            _frame.Visibility = shown;
            _label.Visibility = shown;
            _grip.Visibility = shown;
            _sideGrip.Visibility = edit && _fixedWidth ? Visibility.Visible : Visibility.Collapsed;
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

        /// <summary>The mouse position on screen in device-independent units.</summary>
        private Point MouseOnScreen()
        {
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            System.Drawing.Point pixels = Forms.Cursor.Position;
            return new Point(pixels.X / dpi.DpiScaleX, pixels.Y / dpi.DpiScaleY);
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
                if (!_fixedWidth)
                {
                    Width = _defaultSize.Width;
                    Height = _aspect > 0 ? _defaultSize.Width / _aspect : _defaultSize.Height;
                }

                Left = area.Left + area.Width * 0.75;
                Top = area.Top + area.Height * 0.15;
                return;
            }

            if (!_fixedWidth)
            {
                Width = Math.Max(MinSize, placement.W * area.Width);
                Height = _aspect > 0 ? Math.Max(MinSize, Width / _aspect) : Math.Max(MinSize, placement.H * area.Height);
            }

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
