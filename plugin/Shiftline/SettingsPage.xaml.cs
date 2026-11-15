using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace Shiftline.Plugin
{
    /// <summary>
    /// The Settings page: update options, settings profiles, backups, the system log, test mode and reset.
    /// </summary>
    public partial class SettingsPage : UserControl
    {
        public SettingsPage()
        {
            InitializeComponent();
            ExtendPage();
        }

        /// <summary>Extension point: does nothing unless another part of this class provides a body.</summary>
        partial void ExtendPage();

        public SettingsPage(Shiftline plugin) : this()
        {
            Plugin = plugin;
            Loaded += (sender, args) =>
            {
                StartLiveState();
                RefreshLists();
                RefreshPriority(null);
                LogList.ItemsSource = PluginLog.Entries;
                ScrollLogToEnd();
            };
            Unloaded += (sender, args) => StopLiveState();
            Plugin.Settings.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == "LayerOrder")
                {
                    Dispatcher.BeginInvoke(new Action(() => RefreshPriority(null)));
                }
            };
            ((INotifyCollectionChanged)PluginLog.Entries).CollectionChanged += (sender, args) => ScrollLogToEnd();
        }

        public Shiftline Plugin { get; }

        private Settings Data
        {
            get { return Plugin.Settings; }
        }

        private void RefreshLists()
        {
            BackupList.ItemsSource = ProfileStore.List(ProfileStore.BackupsFolder);
        }

        // ---------------------------------------------------------------- live game state

        private static readonly string[] LiveProperties =
        {
            "DataCorePlugin.CurrentGame", "DataCorePlugin.GameRunning", "DataCorePlugin.GamePaused", "DataCorePlugin.GameInMenu",
            "DataCorePlugin.GameData.EngineIgnitionOn", "DataCorePlugin.GameData.Rpms", "DataCorePlugin.GameData.SpeedKmh",
            "DataCorePlugin.GameData.Brake"
        };

        private System.Windows.Threading.DispatcherTimer _liveTimer;

        private void StartLiveState()
        {
            if (_liveTimer == null)
            {
                _liveTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                _liveTimer.Tick += (sender, args) => UpdateLiveState();
            }

            UpdateLiveState();
            _liveTimer.Start();
        }

        private void StopLiveState()
        {
            if (_liveTimer != null)
            {
                _liveTimer.Stop();
            }
        }

        private void UpdateLiveState()
        {
            StringBuilder text = new StringBuilder();
            foreach (string name in LiveProperties)
            {
                object value = null;
                try
                {
                    value = Plugin.PluginManager == null ? null : Plugin.PluginManager.GetPropertyValue(name);
                }
                catch (Exception)
                {
                }

                text.AppendLine(name.Replace("DataCorePlugin.", string.Empty).Replace("GameData.", string.Empty).PadRight(22) + (value == null ? "-" : value.ToString()));
            }

            LiveState.Text = text.ToString().TrimEnd();
        }

        private void RefreshPriority(string selectId)
        {
            Settings.PriorityItem selected = PriorityList.SelectedItem as Settings.PriorityItem;
            string keep = selectId ?? (selected == null ? null : selected.Id);
            System.Collections.Generic.List<Settings.PriorityItem> items = Data.GetPriorityItems();
            PriorityList.ItemsSource = items;
            PriorityList.SelectedItem = keep == null ? null : items.FirstOrDefault(i => i.Id == keep);
        }

        private void MoveSelectedPriority(int direction)
        {
            Settings.PriorityItem selected = PriorityList.SelectedItem as Settings.PriorityItem;
            if (selected == null)
            {
                return;
            }

            Data.MovePriority(selected.Id, direction);
            RefreshPriority(selected.Id);
        }

        private void PriorityUp_Click(object sender, RoutedEventArgs e)
        {
            MoveSelectedPriority(1);
        }

        private void PriorityDown_Click(object sender, RoutedEventArgs e)
        {
            MoveSelectedPriority(-1);
        }

        private void PriorityReset_Click(object sender, RoutedEventArgs e)
        {
            Data.ResetPriority();
            RefreshPriority(null);
        }

        private void ScrollLogToEnd()
        {
            if (LogList.Items.Count > 0)
            {
                LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
            }
        }

        private static void OpenFolder(string folder)
        {
            try
            {
                Directory.CreateDirectory(folder);
                Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                PluginLog.Info("Could not open " + folder + ": " + ex.Message);
            }
        }

        // ---------------------------------------------------------------- backups

        private void CreateBackup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                PluginLog.Info("Backup created: " + ProfileStore.CreateBackup(Data));
            }
            catch (Exception ex)
            {
                PluginLog.Info("Creating the backup failed: " + ex.Message);
            }

            RefreshLists();
        }

        private void RestoreBackup_Click(object sender, RoutedEventArgs e)
        {
            string name = BackupList.SelectedItem as string;
            if (name == null)
            {
                return;
            }

            try
            {
                Settings restored = ProfileStore.Read(ProfileStore.BackupsFolder, name);
                string backup = ProfileStore.CreateBackup(Data);
                Data.CopyFrom(restored);
                Plugin.SaveSettings();
                PluginLog.Info("Backup restored: " + name + " (backup before restoring: " + backup + ")");
            }
            catch (Exception ex)
            {
                PluginLog.Info("Restoring the backup failed: " + ex.Message);
            }

            RefreshLists();
        }

        private void DeleteBackup_Click(object sender, RoutedEventArgs e)
        {
            string name = BackupList.SelectedItem as string;
            if (name == null)
            {
                return;
            }

            try
            {
                ProfileStore.Delete(ProfileStore.BackupsFolder, name);
                PluginLog.Info("Backup deleted: " + name);
            }
            catch (Exception ex)
            {
                PluginLog.Info("Deleting the backup failed: " + ex.Message);
            }

            RefreshLists();
        }

        private void OpenBackupsFolder_Click(object sender, RoutedEventArgs e)
        {
            OpenFolder(ProfileStore.BackupsFolder);
        }

        // ---------------------------------------------------------------- log and reset

        private void CopyLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(string.Join(Environment.NewLine, PluginLog.Entries.ToArray()));
            }
            catch (Exception ex)
            {
                PluginLog.Info("Copying the log failed: " + ex.Message);
            }
        }

        private void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            PluginLog.Entries.Clear();
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            string backup = ProfileStore.CreateBackup(Data);
            Data.ResetAll();
            Plugin.SaveSettings();
            PluginLog.Info("All settings reset to defaults (backup before resetting: " + backup + ")");
            RefreshLists();
        }
    }
}
