using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Shiftline.Plugin
{
    /// <summary>One source of track shapes as the manifest names it (for the credit line).</summary>
    internal sealed class TrackSource
    {
        public string Name;
        public string DisplayName;
        public string License;
        public string Url;
    }

    /// <summary>
    /// One track layout of the track data (format v1): the line in metres (x east, y north), index 0 at start/finish in the
    /// driving direction, the keys it is found by and the optional pit lane.
    /// </summary>
    internal sealed class TrackLayout
    {
        public string Id;
        public string Name;
        public string Config;
        public string Source;               // manifest source name, "User" for a file of the user
        public double LengthM;
        public bool Closed;
        public double? PresentRotation;     // the usual orientation, degrees clockwise on screen; null = draw north up
        public Point[] Points;
        public Point[] PitPoints;           // null = no pit lane
        public double PitEntryPct;
        public double PitExitPct;
        public string[] IRacingNames = new string[0];
        public string[] TrackCodes = new string[0];

        public string Title
        {
            get
            {
                string name = string.IsNullOrEmpty(Name) ? Readable(Id) : Name;
                return string.IsNullOrEmpty(Config) ? name : name + " (" + Config + ")";
            }
        }

        /// <summary>"spa_2024_combined" as "Spa 2024 combined", for layouts without a name.</summary>
        private static string Readable(string id)
        {
            string text = (id ?? string.Empty).Replace('_', ' ').Trim();
            return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        public static TrackLayout Parse(JObject json, string source)
        {
            TrackLayout layout = new TrackLayout
            {
                Id = (string)json["id"] ?? string.Empty,
                Name = (string)json["name"],
                Config = (string)json["config"],
                Source = source,
                LengthM = (double?)json["lengthM"] ?? 0,
                Closed = (bool?)json["closed"] ?? true,
                PresentRotation = (double?)json["presentRotation"],
                Points = ReadPoints(json["points"] as JArray)
            };

            JObject match = json["match"] as JObject;
            if (match != null)
            {
                layout.IRacingNames = Strings(match.SelectToken("iracing.trackNames") as JArray);
                layout.TrackCodes = Strings(match.SelectToken("simhub.trackCodes") as JArray).Select(c => c.Trim().ToLowerInvariant()).ToArray();
            }

            JObject pit = json["pit"] as JObject;
            if (pit != null)
            {
                Point[] pitPoints = ReadPoints(pit["points"] as JArray);
                double? entry = (double?)pit["entryPct"];
                double? exit = (double?)pit["exitPct"];
                if (pitPoints.Length >= 2 && entry.HasValue && exit.HasValue)
                {
                    layout.PitPoints = pitPoints;
                    layout.PitEntryPct = entry.Value;
                    layout.PitExitPct = exit.Value;
                }
            }

            return layout.Id.Length > 0 && layout.Points.Length >= 3 ? layout : null;
        }

        private static Point[] ReadPoints(JArray array)
        {
            if (array == null)
            {
                return new Point[0];
            }

            List<Point> points = new List<Point>(array.Count);
            foreach (JToken token in array)
            {
                JArray pair = token as JArray;
                if (pair != null && pair.Count >= 2)
                {
                    points.Add(new Point((double)pair[0], (double)pair[1]));
                }
            }

            return points.ToArray();
        }

        private static string[] Strings(JArray array)
        {
            return array == null ? new string[0] : array.Select(t => (string)t).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        }
    }

    /// <summary>
    /// The track shapes shipped with the plugin (data/tracks, zipped into the DLL at build time as "Shiftline.tracks.zip") plus
    /// the user's own files in PluginsData\Shiftline\Tracks, which win. Loaded once in the background; until then IsReady is
    /// false and the Track Map draws its line or ring.
    /// </summary>
    internal static class TrackLibrary
    {
        private const string ResourceName = "Shiftline.tracks.zip";
        private static readonly object Gate = new object();
        private static Task _loading;
        private static volatile TrackLayout[] _layouts;
        private static Dictionary<string, TrackSource> _sources = new Dictionary<string, TrackSource>(StringComparer.Ordinal);
        private static Dictionary<string, string> _mappings = new Dictionary<string, string>(StringComparer.Ordinal);

        public static bool IsReady
        {
            get { return _layouts != null; }
        }

        /// <summary>All layouts, or an empty list while they are loading (loading starts on the first call).</summary>
        public static TrackLayout[] Layouts
        {
            get
            {
                EnsureLoading();
                return _layouts ?? new TrackLayout[0];
            }
        }

        /// <summary>The user's corrections: match key to layout id.</summary>
        public static IDictionary<string, string> Mappings
        {
            get { return _mappings; }
        }

        public static string Problem { get; private set; }

        public static string UserFolder
        {
            get { return Path.Combine(ProfileStore.RootFolder, "Tracks"); }
        }

        public static TrackSource SourceOf(TrackLayout layout)
        {
            TrackSource source;
            return layout != null && _sources.TryGetValue(layout.Source ?? string.Empty, out source) ? source : null;
        }

        public static TrackLayout ById(string id)
        {
            return Layouts.FirstOrDefault(l => string.Equals(l.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public static void EnsureLoading()
        {
            lock (Gate)
            {
                if (_loading == null)
                {
                    _loading = Task.Run((Action)Load);
                }
            }
        }

        private static void Load()
        {
            Dictionary<string, TrackLayout> layouts = new Dictionary<string, TrackLayout>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, TrackSource> sources = new Dictionary<string, TrackSource>(StringComparer.Ordinal);
            try
            {
                LoadShipped(layouts, sources);
            }
            catch (Exception ex)
            {
                Problem = "The shipped track shapes could not be read: " + ex.Message;
                PluginLog.Info("Track Map: " + Problem);
            }

            try
            {
                LoadUser(layouts);
            }
            catch (Exception ex)
            {
                PluginLog.Info("Track Map: own track files could not be read: " + ex.Message);
            }

            _sources = sources;
            _layouts = layouts.Values.ToArray();
            PluginLog.Info("Track Map: " + _layouts.Length + " track shapes loaded.");
        }

        private static void LoadShipped(Dictionary<string, TrackLayout> layouts, Dictionary<string, TrackSource> sources)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
            {
                if (stream == null)
                {
                    Problem = "No track shapes in this build.";
                    return;
                }

                using (ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    // the zip may store "\" or "/" as separator, depending on the tool that made it
                    Dictionary<string, ZipArchiveEntry> entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
                    foreach (ZipArchiveEntry entry in zip.Entries)
                    {
                        entries[entry.FullName.Replace('\\', '/').TrimStart('/')] = entry;
                    }

                    ZipArchiveEntry manifestEntry;
                    if (!entries.TryGetValue("manifest.json", out manifestEntry))
                    {
                        Problem = "The track data has no manifest.";
                        return;
                    }

                    JObject manifest = ReadJson(manifestEntry.Open());
                    foreach (JObject source in (manifest["sources"] as JArray ?? new JArray()).OfType<JObject>())
                    {
                        string name = (string)source["name"] ?? string.Empty;
                        sources[name] = new TrackSource
                        {
                            Name = name,
                            DisplayName = (string)source["displayName"] ?? name,
                            License = (string)source["license"] ?? string.Empty,
                            Url = (string)source["url"] ?? (string)source["repo"] ?? string.Empty
                        };
                    }

                    foreach (JObject item in (manifest["layouts"] as JArray ?? new JArray()).OfType<JObject>())
                    {
                        // the path always comes from the manifest ("layouts/..." or "osm/layouts/..."), never from the id
                        string file = ((string)item["file"] ?? string.Empty).Replace('\\', '/').TrimStart('/');
                        ZipArchiveEntry entry;
                        if (file.Length == 0 || !entries.TryGetValue(file, out entry))
                        {
                            continue;       // a missing layout is normal: that track falls back to the line or the ring
                        }

                        try
                        {
                            TrackLayout layout = TrackLayout.Parse(ReadJson(entry.Open()), (string)item["source"] ?? string.Empty);
                            if (layout != null)
                            {
                                layouts[layout.Id] = layout;
                            }
                        }
                        catch (Exception ex)
                        {
                            PluginLog.Info("Track Map: layout " + file + " skipped: " + ex.Message);
                        }
                    }
                }
            }
        }

        /// <summary>The user's own layouts (any *.json with an "id" below Tracks\layouts) and corrections (Tracks\mappings.json).</summary>
        private static void LoadUser(Dictionary<string, TrackLayout> layouts)
        {
            string folder = UserFolder;
            string own = Path.Combine(folder, "layouts");
            if (Directory.Exists(own))
            {
                foreach (string path in Directory.GetFiles(own, "*.json", SearchOption.AllDirectories))
                {
                    try
                    {
                        TrackLayout layout = TrackLayout.Parse(ReadJson(File.OpenRead(path)), "User");
                        if (layout != null)
                        {
                            layouts[layout.Id] = layout;
                        }
                    }
                    catch (Exception ex)
                    {
                        PluginLog.Info("Track Map: own layout " + path + " skipped: " + ex.Message);
                    }
                }
            }

            string mappings = Path.Combine(folder, "mappings.json");
            if (File.Exists(mappings))
            {
                Dictionary<string, string> read = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(mappings, Encoding.UTF8));
                Dictionary<string, string> clean = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, string> pair in read ?? new Dictionary<string, string>())
                {
                    if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                    {
                        clean[pair.Key.Trim().ToLowerInvariant()] = pair.Value.Trim();
                    }
                }

                _mappings = clean;
            }
        }

        private static JObject ReadJson(Stream stream)
        {
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            using (JsonTextReader json = new JsonTextReader(reader))
            {
                return JObject.Load(json);
            }
        }
    }
}
