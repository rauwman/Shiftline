using System;
using System.Globalization;
using System.Text;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Shiftline.Plugin
{
    /// <summary>The Overlays page. For now it shows what the data layer reads, so the values can be checked.</summary>
    public partial class OverlaysPage : UserControl
    {
        private DispatcherTimer _timer;

        public OverlaysPage()
        {
            InitializeComponent();
        }

        public OverlaysPage(Shiftline plugin) : this()
        {
            Plugin = plugin;
            Loaded += (sender, args) => Start();
            Unloaded += (sender, args) => Stop();
        }

        public Shiftline Plugin { get; }

        private void Start()
        {
            if (_timer == null)
            {
                _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                _timer.Tick += (sender, args) => Refresh();
            }

            Refresh();
            _timer.Start();
        }

        private void Stop()
        {
            if (_timer != null)
            {
                _timer.Stop();
            }
        }

        private void Refresh()
        {
            StandingsSnapshot snapshot = OverlayData.Current;
            string game = Plugin.PluginManager == null ? null : Plugin.PluginManager.GetPropertyValue("DataCorePlugin.CurrentGame") as string;

            StringBuilder summary = new StringBuilder();
            summary.AppendLine("Game".PadRight(22) + (string.IsNullOrEmpty(game) ? "-" : game));
            if (!string.Equals(game, "IRacing", StringComparison.OrdinalIgnoreCase))
            {
                summary.Append("Overlays read iRacing only. Start iRacing and join a session.");
            }
            else if (!snapshot.HasData)
            {
                summary.AppendLine("Standings".PadRight(22) + "no data yet (iRacing session not loaded, or the driver list is empty)");
                AppendDiagnostics(summary, snapshot);
            }
            else
            {
                double age = (DateTime.UtcNow.Ticks - snapshot.TakenUtcTicks) / (double)TimeSpan.TicksPerSecond;
                summary.AppendLine("Track".PadRight(22) + (snapshot.Track.Length == 0 ? "-" : snapshot.Track));
                summary.AppendLine("Session".PadRight(22) + (snapshot.SessionType.Length == 0 ? "-" : snapshot.SessionType));
                summary.AppendLine("Cars".PadRight(22) + snapshot.Rows.Length);
                summary.AppendLine("Own car index".PadRight(22) + snapshot.PlayerCarIdx);
                summary.AppendLine("Snapshot".PadRight(22) + "#" + snapshot.Sequence + ", " + age.ToString("0.0", CultureInfo.InvariantCulture) + " s old");
                AppendDiagnostics(summary, snapshot);
            }

            SummaryText.Text = summary.ToString().TrimEnd();
            TableText.Text = Table(snapshot);
        }

        private static void AppendDiagnostics(StringBuilder text, StandingsSnapshot snapshot)
        {
            foreach (string line in snapshot.Diagnostics)
            {
                text.AppendLine(line);
            }
        }

        private static string Table(StandingsSnapshot snapshot)
        {
            if (!snapshot.HasData)
            {
                return "-";
            }

            StringBuilder text = new StringBuilder();
            text.AppendLine("Pos Cls  #     Name                   Class     Lap  Dist%  Last      Best      Pit  iR");
            foreach (StandingsRow row in snapshot.Rows)
            {
                text.Append((row.Position > 0 ? row.Position.ToString(CultureInfo.InvariantCulture) : "-").PadRight(4));
                text.Append((row.ClassPosition > 0 ? row.ClassPosition.ToString(CultureInfo.InvariantCulture) : "-").PadRight(5));
                text.Append(Cut(row.Number, 5).PadRight(6));
                text.Append(Cut(row.Name, 21).PadRight(23));
                text.Append(Cut(row.ClassName, 8).PadRight(10));
                text.Append(row.LapsCompleted.ToString(CultureInfo.InvariantCulture).PadRight(5));
                text.Append((row.LapDistPct < 0 ? "-" : (row.LapDistPct * 100).ToString("0.0", CultureInfo.InvariantCulture)).PadRight(7));
                text.Append(Lap(row.LastLap).PadRight(10));
                text.Append(Lap(row.BestLap).PadRight(10));
                text.Append((row.OnPitRoad ? "yes" : string.Empty).PadRight(5));
                text.Append(row.IRating);
                text.AppendLine(row.IsPlayer ? "  <- you" : string.Empty);
            }

            return text.ToString().TrimEnd();
        }

        private static string Cut(string value, int length)
        {
            value = value ?? string.Empty;
            return value.Length <= length ? value : value.Substring(0, length);
        }

        private static string Lap(double seconds)
        {
            if (seconds <= 0)
            {
                return "-";
            }

            int minutes = (int)(seconds / 60);
            return minutes + ":" + (seconds - minutes * 60).ToString("00.000", CultureInfo.InvariantCulture);
        }
    }
}
