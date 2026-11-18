using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Shiftline.Plugin
{
    /// <summary>
    /// A see-through window above the game whose content is drawn by the browser engine (WebView2). WebView2 cannot show inside
    /// a WPF window with AllowsTransparency (airspace), so this is a normal window made see-through by DWM. It never takes the
    /// focus; it lets every click through unless <see cref="ClickThrough"/> is off (edit mode), then the page can be dragged.
    /// </summary>
    internal sealed class HtmlOverlayWindow : Window
    {
        private const int GwlExStyle = -20;
        private const int WsExTransparent = 0x00000020;
        private const int WsExToolWindow = 0x00000080;
        private const int WsExLayered = 0x00080000;
        private const int WsExNoActivate = 0x08000000;
        private const int AlwaysKept = WsExToolWindow | WsExLayered | WsExNoActivate;
        private const int WmStyleChanging = 0x007C;
        private const int WmNcLeftButtonDown = 0x00A1;
        private const int HtCaption = 2;
        private const uint LwaAlpha = 0x2;
        private const int DwmCornerPreference = 33;
        private const int DwmBorderColor = 34;
        private const int DoNotRound = 1;
        private const int ColorNone = unchecked((int)0xFFFFFFFE);

        private static readonly List<HtmlOverlayWindow> Open = new List<HtmlOverlayWindow>();
        private readonly WebView2 _view;
        private readonly string _page;
        private IntPtr _hwnd;
        private bool _clickThrough = true;

        public HtmlOverlayWindow(string title, string page, Rect bounds)
        {
            _page = page;
            Title = "Shiftline " + title;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = false;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                GlassFrameThickness = new Thickness(-1),
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false,
            });

            _view = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.Transparent };
            Content = _view;
            Status = "starting";
            Loaded += (sender, args) => Start();
            Open.Add(this);
            Closed += (sender, args) =>
            {
                Open.Remove(this);
                try
                {
                    _view.Dispose();
                }
                catch (Exception)
                {
                    // the browser is already gone
                }
            };
        }

        /// <summary>Every message of the page (JSON text), on the UI thread. A message {"type":"drag"} is handled here.</summary>
        public event Action<string> MessageReceived;

        /// <summary>"starting", "running", or why the page could not start.</summary>
        public string Status { get; private set; }

        public bool IsReady
        {
            get { return _view.CoreWebView2 != null; }
        }

        /// <summary>Clicks go through to the game (normal) or reach the page (edit mode).</summary>
        public bool ClickThrough
        {
            get { return _clickThrough; }
            set
            {
                _clickThrough = value;
                if (_hwnd != IntPtr.Zero)
                {
                    SetWindowLong(_hwnd, GwlExStyle, Wanted(GetWindowLong(_hwnd, GwlExStyle)));
                }
            }
        }

        /// <summary>Sends JSON to the page. Call on the window's UI thread; ignored until the page runs.</summary>
        public void Post(string json)
        {
            if (_view.CoreWebView2 == null)
            {
                return;
            }

            try
            {
                _view.CoreWebView2.PostWebMessageAsJson(json);
            }
            catch (Exception)
            {
                // the browser is closing
            }
        }

        /// <summary>Tells every open page the overlay frame rate of General (call on the UI thread when it changes).</summary>
        public static void SendFrameRateToAll()
        {
            foreach (HtmlOverlayWindow window in Open.ToArray())
            {
                window.SendFrameRate();
            }
        }

        private void SendFrameRate()
        {
            Post("{\"type\":\"cap\",\"fps\":" + OverlaySettings.FrameRate + "}");
        }

        private async void Start()
        {
            try
            {
                await HtmlHost.Load(_view, _page, OnMessage);
                _view.CoreWebView2.NavigationCompleted += (sender, args) => SendFrameRate();
                Status = "running";
            }
            catch (WebView2RuntimeNotFoundException)
            {
                Status = "WebView2 runtime not found on this PC";
            }
            catch (Exception ex)
            {
                Status = "failed: " + ex.GetType().Name + ": " + ex.Message;
                PluginLog.Info(Title + ": " + Status);
            }
        }

        private void OnMessage(string json)
        {
            if (!_clickThrough && json.IndexOf("\"drag\"", StringComparison.Ordinal) >= 0 && _hwnd != IntPtr.Zero)
            {
                // fallback when the page's CSS drag area (app-region) does not reach the window: start the system move loop
                ReleaseCapture();
                SendMessage(_hwnd, WmNcLeftButtonDown, new IntPtr(HtCaption), IntPtr.Zero);
                DragsByMessage++;
            }

            Action<string> handler = MessageReceived;
            if (handler != null)
            {
                handler(json);
            }
        }

        /// <summary>How often a drag came by message (the fallback) instead of the CSS drag area; for the test.</summary>
        public int DragsByMessage { get; private set; }

        public string Bounds
        {
            get { return string.Format(CultureInfo.InvariantCulture, "{0:0},{1:0} {2:0}x{3:0}", Left, Top, ActualWidth, ActualHeight); }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            HwndSource source = (HwndSource)PresentationSource.FromVisual(this);
            if (source == null)
            {
                return;
            }

            source.CompositionTarget.BackgroundColor = Colors.Transparent;
            _hwnd = source.Handle;
            Margins glass = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            DwmExtendFrameIntoClientArea(_hwnd, ref glass);
            int corner = DoNotRound;
            DwmSetWindowAttribute(_hwnd, DwmCornerPreference, ref corner, sizeof(int));
            int border = ColorNone;
            DwmSetWindowAttribute(_hwnd, DwmBorderColor, ref border, sizeof(int));

            source.AddHook(Hook);
            SetWindowLong(_hwnd, GwlExStyle, Wanted(GetWindowLong(_hwnd, GwlExStyle)));
            SetLayeredWindowAttributes(_hwnd, 0, 255, LwaAlpha);
        }

        private int Wanted(int style)
        {
            style |= AlwaysKept;
            return _clickThrough ? style | WsExTransparent : style & ~WsExTransparent;
        }

        /// <summary>WPF resets the extended style now and then; keep click-through and the other bits as wanted.</summary>
        private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmStyleChanging && wParam.ToInt64() == GwlExStyle)
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

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

        [DllImport("user32.dll")]
        private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}
