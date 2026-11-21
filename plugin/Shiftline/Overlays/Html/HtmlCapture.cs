#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;

namespace Shiftline.Plugin
{
    /// <summary>
    /// Developer tool (Debug builds): renders an HTML overlay page to a picture, for the screenshot tool. A view is attached
    /// to a <see cref="Recorder"/>, gets its options and one update, and the recorded messages are played into the page in a
    /// browser of its own, off screen. Example:
    /// <code>
    /// HtmlCapture.Recorder recorder = new HtmlCapture.Recorder();
    /// StandingsHtmlView view = new StandingsHtmlView(OverlayData.StandingsKey);
    /// view.ApplyOptions(options);
    /// view.Attach(recorder);
    /// view.Update(sample);
    /// BitmapSource picture = await HtmlCapture.RenderAsync(StandingsHtmlView.Page, view.DesignSize, recorder.Messages, 2);
    /// </code>
    /// The picture is see-through where the overlay is (WebView2 keeps the transparency of the page).
    /// </summary>
    internal static class HtmlCapture
    {
        /// <summary>Takes the messages a view sends, instead of a window.</summary>
        internal sealed class Recorder : IHtmlSurface
        {
            private readonly List<string> _messages = new List<string>();

            public event Action PageReady
            {
                add { }
                remove { }
            }

            public event Action<string> PageMessage
            {
                add { }
                remove { }
            }

            /// <summary>Everything sent so far, in order (layout first, then the frames).</summary>
            public IList<string> Messages
            {
                get { return _messages; }
            }

            public void Post(string json)
            {
                if (json != null)
                {
                    _messages.Add(json);
                }
            }
        }

        /// <summary>
        /// Loads the page (for example StandingsHtmlView.Page) in a browser off screen, plays the messages into it, waits until
        /// it has drawn and returns the picture. "size" is the overlay's design size, "scale" the resolution (2: twice as sharp).
        /// Call on the UI thread.
        /// </summary>
        public static async Task<BitmapSource> RenderAsync(string page, Size size, IEnumerable<string> messages, double scale = 2)
        {
            Window window = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = false,
                Left = -32000,
                Top = -32000,
                Width = size.Width * scale + 2,             // at least as large as the browser inside (its bounds are pixels)
                Height = size.Height * scale + 2,
                Title = "Shiftline capture"
            };
            CoreWebView2Controller controller = null;
            try
            {
                window.Show();
                IntPtr handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                CoreWebView2Environment environment = await HtmlHost.Environment();
                controller = await environment.CreateCoreWebView2ControllerAsync(handle);
                controller.DefaultBackgroundColor = System.Drawing.Color.Transparent;
                controller.ShouldDetectMonitorScaleChanges = false;
                controller.RasterizationScale = scale;
                controller.Bounds = new System.Drawing.Rectangle(0, 0, (int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale));
                controller.IsVisible = true;

                TaskCompletionSource<bool> loaded = new TaskCompletionSource<bool>();
                controller.CoreWebView2.NavigationCompleted += (sender, args) => loaded.TrySetResult(args.IsSuccess);
                HtmlHost.Open(controller.CoreWebView2, environment, page, null);
                await loaded.Task;

                foreach (string message in messages)
                {
                    controller.CoreWebView2.PostWebMessageAsJson(message);
                }

                // fonts, flags and the first frames of the drawing loop
                await controller.CoreWebView2.ExecuteScriptAsync("document.fonts.ready.then(() => 0)");
                await Task.Delay(600);

                using (MemoryStream stream = new MemoryStream())
                {
                    await controller.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
                    stream.Position = 0;
                    BitmapImage image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = stream;
                    image.EndInit();
                    image.Freeze();
                    return image;
                }
            }
            finally
            {
                if (controller != null)
                {
                    controller.Close();
                }

                window.Close();
            }
        }
    }
}
#endif
