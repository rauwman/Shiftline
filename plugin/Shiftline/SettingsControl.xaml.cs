using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Shiftline.Plugin
{
    /// <summary>
    /// The plugin page: navigation on the left; on the right a header bar (LED profile status, preset, search)
    /// above the selected page (Home, LED, Colours, Settings).
    /// </summary>
    public partial class SettingsControl : UserControl
    {
        private readonly Dictionary<string, UserControl> _pages = new Dictionary<string, UserControl>();

        private DispatcherTimer _timer;
        private bool _refreshing;

        public Shiftline Plugin { get; }

        public SettingsControl()
        {
            InitializeComponent();

            // Attached here and not in the XAML: an event on the root element makes the generated code write
            // "Shiftline.Plugin.SettingsControl", where the class Shiftline hides the namespace.
            PreviewKeyDown += Root_PreviewKeyDown;
        }

        public SettingsControl(Shiftline plugin) : this()
        {
            Plugin = plugin;
            DataContext = plugin.Settings;

            // Persist when the page is left, not only when SimHub shuts down.
            // Test mode never stays on after leaving the page, so the LEDs do not keep showing test patterns.
            Unloaded += (sender, args) =>
            {
                plugin.Settings.TestMode = "Off";
                plugin.SaveSettings();
            };

            Loaded += OnShellLoaded;
            Unloaded += OnShellUnloaded;

            NavHome.IsChecked = true;
        }

        // ---------------------------------------------------------------- header bar

        private void OnShellLoaded(object sender, RoutedEventArgs e)
        {
            Plugin.Settings.PropertyChanged -= OnSettingChanged;
            Plugin.Settings.PropertyChanged += OnSettingChanged;
            RefreshPresets();

            if (_timer == null)
            {
                _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _timer.Tick += (s, args) => UpdateStatus();
            }

            UpdateStatus();
            _timer.Start();
        }

        private void OnShellUnloaded(object sender, RoutedEventArgs e)
        {
            Plugin.Settings.PropertyChanged -= OnSettingChanged;
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

        /// <summary>Shows whether the LED profile reads the script (same check as on the Home page) and the game that runs.</summary>
        private void UpdateStatus()
        {
            TimeSpan? since = Plugin == null ? null : Plugin.SinceScriptRead;
            bool running = since.HasValue && since.Value.TotalSeconds < 5;
            StatusDot.Fill = (System.Windows.Media.Brush)FindResource(running ? "OkBrush" : "WarnBrush");
            StatusText.Text = running ? "LED profile running" : "LED profile not detected";
            StatusText.ToolTip = running ? null : "Import the profile Shiftline Loader into your LED device in SimHub and select it. The Home page explains the steps.";

            string game = Read("DataCorePlugin.CurrentGame") as string;
            bool gameRunning = Convert.ToString(Read("DataCorePlugin.GameRunning")) == "True";
            StatusGame.Text = gameRunning && !string.IsNullOrEmpty(game) ? GameOption.Pretty(game) : string.Empty;
        }

        /// <summary>Fills the preset list with the saved presets and selects the active one.</summary>
        private void RefreshPresets()
        {
            if (Plugin == null)
            {
                return;
            }

            _refreshing = true;
            PresetCombo.ItemsSource = PresetStore.List().Select(info => info.Name).ToList();
            PresetCombo.SelectedItem = Plugin.Settings.ActiveProfile;
            _refreshing = false;
        }

        private void OnSettingChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "ActiveProfile")
            {
                Dispatcher.BeginInvoke(new Action(RefreshPresets), DispatcherPriority.Background);
            }
        }

        private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string name = PresetCombo.SelectedItem as string;
            if (_refreshing || name == null || Plugin == null || name == Plugin.Settings.ActiveProfile)
            {
                return;
            }

            Plugin.LoadPreset(name);
        }

        private void PresetCombo_DropDownOpened(object sender, EventArgs e)
        {
            RefreshPresets();
        }

        /// <summary>Opens the Presets page of the plugin.</summary>
        private void ManagePresets_Click(object sender, RoutedEventArgs e)
        {
            Navigate("Presets", null);
        }

        // ---------------------------------------------------------------- pages

        private UserControl GetPage(string key)
        {
            UserControl page;
            if (_pages.TryGetValue(key, out page))
            {
                return page;
            }

            switch (key)
            {
                case "Led":
                    page = new LedPage(Plugin);
                    break;
                case "Overlays":
                    page = new StandingsPage(Plugin);
                    break;
                case "OverlaySettings":
                    page = new OverlaySettingsPage(Plugin);
                    break;
                case "BattleBar":
                    page = new BattleBarPage(Plugin);
                    break;
                case "Relatives":
                    page = new StandingsPage(Plugin, OverlayData.RelativesKey);
                    break;
                case "TrackMap":
                    page = new TrackMapPage(Plugin);
                    break;
                case "Presets":
                    page = new PresetsPage(Plugin);
                    break;
                case "Colours":
                    page = new ColoursPage();
                    break;
                case "Settings":
                    page = new SettingsPage(Plugin);
                    break;
                default:
                    page = new HomePage(Plugin);
                    break;
            }

            _pages[key] = page;
            return page;
        }

        /// <summary>
        /// The mouse wheel scrolls the page, also over lists and text boxes. A list or text box keeps the wheel only while it can
        /// still scroll in that direction itself; at its end the page scrolls on.
        /// </summary>
        private void PageScroll_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            DependencyObject node = e.OriginalSource as DependencyObject;
            while (node != null && node != PageScroll)
            {
                ScrollViewer inner = node as ScrollViewer;
                if (inner != null && inner.ScrollableHeight > 0)
                {
                    bool atTop = inner.VerticalOffset <= 0;
                    bool atBottom = inner.VerticalOffset >= inner.ScrollableHeight - 0.5;
                    if ((e.Delta > 0 && !atTop) || (e.Delta < 0 && !atBottom))
                    {
                        return;
                    }
                }

                node = node is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
            }

            if (node == null)
            {
                return;                 // not inside the page (for example the list of an open drop-down): leave the wheel to it
            }

            e.Handled = true;
            PageScroll.ScrollToVerticalOffset(PageScroll.VerticalOffset - e.Delta);
        }

        private void ShowPage(string key)
        {
            // Test mode lives on the Settings page and switches off as soon as another page is opened.
            if (key != "Settings" && Plugin != null)
            {
                Plugin.Settings.TestMode = "Off";
            }

            PageHost.Content = GetPage(key);
            RefreshPresets();           // a preset may have been created, renamed or deleted on the Presets page
        }

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            RadioButton button = sender as RadioButton;
            if (button == null || Plugin == null)
            {
                return;
            }

            ShowPage((string)button.Tag);
        }

        /// <summary>Opens a page and, for the LED page, one of its tabs.</summary>
        public void Navigate(string page, string tab)
        {
            Navigate(page, tab, null);
        }

        /// <summary>Like Navigate, and opens one group of the Effects tab (Indicators, Pit, Car, Ers or Extras).</summary>
        public void Navigate(string page, string tab, string group)
        {
            switch (page)
            {
                case "Led":
                    NavLedGroup.IsChecked = true;
                    NavLed.IsChecked = true;
                    break;
                case "Overlays":
                    NavOverlaysGroup.IsChecked = true;
                    NavOverlayStandings.IsChecked = true;
                    break;
                case "OverlaySettings":
                    NavOverlaysGroup.IsChecked = true;
                    NavOverlayOverlaySettings.IsChecked = true;
                    break;
                case "BattleBar":
                    NavOverlaysGroup.IsChecked = true;
                    NavOverlayBattleBar.IsChecked = true;
                    break;
                case "Relatives":
                    NavOverlaysGroup.IsChecked = true;
                    NavOverlayRelatives.IsChecked = true;
                    break;
                case "TrackMap":
                    NavOverlaysGroup.IsChecked = true;
                    NavOverlayTrackMap.IsChecked = true;
                    break;
                case "Presets":
                    NavPresets.IsChecked = true;
                    break;
                case "Colours":
                    NavColours.IsChecked = true;
                    break;
                case "Settings":
                    NavSettings.IsChecked = true;
                    break;
                default:
                    NavHome.IsChecked = true;
                    break;
            }

            ShowPage(page);
            if (page == "Led" && tab != null)
            {
                LedPage led = (LedPage)GetPage("Led");
                led.SelectTab(tab);
                if (group != null)
                {
                    led.OpenEffectGroup(group);
                }
            }
        }

        // ---------------------------------------------------------------- search

        internal sealed class SearchEntry
        {
            public SearchEntry(string title, string page, string tab, string keywords, string group = null)
            {
                Group = group;
                Title = title;
                Page = page;
                Tab = tab;
                Keywords = keywords;
            }

            public string Title { get; private set; }
            public string Page { get; private set; }
            public string Tab { get; private set; }
            public string Group { get; private set; }
            public string Keywords { get; private set; }

            public string Display
            {
                get
                {
                    string page = Page == "Led" ? "Dash Panel" : Page == "Colours" ? "Theme Studio" : Page;
                    return Title + "   -   " + (Tab != null ? page + " / " + Tab : page);
                }
            }

            public bool Matches(string query)
            {
                string haystack = (Title + " " + Keywords).ToLowerInvariant();
                return query.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).All(word => haystack.Contains(word));
            }
        }

        // Add an entry here whenever a function gets a place on a page.
        private static readonly SearchEntry[] Index =
        {
            new SearchEntry("LED function (what the LEDs do)", "Led", null, "led panel function rpm brake pedals throttle fuel flags device arduino"),
            new SearchEntry("LED layout (LEDs per side, top LEDs)", "Led", null, "layout count strip number side centre top change"),
            new SearchEntry("RPM fill style", "Led", "RPM", "rev bar fill direction outside centre left right f1"),
            new SearchEntry("RPM colours and colour mode", "Led", "RPM", "theme blocks gradient first last led percent"),
            new SearchEntry("Redline behaviour and flash colour", "Led", "RPM", "redline flash solid limiter"),
            new SearchEntry("Brake bar style", "Led", "Brake", "brake pedal bar side centre"),
            new SearchEntry("Brake colour and blinking", "Led", "Brake", "brake colour blink"),
            new SearchEntry("Pedal input: brake and throttle colours", "Led", "Pedals", "pedal brake throttle gas colour input"),
            new SearchEntry("Effects (all on or off)", "Led", "Effects", "effects main master switch all on off", null),
            new SearchEntry("Effects: marshal flags, spotter", "Led", "Effects", "flags flag yellow red blue green white black checkered chequered spotter proximity indicator marshal", "Indicators"),
            new SearchEntry("Effects: pit lane", "Led", "Effects", "pit lane limiter speeding alert entry warning", "Pit"),
            new SearchEntry("Effects: car systems", "Led", "Effects", "engine start low fuel tc traction control abs headlight flash turn indicator lift coast", "Car"),
            new SearchEntry("Effects: ERS (energy recovery)", "Led", "Effects", "ers energy recovery deploy charge regen lap limit mode push to pass p2p", "Ers"),
            new SearchEntry("Effects: DRS, clutch, safety car delta", "Led", "Effects", "drs detection clutch safety car delta extras", "Extras"),
            new SearchEntry("Brightness (general, idle)", "Led", "Brightness", "dim bright intensity"),
            new SearchEntry("Idle animation on or off", "Led", "Idle", "idle animation no game"),
            new SearchEntry("Idle style, speed and colours", "Led", "Idle", "wave sweep comet speed theme"),
            new SearchEntry("Fuel gauge colours", "Led", "Fuel", "fuel tank level theme blocks gradient"),
            new SearchEntry("Colour themes (create, edit)", "Colours", null, "theme studio colour color palette gradient create edit duplicate delete"),
            new SearchEntry("Updates", "Settings", null, "update profile automatic"),
            new SearchEntry("Overlays", "Settings", null, "overlay map telemetry iracing"),
            new SearchEntry("Battle Bar (horizontal standings, ticker)", "BattleBar", null, "battle bar horizontal ticker cards standings tv"),
            new SearchEntry("Relatives (the cars around you, gaps)", "Relatives", null, "relatives relative cars around gap ahead behind iracing delta"),
            new SearchEntry("Track Map (the cars on a line or a ring)", "TrackMap", null, "track map line ring circle cars dots position lapped pit radar fastest lap"),
            new SearchEntry("Presets (create, favourite, game, import, export)", "Presets", null, "preset profile save load car game favourite import export rename delete"),
            new SearchEntry("Backups and restore (also reset to standard)", "Settings", null, "backup restore copy reset default standard"),
            new SearchEntry("System log", "Settings", null, "log errors messages"),
            new SearchEntry("Test mode", "Settings", null, "test manual auto scenario without game"),
            new SearchEntry("Reset to defaults", "Settings", null, "reset default"),
            new SearchEntry("Overview and GitHub", "Home", null, "home github version about")
        };

        /// <summary>Puts the cursor into the search field (Ctrl+K).</summary>
        private void OpenSearch()
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
        }

        /// <summary>Clears the search and hides the list of hits.</summary>
        private void CloseSearch()
        {
            SearchBox.Text = string.Empty;
            SearchPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>Ctrl+K jumps to the search field from anywhere on the plugin page.</summary>
        private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
            {
                OpenSearch();
                e.Handled = true;
            }
        }

        private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CloseSearch();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && SearchResults.Items.Count > 0)
            {
                SearchResults.SelectedIndex = 0;        // opens the first hit
                e.Handled = true;
            }
        }

        private void SearchBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            SearchHint.Visibility = Visibility.Collapsed;       // the placeholder goes as soon as the cursor is in the field
        }

        private void SearchBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            // Clicking a hit moves the focus into the list: keep everything then. Anywhere else the search is cleared.
            DependencyObject target = e.NewFocus as DependencyObject;
            while (target != null)
            {
                if (target == SearchPanel)
                {
                    return;
                }

                target = target is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(target) : LogicalTreeHelper.GetParent(target);
            }

            CloseSearch();
            SearchHint.Visibility = Visibility.Visible;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string query = SearchBox.Text.Trim();
            SearchHint.Visibility = SearchBox.Text.Length == 0 && !SearchBox.IsKeyboardFocused ? Visibility.Visible : Visibility.Collapsed;
            if (query.Length == 0)
            {
                SearchResults.ItemsSource = null;
                SearchPanel.Visibility = Visibility.Collapsed;
                return;
            }

            List<SearchEntry> hits = Index.Where(entry => entry.Matches(query)).ToList();
            SearchResults.ItemsSource = hits;
            SearchPanel.Visibility = hits.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SearchResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SearchEntry entry = SearchResults.SelectedItem as SearchEntry;
            if (entry == null)
            {
                return;
            }

            CloseSearch();
            Navigate(entry.Page, entry.Tab, entry.Group);
        }
    }
}
