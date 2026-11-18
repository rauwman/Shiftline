using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Shiftline.Plugin
{
    /// <summary>
    /// The browser engine (WebView2) of the HTML overlays. One shared environment, so every overlay window runs in the same
    /// browser process. Pages, scripts and fonts come from the plugin DLL under https://shiftline.overlay/ (nothing is written
    /// to disk except the browser's own cache folder PluginsData\Shiftline\WebView2).
    /// </summary>
    internal static class HtmlHost
    {
        public const string Origin = "https://shiftline.overlay/";

        // Must stay the same as in every other place that opens an environment on the same folder while the browser runs.
        private const string BrowserArguments = "--disable-features=CalculateNativeWinOcclusion"
            + " --disable-background-timer-throttling --disable-renderer-backgrounding"
            + " --disable-backgrounding-occluded-windows";

        private const string WebResourcePrefix = "Shiftline.web.";
        private static readonly Uri FontFolder = new Uri("pack://application:,,,/Shiftline.Plugin;component/Assets/Fonts/");
        private static readonly Dictionary<string, byte[]> Files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        private static Task<CoreWebView2Environment> _environment;
        private static CoreWebView2Environment _ready;

        /// <summary>The version of the WebView2 runtime on this PC, or null when it is missing.</summary>
        public static string RuntimeVersion()
        {
            try
            {
                return CoreWebView2Environment.GetAvailableBrowserVersionString();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>The shared environment; created on first use (call on the UI thread).</summary>
        public static async Task<CoreWebView2Environment> Environment()
        {
            if (_environment == null)
            {
                string folder = Path.Combine(ProfileStore.RootFolder, "WebView2");
                Directory.CreateDirectory(folder);
                CoreWebView2EnvironmentOptions options = new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = BrowserArguments };
                _environment = CoreWebView2Environment.CreateAsync(null, folder, options);
            }

            try
            {
                CoreWebView2Environment environment = await _environment;
                if (_ready != environment)
                {
                    _ready = environment;
                    // a new or restarted GPU process starts at normal priority: give it the overlays' priority again
                    environment.ProcessInfosChanged += (sender, args) => ApplyGpuPriority();
                    ApplyGpuPriority();
                }

                return environment;
            }
            catch (Exception)
            {
                _environment = null;        // let the next window try again
                throw;
            }
        }

        /// <summary>
        /// Starts the browser in <paramref name="view"/> and opens <paramref name="page"/> (a file name in Overlays/Html/web).
        /// <paramref name="onMessage"/> gets every message of the page as JSON text, on the UI thread.
        /// </summary>
        public static async Task Load(WebView2 view, string page, Action<string> onMessage)
        {
            CoreWebView2Environment environment = await Environment();
            await view.EnsureCoreWebView2Async(environment);
            CoreWebView2 core = view.CoreWebView2;
            CoreWebView2Settings settings = core.Settings;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreDevToolsEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = false;
            settings.IsNonClientRegionSupportEnabled = true;     // lets the page mark a drag area with CSS app-region (edit mode)
            core.AddWebResourceRequestedFilter(Origin + "*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (sender, args) => Serve(environment, args);
            if (onMessage != null)
            {
                core.WebMessageReceived += (sender, args) => onMessage(args.WebMessageAsJson);
            }

            core.Navigate(Origin + page);
        }

        /// <summary>Gives the browser's GPU process the GPU priority of the setting in General (see GpuPriority).</summary>
        public static void ApplyGpuPriority()
        {
            CoreWebView2Environment environment = _ready;
            if (environment == null)
            {
                return;
            }

            try
            {
                foreach (CoreWebView2ProcessInfo info in environment.GetProcessInfos())
                {
                    if (info.Kind == CoreWebView2ProcessKind.Gpu)
                    {
                        using (Process process = Process.GetProcessById(info.ProcessId))
                        {
                            GpuPriority.Set(process.Handle, GpuPriority.Wanted);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                PluginLog.Info("GPU priority of the browser: " + ex.Message);
            }
        }

        private static void Serve(CoreWebView2Environment environment, CoreWebView2WebResourceRequestedEventArgs args)
        {
            string path = new Uri(args.Request.Uri).AbsolutePath.TrimStart('/');
            byte[] data = Read(path);
            args.Response = data == null
                ? environment.CreateWebResourceResponse(null, 404, "Not Found", string.Empty)
                : environment.CreateWebResourceResponse(new MemoryStream(data), 200, "OK", "Content-Type: " + ContentType(path) + "\r\nCache-Control: no-cache");
        }

        /// <summary>A file of the pages: fonts/x.ttf from the WPF fonts, everything else from the embedded web folder.</summary>
        private static byte[] Read(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Contains("..") || path.Contains("\\"))
            {
                return null;
            }

            lock (Files)
            {
                byte[] data;
                if (Files.TryGetValue(path, out data))
                {
                    return data;
                }

                try
                {
                    Stream stream = null;
                    if (path.StartsWith("fonts/", StringComparison.OrdinalIgnoreCase))
                    {
                        System.Windows.Resources.StreamResourceInfo info = Application.GetResourceStream(new Uri(FontFolder, path.Substring(6)));
                        stream = info == null ? null : info.Stream;
                    }
                    else if (path.IndexOf('/') < 0)
                    {
                        stream = typeof(HtmlHost).Assembly.GetManifestResourceStream(WebResourcePrefix + path);
                    }

                    if (stream != null)
                    {
                        using (stream)
                        using (MemoryStream copy = new MemoryStream())
                        {
                            stream.CopyTo(copy);
                            data = copy.ToArray();
                        }
                    }
                }
                catch (Exception ex)
                {
                    PluginLog.Info("HTML overlay file " + path + ": " + ex.Message);
                    data = null;
                }

                Files[path] = data;
                return data;
            }
        }

        private static string ContentType(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".html":
                    return "text/html; charset=utf-8";
                case ".js":
                    return "text/javascript; charset=utf-8";
                case ".css":
                    return "text/css; charset=utf-8";
                case ".json":
                    return "application/json";
                case ".ttf":
                    return "font/ttf";
                case ".png":
                    return "image/png";
                case ".svg":
                    return "image/svg+xml";
                default:
                    return "application/octet-stream";
            }
        }
    }
}
