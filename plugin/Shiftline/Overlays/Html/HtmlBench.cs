using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json.Linq;

namespace Shiftline.Plugin
{
    /// <summary>
    /// TEST ONLY (Debug builds): the measuring step before the overlays move to HTML. Opens four HTML windows the size of the
    /// real overlays (a standings table, relatives, a battle bar, a track map) and feeds them made-up cars, so the rows change
    /// places, gaps change and the cars move on the map. Each page counts how often the browser draws and its longest gap, and
    /// reports it every second; the test adds the memory of the browser processes and writes everything to
    /// PluginsData\Shiftline\html-test.csv, so a run inside a game can be read afterwards. Invented names only.
    /// </summary>
    internal static class HtmlBench
    {
        private const int Cars = 24;
        private static readonly List<Slot> Slots = new List<Slot>();
        private static readonly Random Dice = new Random(7);
        private static readonly double[] Progress = new double[Cars];
        private static readonly double[] Pace = new double[Cars];
        private static DispatcherTimer _dataTimer;
        private static DispatcherTimer _mapTimer;
        private static DispatcherTimer _statsTimer;
        private static DateTime _last;
        private static double _lapStart;
        private static long _memory;
        private static bool _mouse;
        private static string _logFile;

        /// <summary>One window (the track map) instead of four: shows whether the windows share one drawing budget.</summary>
        public static bool Single { get; set; }

        public static bool IsRunning
        {
            get { return Slots.Count > 0; }
        }

        /// <summary>Off: clicks go through (as in a race). On: the windows take the mouse and can be dragged (edit mode test).</summary>
        public static bool Mouse
        {
            get { return _mouse; }
            set
            {
                _mouse = value;
                foreach (Slot slot in Slots)
                {
                    slot.Window.SetEditMode(value);
                }
            }
        }

        public static void Start()
        {
            if (IsRunning)
            {
                return;
            }

            Rect area = SystemParameters.WorkArea;
            if (!Single)
            {
                Open("rows", "Test Standings", new Rect(area.Left + 40, area.Top + 120, 380, 520), 20);
                Open("rows", "Test Relatives", new Rect(area.Left + 40, area.Top + 660, 380, 250), 7);
                Open("bar", "Test Battle Bar", new Rect(area.Left + (area.Width - 900) / 2, area.Top + 30, 900, 96), 8);
            }

            Open("map", "Test Track Map", new Rect(area.Right - 360, area.Top + 120, 320, 320), Cars);

            for (int i = 0; i < Cars; i++)
            {
                Progress[i] = 3 - i * 0.031;
                Pace[i] = 1 / (90.0 + i * 0.15);
            }

            _last = DateTime.UtcNow;
            _lapStart = Now();
            _logFile = Path.Combine(ProfileStore.RootFolder, "html-test.csv");
            File.WriteAllText(_logFile, "time;window;frames per second;longest gap ms;browser memory MB;window in front;windows;frame rate;gpu priority" + Environment.NewLine);

            _dataTimer = new DispatcherTimer(DispatcherPriority.Normal);
            _dataTimer.Tick += (sender, args) => SendRows();
            _dataTimer.Start();
            _mapTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(16) };
            _mapTimer.Tick += (sender, args) => SendMap();
            _mapTimer.Start();
            _statsTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _statsTimer.Tick += (sender, args) => CollectStats();
            _statsTimer.Start();
        }

        public static void Stop()
        {
            foreach (DispatcherTimer timer in new[] { _dataTimer, _mapTimer, _statsTimer })
            {
                if (timer != null)
                {
                    timer.Stop();
                }
            }

            _dataTimer = _mapTimer = _statsTimer = null;
            foreach (Slot slot in Slots.ToArray())
            {
                slot.Window.Close();
            }

            Slots.Clear();
            _mouse = false;
        }

