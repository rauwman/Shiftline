using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;

namespace Shiftline.Plugin
{
    /// <summary>Where an HTML overlay page lives: an overlay window above the game, or a preview on a settings page.</summary>
    internal interface IHtmlSurface
    {
        /// <summary>The page has (re)loaded and can take messages (send its layout again here).</summary>
        event Action PageReady;

        /// <summary>Every message of the page (JSON text), on the UI thread.</summary>
        event Action<string> PageMessage;

        void Post(string json);
    }

    /// <summary>
    /// The HTML preview of the settings pages. There is only one (<see cref="Shared"/>): it waits loaded in a hidden spot of
    /// the plugin's settings (<see cref="HtmlPreviewParking"/>) and a page borrows it with <see cref="MoveTo"/>, so opening a
    /// page shows the preview at once instead of starting a browser first. It uses the composition control of WebView2, which
    /// is drawn into the WPF page, so it scrolls and clips like everything else on the page.
    /// </summary>
    internal sealed class HtmlPreview : Border, IHtmlSurface
    {
        private const string StartPage = "preview.html?preview=1";     // every overlay page in one (see web/preview.html)
        private static HtmlPreview _shared;
        private static Decorator _parking;
        private readonly WebView2CompositionControl _view;
        private bool _started;

        private HtmlPreview()
        {
            _view = new WebView2CompositionControl { DefaultBackgroundColor = System.Drawing.Color.Transparent };
            Child = _view;

            // The control shows the page as a bitmap. Smooth scaling blurs it as soon as it sits half a pixel off the
            // screen grid, so keep it on whole pixels and draw the bitmap 1:1.
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            System.Windows.Media.RenderOptions.SetBitmapScalingMode(_view, System.Windows.Media.BitmapScalingMode.NearestNeighbor);
            Loaded += (sender, args) => Start();
        }

        public event Action PageReady;

        public event Action<string> PageMessage;

        /// <summary>The one preview (created on first use, call on the UI thread).</summary>
        public static HtmlPreview Shared
        {
            get
            {
                if (_shared == null)
                {
                    _shared = new HtmlPreview();
                }

                return _shared;
            }
        }

        /// <summary>The page has loaded and takes messages.</summary>
        public bool IsReady { get; private set; }

        /// <summary>Sets the hidden spot where the preview waits; it is parked there right away.</summary>
        public static void SetParking(Decorator parking)
        {
            _parking = parking;
            if (HtmlHost.RuntimeVersion() != null && (_shared == null || _shared.Parent == null))
            {
                Shared.MoveTo(parking);
            }
        }

        /// <summary>Puts the preview into <paramref name="host"/>, taking it out of wherever it is now.</summary>
        public void MoveTo(Decorator host)
        {
            if (host == null || host.Child == this)
            {
                return;
            }

            Decorator old = Parent as Decorator;
            if (old != null)
            {
                old.Child = null;
            }

            host.Child = this;
        }

        /// <summary>Gives the preview back to the hidden spot, if <paramref name="host"/> still has it.</summary>
        public void Release(Decorator host)
        {
            if (host == null || host.Child != this)
            {
                return;
            }

            host.Child = null;
            if (_parking != null)
            {
                _parking.Child = this;
            }
        }

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

        private async void Start()
        {
            if (_started)
            {
                return;
            }

            _started = true;
            try
            {
                await HtmlHost.Load(_view, StartPage, json =>
                {
                    Action<string> handler = PageMessage;
                    if (handler != null)
                    {
                        handler(json);
                    }
                });
                _view.CoreWebView2.NavigationCompleted += (sender, args) =>
                {
                    IsReady = true;
                    Action ready = PageReady;
                    if (ready != null)
                    {
                        ready();
                    }
                };
            }
            catch (Exception ex)
            {
                _started = false;           // try again the next time it is shown
                PluginLog.Info("HTML preview: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// The hidden spot in the plugin's settings where the shared HTML preview waits loaded (see <see cref="HtmlPreview"/>).
    /// It takes no room and cannot be seen or clicked.
    /// </summary>
    internal sealed class HtmlPreviewParking : Border
    {
        public HtmlPreviewParking()
        {
            Visibility = Visibility.Hidden;
            IsHitTestVisible = false;
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Top;
            Width = 900;
            Height = 400;
            Loaded += (sender, args) => HtmlPreview.SetParking(this);
        }
    }
}
