using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace Shiftline.Plugin
{
    /// <summary>
    /// Fonts for the overlays. Two open fonts (SIL Open Font License, see Assets/Fonts/OFL-*.txt) are built into the plugin, so
    /// they work on every PC; any other name is taken from the fonts installed in Windows.
    /// </summary>
    internal static class OverlayFonts
    {
        private static readonly Uri Folder = new Uri("pack://application:,,,/Shiftline.Plugin;component/Assets/Fonts/");
        private static readonly HashSet<string> BuiltIn = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Saira SemiCondensed", "Inter"
        };

        private static readonly Dictionary<string, FontFamily> Cache = new Dictionary<string, FontFamily>(StringComparer.OrdinalIgnoreCase);

        public static FontFamily Get(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                name = "Segoe UI";
            }

            FontFamily family;
            if (Cache.TryGetValue(name, out family))
            {
                return family;
            }

            try
            {
                family = BuiltIn.Contains(name) ? new FontFamily(Folder, "./#" + name) : new FontFamily(name);
            }
            catch (Exception ex)
            {
                PluginLog.Info("Font " + name + " could not be loaded: " + ex.Message);
                family = new FontFamily("Segoe UI");
            }

            Cache[name] = family;
            return family;
        }
    }
}