        /// <summary>A few lines for the test card.</summary>
        public static string Status
        {
            get
            {
                StringBuilder text = new StringBuilder();
                text.AppendLine("WebView2 runtime".PadRight(20) + (HtmlHost.RuntimeVersion() ?? "not found"));
                if (!IsRunning)
                {
                    text.Append("Test".PadRight(20) + "off");
                    return text.ToString();
                }

                foreach (Slot slot in Slots)
                {
                    text.AppendLine(slot.Name.PadRight(20) + (slot.Window.PageStatus == "running" ? slot.Stats : slot.Window.PageStatus));
                }

                text.AppendLine("Browser memory".PadRight(20) + (_memory > 0 ? (_memory / 1048576) + " MB (all Shiftline browser processes)" : "-"));
                text.AppendLine("Windows".PadRight(20) + Slots.Count);
                text.AppendLine("Frame rate".PadRight(20) + (OverlaySettings.FrameRate == 0 ? "unlimited" : OverlaySettings.FrameRate + " per second") + " (General)");
                text.AppendLine("GPU priority".PadRight(20) + (OverlaySettings.GpuRealtime ? "realtime" : "normal") + " (General)" + (string.IsNullOrEmpty(GpuPriority.Problem) ? string.Empty : ", " + GpuPriority.Problem));
                text.Append("Log".PadRight(20) + _logFile);
                return text.ToString();
            }
        }

        private static void Open(string kind, string name, Rect bounds, int rows)
        {
            Slot slot = new Slot { Kind = kind, Name = name, Rows = rows, Stats = "waiting for the page" };
            // the real overlay window in HTML mode (moving, snapping, align bar as in WPF); a first place when it has none saved
            string key = "HtmlTest." + name.Replace(" ", string.Empty);
            if (OverlaySettings.GetPlacement(key) == null)
            {
                OverlaySettings.SetPlacement(key, new OverlayPlacement
                {
                    Screen = System.Windows.Forms.Screen.PrimaryScreen.DeviceName,
                    X = bounds.Left / SystemParameters.PrimaryScreenWidth,
                    Y = bounds.Top / SystemParameters.PrimaryScreenHeight,
                    W = bounds.Width / SystemParameters.PrimaryScreenWidth,
                    H = bounds.Height / SystemParameters.PrimaryScreenHeight,
                });
            }

            slot.Window = new OverlayWindow(key, name, "test.html?kind=" + kind + "&name=" + Uri.EscapeDataString(name), bounds.Size);
            slot.Window.PageMessage += json => OnMessage(slot, json);
            slot.Window.Closed += (sender, args) => Slots.Remove(slot);
            Slots.Add(slot);
            slot.Window.Show();
        }

        private static void OnMessage(Slot slot, string json)
        {
            try
            {
                JObject message = JObject.Parse(json);
                if ((string)message["type"] != "stats")
                {
                    return;
                }

                slot.Fps = (double)message["fps"];
                slot.Gap = (double)message["gap"];
                slot.Stats = string.Format(CultureInfo.InvariantCulture, "{0,5:0.0} frames/s, longest gap {1,4:0} ms", slot.Fps, slot.Gap);
                slot.Fresh = true;
            }
            catch (Exception)
            {
                // not a message of ours
            }
        }

        /// <summary>Moves the made-up cars on; called before each send.</summary>
        private static void Advance()
        {
            DateTime now = DateTime.UtcNow;
            double seconds = Math.Min(0.5, (now - _last).TotalSeconds);
            _last = now;
            for (int i = 0; i < Cars; i++)
            {
                // a little noise per car, so they close in, pass and drop back
                Pace[i] = Math.Max(1 / 93.0, Math.Min(1 / 88.0, Pace[i] + (Dice.NextDouble() - 0.5) * 0.000004));
                Progress[i] += seconds * Pace[i];
            }

            if (Now() - _lapStart > 92)
            {
                _lapStart = Now();
            }
        }

