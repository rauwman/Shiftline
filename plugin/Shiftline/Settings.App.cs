using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;

namespace Shiftline.Plugin
{
    /// <summary>
    /// Settings that belong to the plugin itself and are not read by the LED script (hand-written part of the
    /// Settings class; the script settings are generated into Settings.cs).
    /// </summary>
    public partial class Settings
    {
        private bool _updateProfiles = true;
        private bool _autoUpdate;
        private int _themesSeeded;
        private string _activeProfile = string.Empty;

        public Settings()
        {
            PropertyChanged += (sender, args) =>
            {
                string name = args.PropertyName;
                if (name == "SideLeds")
                {
                    SyncEffectPositions();
                }
                else if (name != null && name.StartsWith("Fx") && name.EndsWith("Custom"))
                {
                    SyncEffectPosition(name.Substring(2, name.Length - 8));
                }

                if (name == "LedFunction" || name == "PedalBrake" || name == "PedalThrottle" || name == "PedalClutch")
                {
                    UpdateFuelRules();
                }

                if (name == "LedFunction" || name == "BrakeArea")
                {
                    UpdateBrakeRules();
                }

                if (name == "NightMode" || name == "BrightnessGeneral" || name == "BrightnessIdle")
                {
                    Raise("NightModeNote");
                }

                if (name == "PedalBrake" || name == "PedalThrottle" || name == "PedalClutch")
                {
                    Raise("PedalPreset");
                }
            };
            SyncEffectPositions();
        }

        /// <summary>What night mode does to the brightness right now (the sliders keep their values).</summary>
        [JsonIgnore]
        public string NightModeNote
        {
            get
            {
                return "Night mode is on: the LEDs now use half of these values (general " + (BrightnessGeneral / 2) + " %, idle animation " + (BrightnessGeneral * BrightnessIdle / 200) + " % of full brightness). The sliders keep your values.";
            }
        }

        // ---------------------------------------------------------------- pedal presets

        /// <summary>The pedal presets: name, then the areas of brake, throttle and clutch.</summary>
        private static readonly string[][] PedalPresets =
        {
            new[] { "split", "halfLeft", "halfRight", "off" },
            new[] { "sidesTop", "centreEnds", "centreMiddle", "off" },
            new[] { "throttle", "off", "centreLeft", "off" },
            new[] { "brake", "centreLeft", "off", "off" },
            new[] { "three", "sideRight", "centreLeft", "sideLeft" }
        };

        /// <summary>The name of the pedal preset that the three areas match, or "custom".</summary>
        [JsonIgnore]
        public string PedalPreset
        {
            get
            {
                foreach (string[] preset in PedalPresets)
                {
                    if (preset[1] == PedalBrake && preset[2] == PedalThrottle && preset[3] == PedalClutch)
                    {
                        return preset[0];
                    }
                }

                return "custom";
            }
        }

        /// <summary>Applies a pedal preset. "custom" clears all three areas, unless they are custom already.</summary>
        public void ApplyPedalPreset(string name)
        {
            if (name == "custom")
            {
                if (PedalPreset != "custom")
                {
                    PedalBrake = "off";
                    PedalThrottle = "off";
                    PedalClutch = "off";
                }

                Raise("PedalPreset");
                return;
            }

            foreach (string[] preset in PedalPresets)
            {
                if (preset[0] == name)
                {
                    PedalBrake = preset[1];
                    PedalThrottle = preset[2];
                    PedalClutch = preset[3];
                }
            }

            Raise("PedalPreset");
        }

        // ---------------------------------------------------------------- the brake bar on the top row

        /// <summary>The brake bar can use the top row (the centre LEDs) only with the function "brake pressure only"; with the rev bar it uses the sides.</summary>
        public bool BrakeTopAllowed
        {
            get { return LedFunction == "brakeOnly"; }
        }

        /// <summary>On the side LEDs the bar fills inwards or outwards only; left to right and right to left need the top row.</summary>
        public bool BrakeOnTop
        {
            get { return BrakeArea == "top"; }
        }

