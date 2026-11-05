using System.IO;
using System.Text;

namespace Shiftline.Plugin
{
    /// <summary>
    /// The LED script (tools/Shiftline.plugin.js), embedded into the plugin DLL. The plugin publishes it as the property
    /// Shiftline.ScriptSource; the loader profile runs it, so a plugin update also updates the LED logic.
    /// </summary>
    internal static class ScriptSource
    {
        private static string _text;

        public static string Text
        {
            get
            {
                if (_text == null)
                {
                    _text = Load();
                }

                return _text;
            }
        }

        private static string Load()
        {
            using (Stream stream = typeof(ScriptSource).Assembly.GetManifestResourceStream("Shiftline.plugin.js"))
            {
                if (stream == null)
                {
                    PluginLog.Info("The LED script could not be found inside this build.");
                    return string.Empty;
                }

                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}
