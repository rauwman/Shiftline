using System;
using System.Collections.Generic;
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

        // edit mode: the selected overlay, moving with snapping, and the align bar
        private static readonly List<OverlayWindow> Open = new List<OverlayWindow>();
        private static OverlayWindow _selected;
        private const double SnapDistance = 8;
        private bool _moving;
        private Point _moveStartMouse;
        private Point _moveStartPosition;
        private OverlayAlignBar _alignBar;

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

            // Edit mode: a click selects the overlay (frame, align bar, arrow keys); dragging moves it and snaps it to the screen
            // edges and centre and to the edges of the other overlays. Alt held while dragging moves it freely.
            MouseLeftButtonDown += (sender, args) =>
            {
                if (!_edit)
                {
                    return;
                }

                Select(this);
                _moving = true;
                _moveStartMouse = MouseOnScreen();
                _moveStartPosition = new Point(Left, Top);
                CaptureMouse();
                args.Handled = true;
            };
            MouseMove += (sender, args) =>
            {
                if (!_moving)
                {
                    return;
                }

                Point mouse = MouseOnScreen();
                double left = _moveStartPosition.X + mouse.X - _moveStartMouse.X;
                double top = _moveStartPosition.Y + mouse.Y - _moveStartMouse.Y;
                bool free = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
                double? guideX = null;
                double? guideY = null;
                if (!free)
                {
                    Snap(ref left, ref top, out guideX, out guideY);
                }

                Left = left;
                Top = top;
                OverlayGuides.Show(this, guideX, guideY);
            };
            MouseLeftButtonUp += (sender, args) =>
            {
                if (!_moving)
                {
                    return;
                }

                _moving = false;
                ReleaseMouseCapture();
                OverlayGuides.Hide();
                SavePlacement();
            };
            KeyDown += (sender, args) =>
            {
                if (!_edit || !ReferenceEquals(_selected, this))
                {
                    return;
                }

                double step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 10 : 1;
                switch (args.Key)
                {
                    case System.Windows.Input.Key.Left:
                        Left -= step;
                        break;
                    case System.Windows.Input.Key.Right:
                        Left += step;
                        break;
                    case System.Windows.Input.Key.Up:
                        Top -= step;
                        break;
                    case System.Windows.Input.Key.Down:
                        Top += step;
                        break;
                    default:
                        return;
                }

                args.Handled = true;
                SavePlacement();
            };
            LocationChanged += (sender, args) => FollowBar();
            SizeChanged += (sender, args) => FollowBar();
            IsVisibleChanged += (sender, args) => UpdateBar();
            Open.Add(this);
            Closed += (sender, args) =>
            {
                Open.Remove(this);
                if (ReferenceEquals(_selected, this))
                {
                    _selected = null;
                }

                if (_alignBar != null)
                {
                    _alignBar.Close();
                    _alignBar = null;
                }
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
            UpdateBar();
            if (!edit && ReferenceEquals(_selected, this))
            {
                Select(null);
            }

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

            // In edit mode the window may take the keyboard (for the arrow keys); in run mode it never takes the focus from the game.
            int style = NativeMethods.GetExStyle(_hwnd) | NativeMethods.WsExToolWindow;
            style = _edit
                ? style & ~NativeMethods.WsExTransparent & ~NativeMethods.WsExNoActivate
                : style | NativeMethods.WsExTransparent | NativeMethods.WsExNoActivate;
            NativeMethods.SetExStyle(_hwnd, style);
        }

        // ---------------------------------------------------------------- selection, align bar, snapping

        /// <summary>Selects one overlay (or none): thicker frame, the align bar above it, and the keyboard for the arrow keys.</summary>
        private static void Select(OverlayWindow window)
        {
            if (ReferenceEquals(_selected, window))
            {
                return;
            }

            OverlayWindow before = _selected;
            _selected = window;
            if (before != null)
            {
                before._frame.BorderThickness = new Thickness(2);
            }

            if (window != null)
            {
                window._frame.BorderThickness = new Thickness(3);
                window.Activate();
                window.Focus();
            }
        }

        /// <summary>In edit mode every visible overlay has its align bar right away, above it (or below when there is no room).</summary>
        private void UpdateBar()
        {
            bool shown = _edit && IsVisible;
            if (shown && _alignBar == null)
            {
                _alignBar = new OverlayAlignBar(AlignTo);
            }

            if (_alignBar == null)
            {
                return;
            }

            if (shown)
            {
                FollowBar();
                if (!_alignBar.IsVisible)
                {
                    _alignBar.Show();
                }
            }
            else if (_alignBar.IsVisible)
            {
                _alignBar.Hide();
            }
        }

        private void FollowBar()
        {
            if (_alignBar == null || _hwnd == IntPtr.Zero)
            {
                return;
            }

            Rect area = ScreenRect(Forms.Screen.FromHandle(_hwnd));
            double top = Top - OverlayAlignBar.BarHeight - 4;
            if (top < area.Top)
            {
                top = Top + Height + 4;
            }

            double left = Math.Max(area.Left, Math.Min(Left, area.Right - OverlayAlignBar.BarWidth));
            _alignBar.Left = left;
            _alignBar.Top = top;
        }

        /// <summary>Puts the overlay against an edge (with the distance chosen on the General page) or in the middle of its screen.</summary>
        private void AlignTo(string where)
        {
            if (_hwnd == IntPtr.Zero)
            {
                return;
            }

            Rect area = ScreenRect(Forms.Screen.FromHandle(_hwnd));
            double margin = OverlaySettings.AlignMargin;
            switch (where)
            {
                case "Left":
                    Left = area.Left + margin;
                    break;
                case "CentreX":
                    Left = area.Left + (area.Width - Width) / 2;
                    break;
                case "Right":
                    Left = area.Right - Width - margin;
                    break;
                case "Top":
                    Top = area.Top + margin;
                    break;
                case "CentreY":
                    Top = area.Top + (area.Height - Height) / 2;
                    break;
                case "Bottom":
                    Top = area.Bottom - Height - margin;
                    break;
            }

            Left = Math.Round(Left);
            Top = Math.Round(Top);
            SavePlacement();
        }

        /// <summary>
        /// Pulls the new position to the nearest line within a few pixels, separately across and down: the screen edges (with
        /// the align distance), the screen centre, and the edges of the other visible overlays. Returns the lines it used.
        /// </summary>
        private void Snap(ref double left, ref double top, out double? guideX, out double? guideY)
        {
            guideX = null;
            guideY = null;
            if (_hwnd == IntPtr.Zero)
            {
                return;
            }

            Rect area = ScreenRect(Forms.Screen.FromHandle(_hwnd));
            double margin = OverlaySettings.AlignMargin;
            double width = Width;
            double height = Height;

            // lines across: (line, position the window gets when it sits on it)
            List<KeyValuePair<double, double>> xs = new List<KeyValuePair<double, double>>();
            List<KeyValuePair<double, double>> ys = new List<KeyValuePair<double, double>>();
            foreach (double edge in new[] { area.Left, area.Left + margin })
            {
                xs.Add(new KeyValuePair<double, double>(edge, edge));
            }

            foreach (double edge in new[] { area.Right, area.Right - margin })
            {
                xs.Add(new KeyValuePair<double, double>(edge, edge - width));
            }

            xs.Add(new KeyValuePair<double, double>(area.Left + area.Width / 2, area.Left + area.Width / 2 - width / 2));
            foreach (double edge in new[] { area.Top, area.Top + margin })
            {
                ys.Add(new KeyValuePair<double, double>(edge, edge));
            }

            foreach (double edge in new[] { area.Bottom, area.Bottom - margin })
            {
                ys.Add(new KeyValuePair<double, double>(edge, edge - height));
            }

            ys.Add(new KeyValuePair<double, double>(area.Top + area.Height / 2, area.Top + area.Height / 2 - height / 2));
            foreach (OverlayWindow other in Open)
            {
                if (ReferenceEquals(other, this) || !other.IsVisible)
                {
                    continue;
                }

                // my left or right edge on its left or right edge, my top or bottom on its top or bottom
                foreach (double edge in new[] { other.Left, other.Left + other.Width })
                {
                    xs.Add(new KeyValuePair<double, double>(edge, edge));
                    xs.Add(new KeyValuePair<double, double>(edge, edge - width));
                }

                foreach (double edge in new[] { other.Top, other.Top + other.Height })
                {
                    ys.Add(new KeyValuePair<double, double>(edge, edge));
                    ys.Add(new KeyValuePair<double, double>(edge, edge - height));
                }
            }

            double bestX = SnapDistance + 1;
            double l = left;
            foreach (KeyValuePair<double, double> line in xs)
            {
                double distance = Math.Abs(line.Value - l);
                if (distance <= SnapDistance && distance < bestX)
                {
                    bestX = distance;
                    left = line.Value;
                    guideX = line.Key;
                }
            }

            double bestY = SnapDistance + 1;
            double t = top;
            foreach (KeyValuePair<double, double> line in ys)
            {
                double distance = Math.Abs(line.Value - t);
                if (distance <= SnapDistance && distance < bestY)
                {
                    bestY = distance;
                    top = line.Value;
                    guideY = line.Key;
                }
            }
        }

        /// <summary>The screen area of this window in device-independent units (for the guide lines).</summary>
        internal Rect CurrentScreen()
        {
            return _hwnd == IntPtr.Zero ? Rect.Empty : ScreenRect(Forms.Screen.FromHandle(_hwnd));
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
