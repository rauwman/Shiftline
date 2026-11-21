using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json.Linq;
using Forms = System.Windows.Forms;

namespace Shiftline.Plugin
{
    /// <summary>
    /// A borderless, see-through window that floats above the game, drawn by the browser engine (WebView2). In run mode it lets
    /// every mouse click through and never takes the focus. In edit mode the page shows a frame, the name and the grips; the
    /// window can be dragged and resized. Its position is saved as
    /// parts of the screen it is on, so it lands in the same spot when the resolution changes.
    /// </summary>
    internal sealed class OverlayWindow : Window, IHtmlSurface
    {
        private const double MinSize = 60;


        private readonly string _key;
        private readonly Size _defaultSize;
        private IntPtr _hwnd;
        private bool _edit;
        private double _aspect;
        private bool _fixedWidth;
        private Point _dragStartMouse;
        private Size _dragStartSize;

        // edit mode: the selected overlay, moving with snapping, and the align bar
        private static readonly List<OverlayWindow> Open = new List<OverlayWindow>();
        private static OverlayWindow _selected;
        private const double SnapDistance = 8;
        private Point _moveStartMouse;
        private Point _moveStartPosition;
        private OverlayAlignBar _alignBar;

        /// <summary>An overlay drawn by the browser engine (WebView2); <paramref name="page"/> is a file in Overlays/Html/web.</summary>
        public OverlayWindow(string key, string title, string page, Size defaultSize)
        {
            _key = key;
            _page = page;
            _browser = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.Transparent };
            _defaultSize = defaultSize;

