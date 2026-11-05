using System;
using System.Linq;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Shiftline.Plugin
{
    /// <summary>
    /// The LED page: what the LEDs do, and the settings tabs. The chosen function switches the tabs
    /// that do not apply (RPM, Brake, Fuel) off.
    /// </summary>
    public partial class LedPage : UserControl
    {
        private Settings _settings;

        public LedPage()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += (sender, args) => EndEffectTest();
        }

        public LedPage(Shiftline plugin) : this()
        {
            Plugin = plugin;
        }

        public Shiftline Plugin { get; }

        /// <summary>Puts every setting shown on the tab named in the Tag back to its standard value, after asking.</summary>
        private void ResetTab_Click(object sender, RoutedEventArgs e)
        {
            FrameworkElement button = sender as FrameworkElement;
            string tab = button != null ? button.Tag as string : null;
            string[] names;
            if (_settings == null || tab == null || !TabSettings.ByTab.TryGetValue(tab, out names))
            {
                return;
            }

            if (!ConfirmDialog.Ask(this, "Reset tab", "Reset all settings on the " + tab + " tab to their standard values?", "Reset", "Cancel"))
            {
                return;
            }

            foreach (string name in names)
            {
                _settings.ResetSetting(name);
            }
        }

        /// <summary>Applies a pedal preset (the Tag names it); Custom clears the three areas so that they can be set one by one.</summary>
        private void PedalPreset_Click(object sender, RoutedEventArgs e)
        {
            FrameworkElement tile = sender as FrameworkElement;
            if (_settings != null && tile != null)
            {
                _settings.ApplyPedalPreset(tile.Tag as string);
            }
        }

        // ---------------------------------------------------------------- test button (play button on a card)

        private const int TestStepMilliseconds = 3000;
        private DispatcherTimer _testTimer;
        private bool _testRunning;
        private string _savedTestMode;
        private int _savedTestScenario;
        private int[] _testSteps;
        private int _testStepIndex;

        /// <summary>
        /// Plays the scenario named in the Tag on the real LEDs for 3 seconds, then puts the test mode back as it was.
        /// Several names separated by "|" play one after the other, 3 seconds each.
        /// </summary>
        private void TestEffect_Click(object sender, RoutedEventArgs e)
        {
            FrameworkElement button = sender as FrameworkElement;
            string tag = button != null ? button.Tag as string : null;
            if (_settings == null || tag == null)
            {
                return;
            }

            string[] names = tag.Split('|');
            int[] steps = new int[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                steps[i] = Array.IndexOf(TestScenarios.Names, names[i]);
                if (steps[i] < 0)
                {
                    return;
                }
            }

            if (!_testRunning)
            {
                _savedTestMode = _settings.TestMode;
                _savedTestScenario = _settings.TestScenario;
                _testRunning = true;
            }

            _testSteps = steps;
            _testStepIndex = 0;
            _settings.TestScenario = steps[0];
            _settings.TestMode = "Manual";

            if (_testTimer == null)
            {
                _testTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TestStepMilliseconds) };
                _testTimer.Tick += OnTestStepFinished;
            }

            _testTimer.Stop();
            _testTimer.Start();
        }

        private void OnTestStepFinished(object sender, EventArgs e)
        {
            _testStepIndex++;
            if (_settings != null && _testSteps != null && _testStepIndex < _testSteps.Length)
            {
                _settings.TestScenario = _testSteps[_testStepIndex];
                return;
            }

            EndEffectTest();
        }

        /// <summary>Ends a running effect test and restores the test mode and scenario from before it.</summary>
        private void EndEffectTest()
        {
            if (_testTimer != null)
            {
                _testTimer.Stop();
            }

            if (_testRunning && _settings != null)
            {
                _settings.TestMode = _savedTestMode;
                _settings.TestScenario = _savedTestScenario;
            }

            _testRunning = false;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Settings data = DataContext as Settings;
            if (data == null)
            {
                return;
            }

            if (data != _settings)
            {
                if (_settings != null)
                {
                    _settings.PropertyChanged -= OnSettingChanged;
                }

                _settings = data;
                _settings.PropertyChanged += OnSettingChanged;
                EnsureEnabledTab();
            }
        }

        private void OnSettingChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "LedFunction")
            {
                // The tabs update their enabled state from the same change, so look at them a moment later.
                Dispatcher.BeginInvoke(new Action(EnsureEnabledTab), DispatcherPriority.Background);
            }
        }

        private void EnsureEnabledTab()
        {
            TabItem selected = LedTabs.SelectedItem as TabItem;
            if (selected != null && selected.IsEnabled)
            {
                return;
            }

            foreach (object item in LedTabs.Items)
            {
                TabItem tab = item as TabItem;
                if (tab != null && tab.IsEnabled)
                {
                    LedTabs.SelectedItem = tab;
                    return;
                }
            }
        }

        /// <summary>A click on a function tile (not a preset or game change) opens the tab that belongs to it; the Tag of the tile names the tab.</summary>
        private void Function_Click(object sender, RoutedEventArgs e)
        {
            FrameworkElement tile = sender as FrameworkElement;
            string tab = tile == null ? null : tile.Tag as string;
            if (tab != null)
            {
                // The tabs update their enabled state from the same change, so select after that.
                Dispatcher.BeginInvoke(new Action(() => SelectTab(tab)), DispatcherPriority.Background);
            }

            // Choosing the function Fuel with the fuel level switched off would show nothing: start with the gauge on the top row.
            if (_settings != null && tile != null && _settings.LedFunction == "fuel" && _settings.FuelPlacement == "off" && _settings.FuelCentreAllowed)
            {
                _settings.FuelPlacement = "centre";
            }
        }

        /// <summary>Opens one group of the Effects tab (Indicators, Pit, Car, Ers or Extras); the other groups stay as they are.</summary>
        public void OpenEffectGroup(string group)
        {
            System.Windows.Controls.Primitives.ToggleButton toggle = FindName("Group" + group + "Toggle") as System.Windows.Controls.Primitives.ToggleButton;
            if (toggle != null)
            {
                toggle.IsChecked = true;
            }
        }

        /// <summary>Selects a tab by its header text, when that tab is available.</summary>
        public void SelectTab(string header)
        {
            foreach (object item in LedTabs.Items)
            {
                TabItem tab = item as TabItem;
                if (tab != null && (string)tab.Header == header && tab.IsEnabled)
                {
                    LedTabs.SelectedItem = tab;
                    return;
                }
            }
        }
    }
}
