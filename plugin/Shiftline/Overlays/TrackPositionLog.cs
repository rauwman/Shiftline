using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Media;
using System.Windows.Threading;

namespace Shiftline.Plugin
{
    /// <summary>
    /// Records the own position on the lap for a while (data check of the Track Map): every change is written to a CSV file in
    /// PluginsData\Shiftline\Logs, so one can see afterwards how often and how evenly a game delivers it. Runs on its own, the
    /// settings page may be closed meanwhile. Three kinds of lines: "data" (the standings), "feed" (the fast position feed,
    /// with the time it was read) and "frame" (where the Track Map drew the own car, with its delay). A fourth kind, "ui",
    /// every 100 ms from a thread of its own: the longest wait for SimHub's UI thread (uiLagMs), how often WPF rendered
    /// (renders) and the time the Track Map used on that thread (workMs). It tells a busy UI thread from a slow render loop.
    /// </summary>
    internal static class TrackPositionLog
    {
        private static readonly Stopwatch Clock = new Stopwatch();
        private static readonly List<string> Lines = new List<string>();
        private static DispatcherTimer _timer;
        private static double _seconds;
        private static double _lastPct = -2;
        private static long _lastSequence = -1;
        private static long _lastFeed = -1;
        private static double _startUtc;
        private static Thread _probe;
        private static volatile bool _probing;
        private static int _renders;
        private static string _path;
        private static double _savedAt;

        public static bool IsRecording
        {
            get { return _timer != null && _timer.IsEnabled; }
        }

        public static int SecondsLeft
        {
            get { return IsRecording ? (int)Math.Ceiling(Math.Max(0, _seconds - Clock.Elapsed.TotalSeconds)) : 0; }
        }

        /// <summary>The file of the last recording, empty when there is none.</summary>
        public static string LastFile { get; private set; } = string.Empty;

        public static void Start(double seconds)
        {
            if (IsRecording)
            {
                return;
            }

            lock (Lines)
            {
                Lines.Clear();
                Lines.Add("seconds;kind;sequence;game;track;lapDistPct;laps;onPitRoad;trackSurface;delayMs;uiLagMs;renders;workMs");
            }

            _seconds = seconds;
            _lastPct = -2;
            _lastSequence = -1;
            _lastFeed = -1;
            _startUtc = DateTime.UtcNow.Ticks / (double)TimeSpan.TicksPerSecond;
            _savedAt = 0;
            try
            {
                string folder = Path.Combine(ProfileStore.RootFolder, "Logs");
                Directory.CreateDirectory(folder);
                _path = Path.Combine(folder, "trackmap-position-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".csv");
            }
            catch (Exception ex)
            {
                _path = null;
                PluginLog.Info("Track Map: the position record has no folder: " + ex.Message);
            }

            if (_timer == null)
            {
                _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(5) };
                _timer.Tick += (sender, args) => Sample();
            }

            Clock.Restart();
            _timer.Start();
            _renders = 0;
            TrackMapModel.TakeWorkMs();
            CompositionTarget.Rendering += OnRender;
            Dispatcher ui = Dispatcher.CurrentDispatcher;
            _probing = true;
            _probe = new Thread(() => Probe(ui)) { IsBackground = true, Name = "Shiftline position record" };
            _probe.Start();
        }

        private static void OnRender(object sender, EventArgs e)
        {
            Interlocked.Increment(ref _renders);
        }

        private static void Add(string line)
        {
            lock (Lines)
            {
                Lines.Add(line);
            }
        }

        /// <summary>Asks the UI thread for an empty job every 5 ms and notes how long it took to get its turn.</summary>
        private static void Probe(Dispatcher ui)
        {
            Stopwatch window = Stopwatch.StartNew();
            double maxLag = 0;
            while (_probing)
            {
                long asked = Stopwatch.GetTimestamp();
                try
                {
                    ui.Invoke(new Action(() => { }), DispatcherPriority.Send, CancellationToken.None, TimeSpan.FromSeconds(2));
                }
                catch (Exception)
                {
                    // SimHub is closing
                }

                maxLag = Math.Max(maxLag, (Stopwatch.GetTimestamp() - asked) * 1000.0 / Stopwatch.Frequency);
                if (window.ElapsedMilliseconds >= 100)
                {
                    int renders = Interlocked.Exchange(ref _renders, 0);
                    Add(string.Join(";", new[]
                    {
                        Clock.Elapsed.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture),
                        "ui",
                        string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
                        maxLag.ToString("0.0", CultureInfo.InvariantCulture),
                        renders.ToString(CultureInfo.InvariantCulture),
                        TrackMapModel.TakeWorkMs().ToString("0.0", CultureInfo.InvariantCulture)
                    }));
                    maxLag = 0;
                    window.Restart();
                }

                Thread.Sleep(5);
            }
        }