            Title = "Shiftline " + title;
            WindowStyle = WindowStyle.None;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            Width = defaultSize.Width;
            Height = defaultSize.Height;
            // see-through by DWM (set up in SetUpGlass): WebView2 cannot show in a WPF window with AllowsTransparency (airspace)
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                GlassFrameThickness = new Thickness(-1),
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false,
            });
            PageStatus = "starting";
            Loaded += (sender, args) => StartPage();

            Grid root = new Grid { Background = Brushes.Transparent };
            root.Children.Add(_browser);
            Content = root;

            // Edit mode: the page reports where the mouse was pressed (StartPress); a click selects the overlay (frame, align
            // bar, arrow keys), dragging moves it and snaps it to the screen edges and centre and to the other overlays.
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

                try
                {
                    _browser.Dispose();
                }
                catch (Exception)
                {
                    // the browser is already gone
                }
            };

            SourceInitialized += (sender, args) =>
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                ApplyStyles();
                SetUpGlass();
                // the browser may render before WPF reports ContentRendered: place the window as soon as it exists
                RestorePlacement();
            };
        }

        // ---------------------------------------------------------------- moving and resizing (the page reports the mouse)

        private void BeginResize()
        {
            _dragStartMouse = MouseOnScreen();
            _dragStartSize = new Size(Width, Height);
        }

        private void ResizeCorner(Point mouse)
        {
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
        }

        private void EndResizeCorner()
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
        }

        private void ResizeSide(Point mouse)
        {
            double pulled = Math.Max(MinSize, _dragStartSize.Width + (mouse.X - _dragStartMouse.X));
            Action<double> dragging = WidthDragging;
            if (dragging != null)
            {
                dragging(pulled / _dragStartSize.Width);
            }
        }

        private void EndResizeSide()
        {
            Action dragged = WidthDragged;
            if (dragged != null)
            {
                dragged();
            }
        }

        private void BeginMove()
        {
            Select(this);
            _moveStartMouse = MouseOnScreen();
            _moveStartPosition = new Point(Left, Top);
        }

        private void MoveStep(Point mouse, bool free)
        {
            double left = _moveStartPosition.X + mouse.X - _moveStartMouse.X;
            double top = _moveStartPosition.Y + mouse.Y - _moveStartMouse.Y;
            double? guideX = null;
            double? guideY = null;
            if (!free)
            {
                Snap(ref left, ref top, out guideX, out guideY);
            }

            Left = left;
            Top = top;
            OverlayGuides.Show(this, guideX, guideY);
        }

        private void EndMove()
        {
            OverlayGuides.Hide();
            SavePlacement();
        }

        /// <summary>Arrow keys move the selected overlay by 1 (Shift: 10). Returns whether the key was used.</summary>
        private bool MoveByKey(System.Windows.Input.Key key, bool shift)
        {
            if (!_edit || !ReferenceEquals(_selected, this))
            {
                return false;
            }

            double step = shift ? 10 : 1;
            switch (key)
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
                    return false;
            }

            SavePlacement();
            return true;
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
                SendEditState();
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

            ApplyStyles();
            SendEditState();
        }

        private void ApplyStyles()
        {
            if (_hwnd == IntPtr.Zero)
            {
                return;
            }

            // In edit mode the window may take the keyboard (for the arrow keys); in run mode it never takes the focus from the game.
            NativeMethods.SetExStyle(_hwnd, Wanted(NativeMethods.GetExStyle(_hwnd)));
        }

        /// <summary>The extended style the window should have now (edit mode or run mode; always layered).</summary>
        private int Wanted(int style)
        {
            style |= NativeMethods.WsExToolWindow | NativeMethods.WsExLayered;

            return _edit
                ? style & ~NativeMethods.WsExTransparent & ~NativeMethods.WsExNoActivate
                : style | NativeMethods.WsExTransparent | NativeMethods.WsExNoActivate;
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
                before.SendEditState();
            }

            if (window != null)
            {
                window.SendEditState();
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

        // ---------------------------------------------------------------- the page

        private const int WmStyleChanging = 0x007C;
        private const uint LwaAlpha = 0x2;
        private const int DwmCornerPreference = 33;
        private const int DwmBorderColor = 34;
        private const int DoNotRound = 1;
        private const int ColorNone = unchecked((int)0xFFFFFFFE);
        private const int VkLeftButton = 0x01;
        private const int VkAlt = 0x12;

        private readonly string _page;
        private readonly WebView2 _browser;
        private bool _pressing;
        private string _pressPart;

        /// <summary>Every message of the page that is not about moving or resizing (JSON text), on the UI thread.</summary>
        public event Action<string> PageMessage;

        /// <summary>The page has (re)loaded and can take messages (send its layout again here).</summary>
        public event Action PageReady;

        /// <summary>"starting", "running", or why the page could not start.</summary>
        public string PageStatus { get; private set; }

        public bool IsPageReady
        {
            get { return _browser.CoreWebView2 != null; }
        }

        /// <summary>Sends JSON to the page (UI thread); ignored until the page runs.</summary>
        public void Post(string json)
        {
            if (!IsPageReady)
            {
                return;
            }

            try
            {
                _browser.CoreWebView2.PostWebMessageAsJson(json);
            }
            catch (Exception)
            {
                // the browser is closing
            }
        }

        /// <summary>Tells every HTML overlay the overlay frame rate of General (call on the UI thread when it changes).</summary>
        public static void SendFrameRateToAll()
        {
            foreach (OverlayWindow window in Open.ToArray())
            {
                window.SendFrameRate();
            }
        }

        private void SendFrameRate()
        {
            Post("{\"type\":\"cap\",\"fps\":" + OverlaySettings.FrameRate + "}");
        }

        /// <summary>Edit mode, selection and grip layout for the page, which draws frame, name and grips itself.</summary>
        private void SendEditState()
        {
            JObject state = new JObject
            {
                { "type", "edit" },
                { "on", _edit },
                { "selected", ReferenceEquals(_selected, this) },
                { "fixed", _fixedWidth },
                { "name", Title.StartsWith("Shiftline ", StringComparison.Ordinal) ? Title.Substring(10) : Title },
            };
            Post(state.ToString(Newtonsoft.Json.Formatting.None));
        }

        private async void StartPage()
        {
            try
            {
                await HtmlHost.Load(_browser, _page, OnPageMessage);
                _browser.CoreWebView2.NavigationCompleted += (sender, args) =>
                {
                    SendFrameRate();
                    SendEditState();
                    Action ready = PageReady;
                    if (ready != null)
                    {
                        ready();
                    }
                };
                PageStatus = "running";
            }
            catch (WebView2RuntimeNotFoundException)
            {
                PageStatus = "WebView2 runtime not found on this PC";
                PluginLog.Info(Title + ": " + PageStatus);
            }
            catch (Exception ex)
            {
                PageStatus = "failed: " + ex.GetType().Name + ": " + ex.Message;
                PluginLog.Info(Title + ": " + PageStatus);
            }
        }

        private void OnPageMessage(string json)
        {
            JObject message;
            try
            {
                message = JObject.Parse(json);
            }
            catch (Exception)
            {
                return;
            }

            string type = (string)message["type"];
            if (type == "press")
            {
                StartPress((string)message["part"]);
                return;
            }

            if (type == "key")
            {
                MoveByKey(KeyOf((string)message["key"]), (bool?)message["shift"] == true);
                return;
            }

            Action<string> handler = PageMessage;
            if (handler != null)
            {
                handler(json);
            }
        }

        private static System.Windows.Input.Key KeyOf(string name)
        {
            switch (name)
            {
                case "ArrowLeft":
                    return System.Windows.Input.Key.Left;
                case "ArrowRight":
                    return System.Windows.Input.Key.Right;
                case "ArrowUp":
                    return System.Windows.Input.Key.Up;
                case "ArrowDown":
                    return System.Windows.Input.Key.Down;
                default:
                    return System.Windows.Input.Key.None;
            }
        }

        /// <summary>
        /// The page reports a mouse press on the body, the grip or the side edge. The mouse itself stays with the browser, so
        /// from here on the window follows the cursor on screen, every screen frame, until the button is released (same moving, snapping and
        /// resizing as in WPF mode).
        /// </summary>
        private void StartPress(string part)
        {
            if (!_edit || _pressing)
            {
                return;
            }

            _pressPart = part == "grip" || part == "side" ? part : "body";
            if (_pressPart == "body")
            {
                BeginMove();
            }
            else
            {
                Select(this);
                BeginResize();
            }

            // follow the cursor once per screen frame (a timer runs only every 15-31 ms and looks jerky)
            _pressing = true;
            CompositionTarget.Rendering += PressTick;
        }

        private void PressTick(object sender, EventArgs e)
        {
            bool down = (GetAsyncKeyState(VkLeftButton) & 0x8000) != 0;
            if (down)
            {
                Point mouse = MouseOnScreen();
                switch (_pressPart)
                {
                    case "body":
                        MoveStep(mouse, (GetAsyncKeyState(VkAlt) & 0x8000) != 0);
                        break;
                    case "side":
                        ResizeSide(mouse);
                        break;
                    default:
                        ResizeCorner(mouse);
                        break;
                }

                return;
            }

            _pressing = false;
            CompositionTarget.Rendering -= PressTick;
            switch (_pressPart)
            {
                case "body":
                    EndMove();
                    break;
                case "side":
                    EndResizeSide();
                    break;
                default:
                    EndResizeCorner();
                    break;
            }
        }

        /// <summary>A normal window made see-through by DWM; it keeps its layered and click-through bits.</summary>
        private void SetUpGlass()
        {
            HwndSource source = HwndSource.FromHwnd(_hwnd);
            if (source == null)
            {
                return;
            }

            source.CompositionTarget.BackgroundColor = Colors.Transparent;
            Margins glass = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            DwmExtendFrameIntoClientArea(_hwnd, ref glass);
            int corner = DoNotRound;
            DwmSetWindowAttribute(_hwnd, DwmCornerPreference, ref corner, sizeof(int));
            int border = ColorNone;
            DwmSetWindowAttribute(_hwnd, DwmBorderColor, ref border, sizeof(int));
            source.AddHook(KeepStyle);
            ApplyStyles();
            SetLayeredWindowAttributes(_hwnd, 0, 255, LwaAlpha);
        }

        /// <summary>WPF resets the extended style now and then; keep what the mode needs.</summary>
        private IntPtr KeepStyle(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmStyleChanging && wParam.ToInt64() == NativeMethods.GwlExStyle)
            {
                StyleStruct style = Marshal.PtrToStructure<StyleStruct>(lParam);
                int wanted = Wanted(style.StyleNew);
                if (wanted != style.StyleNew)
                {
                    style.StyleNew = wanted;
                    Marshal.StructureToPtr(style, lParam, false);
                }
            }

            return IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Margins
        {
            public int Left;
            public int Right;
            public int Top;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct StyleStruct
        {
            public int StyleOld;
            public int StyleNew;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("user32.dll")]
        private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int key);
    }
}