        public void UpdateBrakeRules()
        {
            Raise("BrakeTopAllowed");
            Raise("BrakeOnTop");
            if (!BrakeTopAllowed && BrakeArea == "top")
            {
                BrakeArea = "both";
            }

            if (BrakeArea != "top" && (BrakeDirection == "leftRight" || BrakeDirection == "rightLeft"))
            {
                BrakeDirection = "outIn";          // left to right only makes sense on the top row
            }
        }

        // ---------------------------------------------------------------- where the fuel level may be shown

        private static bool UsesCentre(string area)
        {
            return !string.IsNullOrEmpty(area) && (area.StartsWith("centre") || area.StartsWith("half"));
        }

        private static bool UsesLeft(string area)
        {
            return area == "sideLeft" || area == "sideBoth";
        }

        private static bool UsesRight(string area)
        {
            return area == "sideRight" || area == "sideBoth";
        }

        private bool PedalsUse(Func<string, bool> test)
        {
            return LedFunction == "brakeThrottle" && (test(PedalBrake) || test(PedalThrottle) || test(PedalClutch));
        }

        /// <summary>The centre LEDs are free for the fuel level unless the chosen LED function or a pedal uses them.</summary>
        public bool FuelCentreAllowed
        {
            get { return LedFunction != "rpmBrake" && LedFunction != "rpmOnly" && !PedalsUse(UsesCentre); }
        }

        /// <summary>Both side columns are free for the fuel level.</summary>
        public bool FuelSidesAllowed
        {
            get { return FuelLeftAllowed && FuelRightAllowed; }
        }

        public bool FuelLeftAllowed
        {
            get { return !PedalsUse(UsesLeft); }
        }

        public bool FuelRightAllowed
        {
            get { return !PedalsUse(UsesRight); }
        }

        /// <summary>Tells the page which fuel areas are available now and switches the fuel level off when its area is no longer allowed.</summary>
        public void UpdateFuelRules()
        {
            Raise("FuelCentreAllowed");
            Raise("FuelSidesAllowed");
            Raise("FuelLeftAllowed");
            Raise("FuelRightAllowed");
            bool allowed = FuelPlacement == "off" || string.IsNullOrEmpty(FuelPlacement)
                || (FuelPlacement == "centre" && FuelCentreAllowed)
                || (FuelPlacement == "sides" && FuelSidesAllowed)
                || (FuelPlacement == "left" && FuelLeftAllowed)
                || (FuelPlacement == "right" && FuelRightAllowed);
            if (!allowed)
            {
                FuelPlacement = "off";
            }
        }

        /// <summary>
        /// While an effect does not use a custom position, its start and count show the standard position for the current
        /// number of LEDs per side, so switching the custom position on starts from there.
        /// </summary>
        public void SyncEffectPositions()
        {
            foreach (string name in EffectPositions.Keys)
            {
                SyncEffectPosition(name);
            }
        }

        private void SyncEffectPosition(string effect)
        {
            int[][] table;
            PropertyInfo custom = typeof(Settings).GetProperty("Fx" + effect + "Custom");
            if (custom == null || !EffectPositions.TryGetValue(effect, out table) || (bool)custom.GetValue(this, null))
            {
                return;
            }

            int[] position = table[Math.Max(1, Math.Min(table.Length, SideLeds)) - 1];
            typeof(Settings).GetProperty("Fx" + effect + "Start").SetValue(this, position[0], null);
            typeof(Settings).GetProperty("Fx" + effect + "Count").SetValue(this, position[1], null);
        }

        /// <summary>One line of the priority list: Rank 1 is the highest priority.</summary>
        public class PriorityItem
        {
            public string Id { get; set; }
            public int Rank { get; set; }
            public string Label { get; set; }
            public string Display { get { return Rank + ".  " + Label; } }
        }

