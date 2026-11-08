using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace Shiftline.Plugin
{
    /// <summary>Where an overlay sits: the screen's device name, and position and size as parts (0..1) of that screen.</summary>
    public sealed class OverlayPlacement
    {
        public string Screen { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double W { get; set; }
        public double H { get; set; }

        public OverlayPlacement Copy()
        {
            return new OverlayPlacement { Screen = Screen, X = X, Y = Y, W = W, H = H };
        }
    }

    /// <summary>
    /// General overlay settings, kept in their own small file (Overlays.json next to the other plugin data).
    /// The update rate (one global value, and an optional value per overlay that overrides it), which overlays are switched on,
    /// and where each overlay sits. Later the switches and positions move into the presets.
    /// </summary>
    public static class OverlaySettings
    {
        public static readonly int[] Rates = { 5, 10, 20, 30, 60 };
        public const int DefaultRate = 10;

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, int> Overrides = new Dictionary<string, int>();
        private static readonly Dictionary<string, OverlayPlacement> Placements = new Dictionary<string, OverlayPlacement>();
        private static readonly HashSet<string> EnabledOverlays = new HashSet<string>();
        private static int _globalRate = DefaultRate;
        private static bool _onlyWhenGameInFront = true;

        /// <summary>Overlays show only while the game window is in front (edit mode shows them anyway). Default on.</summary>
        public static bool OnlyWhenGameInFront
        {
            get { lock (Gate) { return _onlyWhenGameInFront; } }
            set
            {
                lock (Gate)
                {
                    _onlyWhenGameInFront = value;
                }

                Save();
                RaiseChanged();
            }
        }

        /// <summary>Raised after a switch, a rate or the positions changed (not when one overlay was moved). Any thread.</summary>
        public static event Action Changed;

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
                RaiseChanged();
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
            RaiseChanged();
        }

        public static bool IsEnabled(string overlay)
        {
            lock (Gate)
            {
                return EnabledOverlays.Contains(overlay);
            }
        }

        public static void SetEnabled(string overlay, bool enabled)
        {
            lock (Gate)
            {
                if (enabled)
                {
                    EnabledOverlays.Add(overlay);
                }
                else
                {
                    EnabledOverlays.Remove(overlay);
                }
            }

            Save();
            RaiseChanged();
        }

        /// <summary>A copy of the saved position, or null when the overlay was never placed.</summary>
        public static OverlayPlacement GetPlacement(string overlay)
        {
            lock (Gate)
            {
                OverlayPlacement placement;
                return Placements.TryGetValue(overlay, out placement) ? placement.Copy() : null;
            }
        }

        public static void SetPlacement(string overlay, OverlayPlacement placement)
        {
            lock (Gate)
            {
                Placements[overlay] = placement.Copy();
            }

            Save();
        }

        public static void ResetPlacements()
        {
            lock (Gate)
            {
                Placements.Clear();
            }

            Save();
            RaiseChanged();
        }

        private static void RaiseChanged()
        {
            Action handler = Changed;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler();
            }
            catch (Exception ex)
            {
                PluginLog.Info("Overlay settings: " + ex.Message);
            }
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

                JToken front = json["OnlyWhenGameInFront"];
                if (front != null && front.Type == JTokenType.Boolean)
                {
                    _onlyWhenGameInFront = (bool)front;
                }

                JArray enabled = json["Enabled"] as JArray;
                if (enabled != null)
                {
                    foreach (JToken name in enabled)
                    {
                        if (name.Type == JTokenType.String)
                        {
                            EnabledOverlays.Add((string)name);
                        }
                    }
                }

                JObject placements = json["Placements"] as JObject;
                if (placements != null)
                {
                    foreach (JProperty property in placements.Properties())
                    {
                        JObject value = property.Value as JObject;
                        if (value != null)
                        {
                            Placements[property.Name] = new OverlayPlacement
                            {
                                Screen = (string)value["Screen"],
                                X = (double?)value["X"] ?? 0,
                                Y = (double?)value["Y"] ?? 0,
                                W = (double?)value["W"] ?? 0,
                                H = (double?)value["H"] ?? 0
                            };
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
                JArray enabled = new JArray();
                JObject placements = new JObject();
                int global;
                bool front;
                lock (Gate)
                {
                    global = _globalRate;
                    front = _onlyWhenGameInFront;
                    foreach (KeyValuePair<string, int> pair in Overrides)
                    {
                        own[pair.Key] = pair.Value;
                    }

                    foreach (string name in EnabledOverlays)
                    {
                        enabled.Add(name);
                    }

                    foreach (KeyValuePair<string, OverlayPlacement> pair in Placements)
                    {
                        placements[pair.Key] = new JObject
                        {
                            { "Screen", pair.Value.Screen }, { "X", pair.Value.X }, { "Y", pair.Value.Y }, { "W", pair.Value.W }, { "H", pair.Value.H }
                        };
                    }
                }

                Directory.CreateDirectory(ProfileStore.RootFolder);
                File.WriteAllText(FilePath, new JObject
                {
                    { "GlobalRate", global }, { "OnlyWhenGameInFront", front }, { "Rates", own }, { "Enabled", enabled }, { "Placements", placements }
                }.ToString());
            }
            catch (Exception ex)
            {
                PluginLog.Info("Overlay settings could not be saved: " + ex.Message);
            }
        }
    }
}