        private static void SendRows()
        {
            _dataTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / OverlaySettings.GlobalRate);
            Advance();
            int[] order = Enumerable.Range(0, Cars).OrderByDescending(i => Progress[i]).ToArray();
            double now = Now();
            foreach (Slot slot in Slots)
            {
                if (slot.Kind == "map" || !slot.Window.IsPageReady)
                {
                    continue;
                }

                // the standings show the top of the field; relatives and the bar the cars around car 0 ("you")
                int me = Array.IndexOf(order, 0);
                int first = slot.Rows >= 20 ? 0 : Math.Max(0, Math.Min(Cars - slot.Rows, me - slot.Rows / 2));
                StringBuilder json = new StringBuilder();
                json.Append("{\"type\":\"rows\",\"clock\":{\"v\":").Append(Num(now - _lapStart)).Append(",\"t\":").Append(Num(now)).Append("},\"rows\":[");
                for (int r = 0; r < slot.Rows && first + r < Cars; r++)
                {
                    int car = order[first + r];
                    double gap = (Progress[order[0]] - Progress[car]) * 90;
                    if (r > 0)
                    {
                        json.Append(',');
                    }

                    json.Append("{\"id\":").Append(car)
                        .Append(",\"pos\":").Append(first + r + 1)
                        .Append(",\"num\":\"").Append(car * 7 % 99 + 1).Append('"')
                        .Append(",\"name\":\"Driver ").Append((char)('A' + car % 26)).Append(". Example").Append('"')
                        .Append(",\"gap\":\"").Append(first + r == 0 ? "Leader" : "+" + Num(gap, "0.000")).Append('"')
                        .Append(",\"cls\":\"").Append(car % 3 == 0 ? "#33ceff" : "#ffda59").Append('"')
                        .Append(",\"me\":").Append(car == 0 ? "true" : "false")
                        .Append('}');
                }

                json.Append("]}");
                slot.Window.Post(json.ToString());
            }
        }

        private static void SendMap()
        {
            Advance();
            StringBuilder json = new StringBuilder("{\"type\":\"map\",\"t\":").Append(Num(Now())).Append(",\"cars\":[");
            for (int i = 0; i < Cars; i++)
            {
                json.Append(i == 0 ? string.Empty : ",").Append(Num(Progress[i] - Math.Floor(Progress[i]), "0.000000"));
            }

            json.Append("]}");
            string text = json.ToString();
            foreach (Slot slot in Slots)
            {
                if (slot.Kind == "map" && slot.Window.IsPageReady)
                {
                    slot.Window.Post(text);
                }
            }
        }

        private static async void CollectStats()
        {
            try
            {
                CoreWebView2Environment environment = await HtmlHost.Environment();
                long bytes = 0;
                foreach (CoreWebView2ProcessInfo process in environment.GetProcessInfos())
                {
                    try
                    {
                        using (Process running = Process.GetProcessById(process.ProcessId))
                        {
                            bytes += running.PrivateMemorySize64;
                        }
                    }
                    catch (Exception)
                    {
                        // the process has just ended
                    }
                }

                _memory = bytes;
            }
            catch (Exception)
            {
                _memory = 0;
            }

            if (_logFile == null)
            {
                return;
            }

            string front = ForegroundProcess();
            string time = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            StringBuilder lines = new StringBuilder();
            foreach (Slot slot in Slots)
            {
                if (slot.Fresh)
                {
                    slot.Fresh = false;
                    lines.Append(time).Append(';').Append(slot.Name).Append(';').Append(Num(slot.Fps, "0.0")).Append(';')
                        .Append(Num(slot.Gap, "0")).Append(';').Append(_memory / 1048576).Append(';').Append(front)
                        .Append(';').Append(Slots.Count).Append(';').Append(OverlaySettings.FrameRate)
                        .Append(';').Append(OverlaySettings.GpuRealtime ? "realtime" : "normal").AppendLine();
                }
            }

            try
            {
                File.AppendAllText(_logFile, lines.ToString());
            }
            catch (Exception)
            {
                // the file is open somewhere; the next second tries again
            }
        }

        private static string ForegroundProcess()
        {
            try
            {
                uint id;
                GetWindowThreadProcessId(GetForegroundWindow(), out id);
                using (Process process = Process.GetProcessById((int)id))
                {
                    return process.ProcessName;
                }
            }
            catch (Exception)
            {
                return "?";
            }
        }

        /// <summary>Seconds since 1970 (UTC), the clock of the page (performance.timeOrigin + performance.now()).</summary>
        private static double Now()
        {
            return (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        private static string Num(double value, string format = "0.000")
        {
            return value.ToString(format, CultureInfo.InvariantCulture);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        private sealed class Slot
        {
            public string Kind;
            public string Name;
            public int Rows;
            public OverlayWindow Window;
            public string Stats;
            public double Fps;
            public double Gap;
            public bool Fresh;
        }
    }
}
