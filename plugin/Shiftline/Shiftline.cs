using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Threading.Tasks;
using System.Windows.Media;
using GameReaderCommon;
using SimHub.Plugins;

namespace Shiftline.Plugin
{
    /// <summary>
    /// Settings page and property provider for the "Shiftline" LED profile.
    ///
    /// The class name is the property prefix: every setting is published as
    /// Shiftline.&lt;Name&gt; and read by the JavaScript inside the LED profile.
    /// The colour themes are published as the lists Shiftline.RpmColours, Shiftline.IdleColours and Shiftline.FuelColours.
    /// The LED logic itself lives in that profile; this plugin only stores the settings.
    /// </summary>
    [PluginDescription("Settings for the Shiftline LED profile (RPM bar, brake bar, idle animation)")]
    [PluginAuthor("rauwman")]
    [PluginName("Shiftline")]
    public class Shiftline : IPlugin, IDataPlugin, IWPFSettingsV2
    {
        private const string SettingsKey = "GeneralSettings";

        public Settings Settings;

        private string _lastGame;
        private string _lastCar;
        private long _scriptReadTicks;

        /// <summary>How long ago the LED profile last asked for the script, or null when it never did in this session.</summary>
        public TimeSpan? SinceScriptRead
        {
            get
            {
                long ticks = System.Threading.Interlocked.Read(ref _scriptReadTicks);
                return ticks == 0 ? (TimeSpan?)null : TimeSpan.FromTicks(DateTime.UtcNow.Ticks - ticks);
            }
        }

        private void NoteScriptRead()
        {
            System.Threading.Interlocked.Exchange(ref _scriptReadTicks, DateTime.UtcNow.Ticks);
        }

        /// <summary>Instance of the current plugin manager.</summary>
        public PluginManager PluginManager { get; set; }

        /// <summary>Left menu icon (24x24, black and white compatible).</summary>
        public ImageSource PictureIcon => this.ToIcon(IconFactory.Create());

        /// <summary>Short title for the left menu.</summary>
        public string LeftMenuTitle => "Shiftline";

        /// <summary>
        /// Called once per game data update. Nothing to compute here: the settings are
        /// published as delegates that SimHub evaluates on demand.
        /// </summary>
        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            if (Settings == null)
            {
                return;
            }

            OverlayData.Update(pluginManager);      // before the early return below: the standings change on every update

            // A new game: switch to the preset of that game (its favourite, otherwise the favourite of "any game").
            string game = pluginManager.GetPropertyValue("DataCorePlugin.CurrentGame") as string;
            string car = pluginManager.GetPropertyValue("DataCorePlugin.GameData.CarModel") as string ?? string.Empty;
            if (string.IsNullOrEmpty(game) || (game == _lastGame && car == _lastCar))
            {
                return;
            }

            _lastGame = game;
            _lastCar = car;
            KnownCars.Add(GameOption.KeyOf(game), car);
            KnownGames.Add(game);
            try
            {
                string name = PresetStore.ChooseFor(GameOption.KeyOf(game), car);
                if (name != null && name != Settings.ActiveProfile)
                {
                    PluginLog.Info("Game " + game + (car.Length > 0 ? ", car " + car : string.Empty) + ": switching to the preset " + name);
                    Application.Current.Dispatcher.BeginInvoke(new Action(() => LoadPreset(name)));
                }
            }
            catch (Exception ex)
            {
                PluginLog.Info("Choosing the preset for " + game + " failed: " + ex.Message);
            }
        }

        /// <summary>Called when SimHub stops: persist the settings.</summary>
        public void End(PluginManager pluginManager)
        {
            SaveSettings();
        }

        /// <summary>Returns the settings page.</summary>
        public System.Windows.Controls.Control GetWPFSettingsControl(PluginManager pluginManager)
        {
            try
            {
                return new SettingsControl(this);
            }
            catch (Exception ex)
            {
                // Show the problem on the page instead of a modal error, and keep it in the log.
                PluginLog.Info("The settings page could not be created: " + ex);
                return new System.Windows.Controls.TextBox
                {
                    Text = "Shiftline: the settings page could not be created." + Environment.NewLine + Environment.NewLine + ex,
                    IsReadOnly = true,
                    TextWrapping = System.Windows.TextWrapping.Wrap,
                    Margin = new System.Windows.Thickness(20)
                };
            }
        }