        /// <summary>The order of the effects, lowest priority first (same rules as the LED script: invalid entries are ignored, missing ones are put back).</summary>
        public List<string> EffectiveLayerOrder()
        {
            List<string> known = LayerDefs.Select(d => d[0]).ToList();
            List<string> order = new List<string>();
            foreach (string part in (LayerOrder ?? string.Empty).Split(','))
            {
                string id = part.Trim();
                if (known.Contains(id) && !order.Contains(id))
                {
                    order.Add(id);
                }
            }

            for (int i = 0; i < known.Count; i++)
            {
                if (order.Contains(known[i]))
                {
                    continue;
                }

                int at = 0;
                for (int j = i - 1; j >= 0; j--)
                {
                    int pos = order.IndexOf(known[j]);
                    if (pos >= 0)
                    {
                        at = pos + 1;
                        break;
                    }
                }

                order.Insert(at, known[i]);
            }

            return order;
        }

        /// <summary>The priority list as shown on the Settings page: highest priority first.</summary>
        public List<PriorityItem> GetPriorityItems()
        {
            List<string> order = EffectiveLayerOrder();
            List<PriorityItem> items = new List<PriorityItem>();
            for (int i = order.Count - 1; i >= 0; i--)
            {
                string id = order[i];
                items.Add(new PriorityItem { Id = id, Rank = order.Count - i, Label = LayerDefs.First(d => d[0] == id)[1] });
            }

            return items;
        }

        /// <summary>Moves an effect one place up (higher priority, direction +1) or down (direction -1).</summary>
        public void MovePriority(string id, int direction)
        {
            List<string> order = EffectiveLayerOrder();
            int index = order.IndexOf(id);
            int target = index + direction;
            if (index < 0 || target < 0 || target >= order.Count)
            {
                return;
            }

            order.RemoveAt(index);
            order.Insert(target, id);
            string standard = string.Join(",", LayerDefs.Select(d => d[0]));
            string text = string.Join(",", order);
            LayerOrder = text == standard ? string.Empty : text;
        }

        /// <summary>Back to the standard priority order.</summary>
        public void ResetPriority()
        {
            LayerOrder = string.Empty;
        }

        /// <summary>Receive updated LED profiles (the update check itself comes with the public release).</summary>
        public bool UpdateProfiles
        {
            get { return _updateProfiles; }
            set
            {
                if (_updateProfiles != value)
                {
                    _updateProfiles = value;
                    Raise("UpdateProfiles");
                }
            }
        }

        /// <summary>Install plugin updates automatically (the update check itself comes with the public release).</summary>
        public bool AutoUpdate
        {
            get { return _autoUpdate; }
            set
            {
                if (_autoUpdate != value)
                {
                    _autoUpdate = value;
                    Raise("AutoUpdate");
                }
            }
        }

        /// <summary>Name of the settings profile that was loaded or saved last (shown at the top of the LED panel); empty when none.</summary>
        public string ActiveProfile
        {
            get { return _activeProfile; }
            set
            {
                string text = value ?? string.Empty;
                if (_activeProfile != text)
                {
                    _activeProfile = text;
                    Raise("ActiveProfile");
                }
            }
        }

        /// <summary>Which generation of starter themes has been added to the theme list (see EnsureThemes).</summary>
        public int ThemesSeeded
        {
            get { return _themesSeeded; }
            set
            {
                if (_themesSeeded != value)
                {
                    _themesSeeded = value;
                    Raise("ThemesSeeded");
                }
            }
        }

        /// <summary>
        /// Copies every setting and the colour themes of another set into this object. Used to load a settings profile
        /// or a backup: the page keeps its bindings because this object stays the same.
        /// </summary>
        public void CopyFrom(Settings other)
        {
            foreach (PropertyInfo property in typeof(Settings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Type type = property.PropertyType;
                bool simple = type == typeof(string) || type == typeof(int) || type == typeof(bool);
                if (simple && property.CanRead && property.CanWrite)
                {
                    property.SetValue(this, property.GetValue(other, null), null);
                }
            }

            Themes.Clear();
            foreach (ColourTheme theme in other.Themes)
            {
                Themes.Add(theme);
            }

            EnsureThemes();
        }
    }
}
