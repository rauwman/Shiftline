using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Shiftline.Plugin
{
    /// <summary>How our track shape lines up with the game's lap: the shift of the start and how well SimHub's map fits.</summary>
    internal sealed class TrackAlignmentResult
    {
        public static readonly TrackAlignmentResult None = new TrackAlignmentResult { State = "none" };
        public static readonly TrackAlignmentResult Pending = new TrackAlignmentResult { State = "looking" };

        public string State = "none";       // looking, none (no SimHub map), found, no fit
        public string Source = string.Empty; // own record, cloud
        public string File = string.Empty;
        public double Offset;               // add to the game's LapDistPct to get ours
        public double Fit = double.NaN;     // remaining difference of the shapes, as a part of the track's size

        public bool Usable
        {
            get { return State == "found"; }
        }
    }

    /// <summary>
    /// Lines our track shape up with the game. SimHub keeps a map of every track (its own recording in
    /// PluginsData\&lt;game&gt;\MapRecords, maps downloaded from SimHub's cloud in MapRecordsCloud): the car's position in the game
    /// world together with the game's own LapDistPct. Laying that line over our shape at every possible shift of the start shows
    /// where the game starts its lap. Runs in the background, once per track and when SimHub records a new map.
    /// </summary>
    internal static class TrackAlignment
    {
        private const int Samples = 200;
        private const double MaxFit = 0.06;             // a worse fit means: SimHub's map is another layout
        private static readonly TimeSpan Recheck = TimeSpan.FromSeconds(15);
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>(StringComparer.Ordinal);

        private sealed class Entry
        {
            public TrackAlignmentResult Result = TrackAlignmentResult.Pending;
            public string File = string.Empty;
            public DateTime FileTime;
            public DateTime Checked;
            public bool Busy;
        }

        /// <summary>The alignment for the running track and the chosen layout; Pending while it is worked out.</summary>
        public static TrackAlignmentResult For(StandingsSnapshot snapshot, TrackLayout layout)
        {
            if (snapshot == null || !snapshot.HasData || snapshot.IsSample || layout == null)
            {
                return TrackAlignmentResult.None;
            }

            string game = snapshot.Game ?? string.Empty;
            string code = SimHubCode(snapshot);
            if (game.Length == 0 || code.Length == 0)
            {
                return TrackAlignmentResult.None;
            }

            string key = game + "/" + code + "/" + layout.Id;
            Entry entry;
            lock (Gate)
            {
                if (!Entries.TryGetValue(key, out entry))
                {
                    entry = new Entry();
                    Entries[key] = entry;
                }

                if (entry.Busy || DateTime.UtcNow - entry.Checked < Recheck)
                {
                    return entry.Result;
                }

                entry.Busy = true;
                entry.Checked = DateTime.UtcNow;
            }

            double length = snapshot.TrackLengthM;
            Task.Run(() => Work(entry, game, code, length, layout));
            return entry.Result;
        }

        /// <summary>The name SimHub gives the track's map file: the Track code, for iRacing "&lt;track&gt;-&lt;config&gt;".</summary>
        public static string SimHubCode(StandingsSnapshot snapshot)
        {
            if (string.Equals(snapshot.Game, "IRacing", StringComparison.OrdinalIgnoreCase))
            {
                string name = (snapshot.TrackName ?? string.Empty).Trim();
                string config = (snapshot.TrackConfig ?? string.Empty).Trim();
                return config.Length == 0 ? name : name + "-" + config;
            }

            return (snapshot.TrackCode ?? string.Empty).Trim();
        }

        private static void Work(Entry entry, string game, string code, double length, TrackLayout layout)
        {
            try
            {
                string source;
                string file = FindMap(game, code, length, out source);
                if (file == null)
                {
                    entry.Result = new TrackAlignmentResult { State = "none" };
                    return;
                }

                DateTime time = System.IO.File.GetLastWriteTimeUtc(file);
                if (file == entry.File && time == entry.FileTime && entry.Result != TrackAlignmentResult.Pending)
                {
                    return;     // the same map as before: nothing to do
                }

                TrackAlignmentResult result = Align(ReadMap(file), layout);
                result.Source = source;
                result.File = file;
                entry.File = file;
                entry.FileTime = time;
                entry.Result = result;
            }
            catch (Exception ex)
            {
                entry.Result = new TrackAlignmentResult { State = "none" };
                PluginLog.Info("Track Map: SimHub map for " + code + " could not be read: " + ex.Message);
            }
            finally
            {
                entry.Busy = false;
            }
        }

        /// <summary>SimHub's own recording first, otherwise the cloud map whose length is closest to the game's.</summary>
        private static string FindMap(string game, string code, double length, out string source)
        {
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PluginsData", game);
            string own = Path.Combine(folder, "MapRecords", code + ".shtl");
            if (System.IO.File.Exists(own))
            {
                source = "own record";
                return own;
            }

            source = "cloud";
            string cloud = Path.Combine(folder, "MapRecordsCloud");
            if (!Directory.Exists(cloud))
            {
                return null;
            }

            string prefix = code + "-";
            List<string> files = Directory.GetFiles(cloud, "*.shtl")
                .Where(f => Path.GetFileNameWithoutExtension(f).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && IsNumber(Path.GetFileNameWithoutExtension(f).Substring(prefix.Length)))
                .ToList();
            if (files.Count == 0)
            {
                return null;
            }

            return files.OrderBy(f => length > 0 ? Math.Abs(Number(Path.GetFileNameWithoutExtension(f).Substring(prefix.Length)) - length) : 0)
                .ThenByDescending(f => System.IO.File.GetLastWriteTimeUtc(f))
                .First();
        }

        private static bool IsNumber(string text)
        {
            double value;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static double Number(string text)
        {
            double value;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : 0;
        }

        /// <summary>The map's points: game LapDistPct with the position in the game world (x and z, the ground plane).</summary>
        private static List<double[]> ReadMap(string path)
        {
            byte[] raw = System.IO.File.ReadAllBytes(path);
            Stream stream = new MemoryStream(raw);
            if (raw.Length > 2 && raw[0] == 0x1F && raw[1] == 0x8B)
            {
                stream = new GZipStream(stream, CompressionMode.Decompress);
            }

            JObject map;
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            using (JsonTextReader json = new JsonTextReader(reader))
            {
                map = JObject.Load(json);
            }

            List<double[]> points = new List<double[]>();
            bool withPct = true;
            foreach (JObject item in (map["CarCoordinates"] as JArray ?? new JArray()).OfType<JObject>())
            {
                JArray value = item["Value"] as JArray;
                if (value == null || value.Count < 3)
                {
                    continue;
                }

                double? p = (double?)item["p"];
                withPct &= p.HasValue;
                points.Add(new[] { p ?? 0, (double)value[0], (double)value[2] });
            }

            if (!withPct && points.Count > 1)
            {
                // older SimHub maps have no "p": the recording is one lap from the game's line, so the distance driven gives it
                double total = 0;
                double[] along = new double[points.Count];
                for (int i = 1; i < points.Count; i++)
                {
                    total += Math.Sqrt(Math.Pow(points[i][1] - points[i - 1][1], 2) + Math.Pow(points[i][2] - points[i - 1][2], 2));
                    along[i] = total;
                }

                for (int i = 0; i < points.Count; i++)
                {
                    points[i][0] = total > 0 ? along[i] / total : 0;
                }
            }

            return points.Where(m => m[0] >= 0 && m[0] < 1).ToList();
        }

        /// <summary>
        /// Lays SimHub's line over our shape at every shift of the start and keeps the best: per shift the shapes are matched by
        /// the best turn, scale and (if needed) mirror, then the remaining distance is measured.
        /// </summary>
        private static TrackAlignmentResult Align(List<double[]> map, TrackLayout layout)
        {
            if (map.Count < 50)
            {
                return new TrackAlignmentResult { State = "none" };
            }

            map.Sort((a, b) => a[0].CompareTo(b[0]));
            Vector[] game = new Vector[Samples];
            for (int k = 0; k < Samples; k++)
            {
                game[k] = Interpolate(map, k / (double)Samples);
            }

            double bestFit = double.MaxValue;
            double bestShift = 0;
            for (double shift = -0.2; shift <= 0.2; shift += 0.0025)
            {
                double fit = Fit(game, layout, shift);
                if (fit < bestFit)
                {
                    bestFit = fit;
                    bestShift = shift;
                }
            }

            double coarse = bestShift;
            for (double shift = coarse - 0.003; shift <= coarse + 0.003; shift += 0.0002)
            {
                double fit = Fit(game, layout, shift);
                if (fit < bestFit)
                {
                    bestFit = fit;
                    bestShift = shift;
                }
            }

            return new TrackAlignmentResult
            {
                State = bestFit <= MaxFit ? "found" : "no fit",
                Offset = bestFit <= MaxFit ? bestShift : 0,
                Fit = bestFit
            };
        }

        private static Vector Interpolate(List<double[]> map, double p)
        {
            int hi = map.FindIndex(m => m[0] >= p);
            double[] b = hi < 0 ? map[0] : map[hi];
            double[] a = hi <= 0 ? map[map.Count - 1] : map[hi - 1];
            double pa = a[0];
            double pb = b[0];
            if (hi <= 0)
            {
                pa -= 1;            // wrap around the start
                if (p > pb)
                {
                    pb += 1;
                }
            }

            double t = pb - pa > 1e-9 ? (p - pa) / (pb - pa) : 0;
            return new Vector(a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t);
        }

        /// <summary>Remaining distance after the best similarity transform (turn, scale, mirror), relative to our shape's size.</summary>
        private static double Fit(Vector[] game, TrackLayout layout, double shift)
        {
            int n = game.Length;
            Vector[] ours = new Vector[n];
            Vector meanGame = new Vector();
            Vector meanOurs = new Vector();
            for (int k = 0; k < n; k++)
            {
                ours[k] = (Vector)LayoutAt(layout, k / (double)n + shift);
                meanGame += game[k];
                meanOurs += ours[k];
            }

            meanGame /= n;
            meanOurs /= n;
            double gameSize = 0;
            double oursSize = 0;
            double re = 0, im = 0, reMirror = 0, imMirror = 0;
            for (int k = 0; k < n; k++)
            {
                Vector a = game[k] - meanGame;
                Vector b = ours[k] - meanOurs;
                gameSize += a.LengthSquared;
                oursSize += b.LengthSquared;
                // complex numbers: sum of conj(a) * b, and of a * b for the mirrored case
                re += a.X * b.X + a.Y * b.Y;
                im += a.X * b.Y - a.Y * b.X;
                reMirror += a.X * b.X - a.Y * b.Y;
                imMirror += a.X * b.Y + a.Y * b.X;
            }

            if (gameSize <= 0 || oursSize <= 0)
            {
                return double.MaxValue;
            }

            double best = Math.Max(re * re + im * im, reMirror * reMirror + imMirror * imMirror);
            double remaining = Math.Max(0, oursSize - best / gameSize);
            return Math.Sqrt(remaining / oursSize);
        }

        private static Point LayoutAt(TrackLayout layout, double pct)
        {
            Point[] points = layout.Points;
            int count = points.Length;
            double f = (pct - Math.Floor(pct)) * count;
            int i = (int)Math.Floor(f);
            double t = f - i;
            Point a = points[i % count];
            Point b = points[(i + 1) % count];
            return a + (b - a) * t;
        }
    }
}