        /// <summary>Called once after the plugins start.</summary>
        public void Init(PluginManager pluginManager)
        {

            Settings = this.ReadCommonSettings<Settings>(SettingsKey, () => new Settings());
            Settings.EnsureThemes();
            Settings.TestMode = "Off";
            EnsurePresets();

            // Publish every setting the LED script reads as Shiftline.<PropertyName>.
            foreach (string name in Settings.ScriptSettingNames)
            {
                PropertyInfo captured = typeof(Settings).GetProperty(name);
                this.AttachDelegate(name, () => captured.GetValue(Settings, null));
            }

            // The colour lists of the selected themes, read by the LED script.
            this.AttachDelegate("RpmColours", () => Settings.ResolveColours(Settings.RpmThemeId, Settings.DefaultRpmColours));
            this.AttachDelegate("IdleColours", () => Settings.ResolveColours(Settings.IdleThemeId, Settings.DefaultIdleColours));
            this.AttachDelegate("FuelColours", () => Settings.ResolveColours(Settings.FuelThemeId, Settings.DefaultFuelColours));

            // Overlay data check properties (Shiftline.Standings.*).
            OverlayData.Attach(this);

            // The LED script itself, for the loader profile (see profiles\Shiftline Loader.ledsprofile).
            this.AttachDelegate("ScriptBuild", () => { NoteScriptRead(); return Settings.ScriptBuild; });
            this.AttachDelegate("ScriptSource", () => ScriptSource.Text);

            // Look for a newer release a little after SimHub has started (the Home page shows the result).
            if (UpdateService.AutoCheck)
            {
                Task.Run(async () =>
                {
                    await Task.Delay(15000).ConfigureAwait(false);
                    await UpdateService.CheckAsync().ConfigureAwait(false);
                });
            }

            // Actions that can be mapped to a button or key in SimHub (Controls and events).
            this.AddAction(actionName: "NextPreset", actionStart: (a, b) => OnUi(() => CyclePreset(1)));
            this.AddAction(actionName: "PreviousPreset", actionStart: (a, b) => OnUi(() => CyclePreset(-1)));
            this.AddAction(actionName: "BrightnessUp", actionStart: (a, b) => OnUi(() => ChangeBrightness(10)));
            this.AddAction(actionName: "BrightnessDown", actionStart: (a, b) => OnUi(() => ChangeBrightness(-10)));
            this.AddAction(actionName: "ToggleNightMode", actionStart: (a, b) => OnUi(() => Settings.NightMode = !Settings.NightMode));

            string built = File.GetLastWriteTime(Assembly.GetExecutingAssembly().Location).ToString("yyyy-MM-dd HH:mm:ss");
            PluginLog.Info("started (DLL built " + built + "), " + Settings.Themes.Count + " colour themes, LED script " + Settings.ScriptBuild + " (" + ScriptSource.Text.Length + " characters)");
        }

        /// <summary>Writes the settings to disk, and into the active preset (a preset always holds the settings last used with it).</summary>
        public void SaveSettings()
        {
            if (Settings != null)
            {
                this.SaveCommonSettings(SettingsKey, Settings);
                try
                {
                    if (PresetStore.Exists(Settings.ActiveProfile))
                    {
                        PresetStore.Save(Settings.ActiveProfile, Settings);
                    }
                }
                catch (Exception ex)
                {
                    PluginLog.Info("Saving the preset " + Settings.ActiveProfile + " failed: " + ex.Message);
                }
            }
        }

        /// <summary>There is always at least one preset, and the active one exists.</summary>
        private void EnsurePresets()
        {
            try
            {
                PresetStore.MigrateOldProfiles();
                if (!PresetStore.Exists(Settings.ActiveProfile))
                {
                    string name = PresetStore.UniqueName("Default");
                    bool first = PresetStore.List().Count == 0;
                    PresetStore.Save(name, Settings, "any", first);
                    Settings.ActiveProfile = name;
                }
            }
            catch (Exception ex)
            {
                PluginLog.Info("Preparing the presets failed: " + ex.Message);
            }
        }

        private static void OnUi(Action action)
        {
            if (Application.Current != null)
            {
                Application.Current.Dispatcher.BeginInvoke(action);
            }
        }

        /// <summary>Loads the next (direction 1) or previous (-1) preset of the list; used by the button actions.</summary>
        private void CyclePreset(int direction)
        {
            System.Collections.Generic.List<PresetInfo> presets = PresetStore.List();
            if (Settings == null || presets.Count < 2)
            {
                return;
            }

            int index = presets.FindIndex(p => p.Name == Settings.ActiveProfile);
            int next = ((index < 0 ? 0 : index) + direction + presets.Count) % presets.Count;
            LoadPreset(presets[next].Name);
        }

        /// <summary>Changes the general brightness by a number of percent.</summary>
        private void ChangeBrightness(int delta)
        {
            if (Settings == null)
            {
                return;
            }

            Settings.BrightnessGeneral = Math.Max(1, Math.Min(100, Settings.BrightnessGeneral + delta));
        }

        /// <summary>Loads a preset into the settings. What was set up since the last save stays in the preset that was active.</summary>
        public void LoadPreset(string name)
        {
            if (Settings == null || !PresetStore.Exists(name))
            {
                return;
            }

            try
            {
                SaveSettings();
                Settings.CopyFrom(PresetStore.ReadSettings(name));
                Settings.ActiveProfile = name;
                Settings.TestMode = "Off";
                SaveSettings();
                PluginLog.Info("Preset loaded: " + name);
            }
            catch (Exception ex)
            {
                PluginLog.Info("Loading the preset " + name + " failed: " + ex.Message);
            }
        }
    }
}
