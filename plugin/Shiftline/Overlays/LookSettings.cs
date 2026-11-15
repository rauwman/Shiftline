using System.Collections.Generic;
using System.Linq;

namespace Shiftline.Plugin
{
    /// <summary>
    /// The look settings that are set once for all overlays (page "General"): the opacity of the rows and of the header and footer,
    /// the font, its weight and the shape of the boxes. Every overlay page offers the same entries with "Same as in General" in
    /// front; an overlay that has it chosen takes the value from General, otherwise it uses its own. The values of General live
    /// in Overlays.json like the options of an overlay, under the name "General".
    /// </summary>
    internal static class LookSettings
    {
        public const string GeneralKey = "General";
        public const string FollowGeneral = "Global";

        private static readonly string[] OpacityValues =
        {
            "100", "100 %", "90", "90 %", "80", "80 %", "70", "70 %", "60", "60 %", "50", "50 %", "40", "40 %", "30", "30 %", "20", "20 %", "0", "0 % (no background)"
        };

        private static readonly string[] FontValues =
        {
            "Segoe UI", "Segoe UI", "Saira SemiCondensed", "Saira Semi Condensed", "Inter", "Inter", "Arial", "Arial", "Verdana", "Verdana", "Tahoma", "Tahoma", "Consolas", "Consolas"
        };

        private static readonly string[] WeightValues = { "Normal", "Normal", "SemiBold", "Semi-bold", "Bold", "Bold" };

        private static readonly string[] CornerValues = { "Rounded", "Rounded", "Square", "Square" };

        /// <summary>The choices as General offers them, with the value every overlay gets while it follows General.</summary>
        public static readonly StandingsChoice[] General =
        {
            new StandingsChoice("Look", "Opacity", "Background opacity of the rows and cards", "60", OpacityValues),
            new StandingsChoice("Look", "HfOpacity", "Background opacity of the header and footer", "0", OpacityValues),
            new StandingsChoice("Look", "Font", "Font", "Segoe UI", FontValues),
            new StandingsChoice("Look", "Weight", "Font weight", "Normal", WeightValues),
            new StandingsChoice("Look", "Corners", "Background boxes", "Rounded", CornerValues)
        };

        /// <summary>The same choice for the page of one overlay: "Same as in General" first, then the values.</summary>
        public static StandingsChoice ForOverlay(string key, string label, string fallback)
        {
            StandingsChoice general = General.First(c => c.Key == key);
            List<string> pairs = new List<string> { FollowGeneral, "Same as in General" };
            for (int i = 0; i < general.Values.Length; i++)
            {
                pairs.Add(general.Values[i]);
                pairs.Add(general.Labels[i]);
            }

            return new StandingsChoice("Look", key, label, fallback, pairs.ToArray());
        }

        /// <summary>The value an overlay really uses: its own, or the one of General when it follows General.</summary>
        public static string Resolve(string key, string value)
        {
            if (value != FollowGeneral)
            {
                return value;
            }

            StandingsChoice general = General.FirstOrDefault(c => c.Key == key);
            if (general == null)
            {
                return value;
            }

            string chosen = OverlaySettings.GetOption(GeneralKey, key, general.Fallback);
            return general.Values.Contains(chosen) ? chosen : general.Fallback;
        }

        /// <summary>The value set in General (the default while nothing was chosen there).</summary>
        public static string GeneralValue(string key)
        {
            return Resolve(key, FollowGeneral);
        }

        public static void SetGeneral(string key, string value)
        {
            OverlaySettings.SetOption(GeneralKey, key, value);
        }
    }
}
