using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace Shiftline.Plugin
{
    /// <summary>
    /// General overlay settings, kept in their own small file (Overlays.json next to the other plugin data).
    /// For now only the update rate: one global value, and an optional value per overlay that overrides it.
    /// </summary>
    public static class OverlaySettings
    {
        public static readonly int[] Rates = { 5, 10, 20, 30, 60 };
        public const int DefaultRate = 10;

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, int> Overrides = new Dictionary<string, int>();
        private static int _globalRate = DefaultRate;

        static OverlaySettings()
        {
            Load();
        }

        private static string FilePath
        {
            get { return Path.Combine(ProfileStore.RootFolder, "Overlays.json"); }
        }

        /// <summary>How often bars and numbers update, per second, when an overlay has no value of its own.</summary>
        public static int GlobalRate
        {
            get { lock (Gate) { return _globalRate; } }
            set
            {
                lock (Gate)
                {
                    _globalRate = Clamp(value);
                }

                Save();
            }
        }

        /// <summary>The rate an overlay really uses: its own value, or the global one.</summary>
        public static int RateFor(string overlay)
        {
            lock (Gate)
            {
                int own;
                return Overrides.TryGetValue(overlay, out own) ? own : _globalRate;
            }
        }

        /// <summary>The overlay's own rate, or 0 when it follows the global one.</summary>
        public static int OverrideFor(string overlay)
        {
            lock (Gate)
            {
                int own;
                return Overrides.TryGetValue(overlay, out own) ? own : 0;
            }
        }

        /// <summary>Sets the overlay's own rate; 0 makes it follow the global rate again.</summary>
        public static void SetOverride(string overlay, int rate)
        {
            lock (Gate)
            {
                if (rate <= 0)
                {
                    Overrides.Remove(overlay);
                }
                else
                {
                    Overrides[overlay] = Clamp(rate);
                }
            }

            Save();
        }

        private static int Clamp(int rate)
        {
            return Math.Max(1, Math.Min(60, rate));
        }

        private static void Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    return;
                }

                JObject json = JObject.Parse(File.ReadAllText(FilePath));
                JToken global = json["GlobalRate"];
                if (global != null && global.Type == JTokenType.Integer)
                {
                    _globalRate = Clamp((int)global);
                }

                JObject own = json["Rates"] as JObject;
                if (own != null)
                {
                    foreach (JProperty property in own.Properties())
                    {
                        if (property.Value.Type == JTokenType.Integer)
                        {
                            Overrides[property.Name] = Clamp((int)property.Value);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                PluginLog.Info("Overlay settings could not be read, using the defaults: " + ex.Message);
            }
        }

        private static void Save()
        {
            try
            {
                JObject own = new JObject();
                int global;
                lock (Gate)
                {
                    global = _globalRate;
                    foreach (KeyValuePair<string, int> pair in Overrides)
                    {
                        own[pair.Key] = pair.Value;
                    }
                }

                Directory.CreateDirectory(ProfileStore.RootFolder);
                File.WriteAllText(FilePath, new JObject { { "GlobalRate", global }, { "Rates", own } }.ToString());
            }
            catch (Exception ex)
            {
                PluginLog.Info("Overlay settings could not be saved: " + ex.Message);
            }
        }
    }
}