        private static void Sample()
        {
            double now = Clock.Elapsed.TotalSeconds;
            if (now >= _seconds)
            {
                Finish();
                return;
            }

            if (now - _savedAt >= 5)
            {
                _savedAt = now;
                Save();         // written as it goes, so a record that is cut short (SimHub closed) is not lost
            }

            StandingsSnapshot snapshot = OverlayData.Current;
            CarPositions feed = OverlayData.Positions;
            if (feed.Sequence != 0 && feed.Sequence != _lastFeed)
            {
                _lastFeed = feed.Sequence;
                double fed = feed.PctOf(snapshot.PlayerCarIdx);
                Add(string.Join(";", new[]
                {
                    (feed.TakenSeconds - _startUtc).ToString("0.000", CultureInfo.InvariantCulture),
                    "feed",
                    feed.Sequence.ToString(CultureInfo.InvariantCulture),
                    string.Empty, string.Empty,
                    fed.ToString("0.000000", CultureInfo.InvariantCulture),
                    string.Empty, string.Empty, string.Empty, string.Empty
                }));
            }

            StandingsRow me = snapshot.Rows == null ? null : snapshot.Rows.FirstOrDefault(r => r.IsPlayer || r.CarIdx == snapshot.PlayerCarIdx);
            double pct = me == null ? -1 : me.LapDistPct;
            if (snapshot.Sequence == _lastSequence && Math.Abs(pct - _lastPct) < 1e-12)
            {
                return;     // nothing new
            }

            _lastSequence = snapshot.Sequence;
            _lastPct = pct;
            Add(string.Join(";", new[]
            {
                now.ToString("0.000", CultureInfo.InvariantCulture),
                "data",
                snapshot.Sequence.ToString(CultureInfo.InvariantCulture),
                snapshot.Game ?? string.Empty,
                TrackQuery.From(snapshot).Key,
                pct.ToString("0.000000", CultureInfo.InvariantCulture),
                me == null ? string.Empty : me.LapsCompleted.ToString(CultureInfo.InvariantCulture),
                me == null ? string.Empty : (me.OnPitRoad ? "1" : "0"),
                me == null ? string.Empty : me.TrackSurface.ToString(CultureInfo.InvariantCulture),
                string.Empty
            }));
        }

        /// <summary>Where the Track Map drew the own car in this frame (called by the overlay while recording).</summary>
        public static void Frame(double pct, double delay)
        {
            if (!IsRecording)
            {
                return;
            }

            Add(string.Join(";", new[]
            {
                Clock.Elapsed.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture),
                "frame",
                string.Empty, string.Empty, string.Empty,
                pct.ToString("0.000000", CultureInfo.InvariantCulture),
                string.Empty, string.Empty, string.Empty,
                (delay * 1000).ToString("0", CultureInfo.InvariantCulture)
            }));
        }

        /// <summary>Ends a running record early and saves it.</summary>
        public static void Stop()
        {
            if (IsRecording)
            {
                Finish();
            }
        }

        private static bool Save()
        {
            if (_path == null)
            {
                return false;
            }

            try
            {
                lock (Lines)
                {
                    File.WriteAllLines(_path, Lines, Encoding.UTF8);
                }

                return true;
            }
            catch (Exception ex)
            {
                PluginLog.Info("Track Map: the position record could not be saved: " + ex.Message);
                return false;
            }
        }

        private static void Finish()
        {
            _timer.Stop();
            _probing = false;
            CompositionTarget.Rendering -= OnRender;
            if (Save())
            {
                LastFile = _path;
                PluginLog.Info("Track Map: position recorded in " + _path);
            }

            lock (Lines)
            {
                Lines.Clear();
            }
        }
    }
}
