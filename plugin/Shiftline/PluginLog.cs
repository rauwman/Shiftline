using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;

namespace Shiftline.Plugin
{
    /// <summary>
    /// The plugin's own log. Entries are shown on the Settings page and also written to SimHub's log.
    /// Safe to call from any thread.
    /// </summary>
    public static class PluginLog
    {
        public const int MaxEntries = 500;

        public static readonly ObservableCollection<string> Entries = new ObservableCollection<string>();

        public static void Info(string message)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + message;

            try
            {
                SimHub.Logging.Current.Info("Shiftline: " + message);
            }
            catch (Exception)
            {
                // logging must never break the plugin
            }

            Dispatcher dispatcher = Application.Current != null ? Application.Current.Dispatcher : null;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                Add(line);
            }
            else
            {
                dispatcher.BeginInvoke(new Action(() => Add(line)));
            }
        }

        private static void Add(string line)
        {
            Entries.Add(line);
            while (Entries.Count > MaxEntries)
            {
                Entries.RemoveAt(0);
            }
        }
    }
}
