using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace Shiftline.Plugin
{
    /// <summary>
    /// The colour themes of the plugin (hand-written part of the Settings class; the simple settings are
    /// generated into Settings.cs). The list is saved with the other settings.
    /// </summary>
    public partial class Settings
    {
        public const string DefaultRpmColours = "#01ABFF,#903AA1,#FF8C00,#D60000";
        public const string DefaultIdleColours = "#01ABFF,#903AA1,#FF8C00,#D60000";
        public const string DefaultFuelColours = "#FF0000,#00FF00";

        // The list starts empty on purpose: loading the saved settings adds the saved themes to it, so the
        // starter themes are only created afterwards by EnsureThemes() when nothing was saved.
        public ObservableCollection<ColourTheme> Themes { get; set; } = new ObservableCollection<ColourTheme>();

        /// <summary>The built-in colour themes, in the order they appear: id, name and colours.</summary>
        public static readonly string[][] BuiltInThemes =
        {
            new[] { "shiftline", "Shiftline", "#01ABFF", "#903AA1", "#FF8C00", "#D60000" },
            new[] { "classic", "Classic", "#00FF00", "#FFFF00", "#FF0000" },
            new[] { "ember", "Ember", "#FF8C00", "#FF0000", "#FF1493" },
            new[] { "ice", "Ice", "#00BFFF", "#1E90FF", "#FFFFFF" },
            new[] { "fuel", "Fuel", "#FF0000", "#00FF00" },
            new[] { "f1", "F1", "#00FF00", "#FF0000", "#0000FF" },
            new[] { "f1duo", "F1 Duo", "#0000FF", "#FF0000" }
        };

        public static bool IsBuiltInId(string id)
        {
            return BuiltInThemes.Any(theme => theme[0] == id);
        }

        /// <summary>
        /// Makes sure the built-in themes exist with their original names and colours (they cannot be deleted or changed),
        /// that they come first in the list, and that every selection points to an existing theme.
        /// </summary>
        public void EnsureThemes()
        {
            if (Themes == null)
            {
                Themes = new ObservableCollection<ColourTheme>();
            }

            for (int position = 0; position < BuiltInThemes.Length; position++)
            {
                string[] definition = BuiltInThemes[position];
                ColourTheme theme = Themes.FirstOrDefault(candidate => candidate.Id == definition[0]);
                if (theme == null)
                {
                    theme = new ColourTheme(definition[0], definition[1], definition.Skip(2).ToArray());
                    Themes.Insert(Math.Min(position, Themes.Count), theme);
                    continue;
                }

                theme.Name = definition[1];
                string[] colours = definition.Skip(2).ToArray();
                if (!theme.Stops.Select(stop => stop.Hex).SequenceEqual(colours))
                {
                    theme.Stops.Clear();
                    foreach (string colour in colours)
                    {
                        theme.Stops.Add(new ColourStop(colour));
                    }
                }

                int current = Themes.IndexOf(theme);
                if (current != position && position < Themes.Count)
                {
                    Themes.Move(current, position);
                }
            }

            ThemesSeeded = BuiltInThemes.Length;

            if (RpmRedline != "flash" && RpmRedline != "flashAlt" && RpmRedline != "flashSwap")
            {
                RpmRedline = "off";          // the old "light all LEDs" looked the same as nothing special
            }

            if (!Themes.Any(theme => theme.Id == RpmThemeId))
            {
                RpmThemeId = "shiftline";
            }

            if (!Themes.Any(theme => theme.Id == IdleThemeId))
            {
                IdleThemeId = "shiftline";
            }

            if (!Themes.Any(theme => theme.Id == FuelThemeId))
            {
                FuelThemeId = "fuel";
            }

            SyncEffectPositions();
            UpdateFuelRules();
            UpdateBrakeRules();
        }

        /// <summary>Restores all settings and the starter themes.</summary>
        public void ResetAll()
        {
            Themes.Clear();
            ResetToDefaults();
            EnsureThemes();
        }

        /// <summary>
        /// The colour list of a theme for the LED script ("#RRGGBB,#RRGGBB,..."), or the fallback when the theme does not
        /// exist or is not usable. Called from SimHub's data thread while the page may be editing, so it never throws.
        /// </summary>
        public string ResolveColours(string themeId, string fallback)
        {
            try
            {
                ColourTheme theme = Themes.ToArray().FirstOrDefault(candidate => candidate.Id == themeId);
                if (theme == null)
                {
                    return fallback;
                }

                string list = theme.ToColourList();
                int count = list.Split(',').Length;
                return count >= ColourTheme.MinStops && count <= ColourTheme.MaxStops ? list : fallback;
            }
            catch (System.Exception)
            {
                return fallback;
            }
        }
    }
}
