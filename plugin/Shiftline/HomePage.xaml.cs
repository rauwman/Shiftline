using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace Shiftline.Plugin
{
    /// <summary>The Home page: overview of what the plugin does and a link to the project.</summary>
    public partial class HomePage : UserControl
    {
        private const string ProjectUrl = "https://github.com/rauwman/Shiftline";

        private System.Windows.Threading.DispatcherTimer _timer;

        public HomePage() : this(null)
        {
        }

        public HomePage(Shiftline plugin)
        {
            Plugin = plugin;
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            VersionText.Text = "Version " + UpdateService.Installed;
            AutoCheckToggle.IsChecked = UpdateService.AutoCheck;
        }

        public Shiftline Plugin { get; }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            ShowUpdateState();
            if (_timer == null)
            {
                _timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _timer.Tick += (s, args) => UpdateStatus();
            }

            UpdateStatus();
            _timer.Start();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_timer != null)
            {
                _timer.Stop();
            }
        }

        private object Read(string name)
        {
            try
            {
                return Plugin == null || Plugin.PluginManager == null ? null : Plugin.PluginManager.GetPropertyValue(name);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Shows whether the LED profile reads the script, and the game, car and preset that are active now.</summary>
        private void UpdateStatus()
        {
            TimeSpan? since = Plugin == null ? null : Plugin.SinceScriptRead;
            bool running = since.HasValue && since.Value.TotalSeconds < 5;
            LoaderDot.Fill = (System.Windows.Media.Brush)FindResource(running ? "OkBrush" : "WarnBrush");
            LoaderText.Text = running ? "LED profile is running" : "LED profile not detected";
            QuickStart.Visibility = running ? Visibility.Collapsed : Visibility.Visible;

            string game = Read("DataCorePlugin.CurrentGame") as string;
            bool gameRunning = Convert.ToString(Read("DataCorePlugin.GameRunning")) == "True";
            GameText.Text = gameRunning && !string.IsNullOrEmpty(game) ? GameOption.Pretty(game) : "No game running";
            string car = Read("DataCorePlugin.GameData.CarModel") as string;
            CarText.Text = gameRunning && !string.IsNullOrEmpty(car) ? car : "-";
            Settings settings = DataContext as Settings;
            PresetText.Text = settings == null ? "-" : settings.ActiveProfile;
        }

        /// <summary>Shows what the last update check found and which buttons make sense.</summary>
        private void ShowUpdateState()
        {
            UpdateInfo info = UpdateService.Available;
            UpdateText.Text = UpdateService.Checked ? UpdateService.Status : "Version " + UpdateService.Installed + " is installed. Not checked yet in this session.";
            InstallButton.Visibility = info != null && info.DllUrl != null ? Visibility.Visible : Visibility.Collapsed;
            NotesButton.Visibility = info != null ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            CheckButton.IsEnabled = false;
            UpdateText.Text = "Looking for a new version ...";
            await UpdateService.CheckAsync();
            CheckButton.IsEnabled = true;
            ShowUpdateState();
        }

        private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
        {
            UpdateInfo info = UpdateService.Available;
            if (info == null || info.DllUrl == null)
            {
                return;
            }

            bool confirmed = ConfirmDialog.Ask(this, "Shiftline update",
                "SimHub will close and start again with Shiftline " + info.Version + ". Finish your session first, a running game keeps running but SimHub cannot be used while it restarts. Windows may ask for administrator rights to replace the file. Continue?",
                "Continue", "Cancel");
            if (!confirmed)
            {
                return;
            }

            InstallButton.IsEnabled = false;
            try
            {
                UpdateText.Text = "Downloading version " + info.Version + " ...";
                string file = await UpdateService.DownloadAsync(info);
                UpdateText.Text = "Closing SimHub to install the update ...";
                if (UpdateService.StartInstall(file))
                {
                    Application.Current.Shutdown();
                    return;
                }

                UpdateText.Text = "The update was not started (administrator rights were not given).";
            }
            catch (Exception ex)
            {
                PluginLog.Info("Update failed: " + ex.Message);
                UpdateText.Text = "The update failed: " + ex.Message;
            }

            InstallButton.IsEnabled = true;
        }

        private void UpdateNotes_Click(object sender, RoutedEventArgs e)
        {
            UpdateInfo info = UpdateService.Available;
            if (info != null && !string.IsNullOrEmpty(info.PageUrl))
            {
                OpenUrl(info.PageUrl);
            }
        }

        private void AutoCheck_Click(object sender, RoutedEventArgs e)
        {
            UpdateService.AutoCheck = AutoCheckToggle.IsChecked == true;
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                PluginLog.Info("Could not open " + url + ": " + ex.Message);
            }
        }

        private void OpenGitHub_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(ProjectUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                PluginLog.Info("Could not open " + ProjectUrl + ": " + ex.Message);
            }
        }
    }
}
