using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Shiftline.Plugin
{
    /// <summary>The settings of the Track Map: how it looks and which cars it shows, with a live preview of invented data.</summary>
    public partial class TrackMapPage : UserControl
    {
        private readonly Dictionary<string, Grid> _rows = new Dictionary<string, Grid>();
        private readonly Stopwatch _driving = new Stopwatch();
        private TrackMapOverlay _preview;
        private DispatcherTimer _radioTimer;
        private DispatcherTimer _checkTimer;
        private bool _shapesShown;
        private readonly Random _random = new Random();
        private DispatcherTimer _timer;
        private bool _previewDriving;
        private bool _building;
        private bool _filling;

        public TrackMapPage()
        {
            InitializeComponent();
        }

        public TrackMapPage(Shiftline plugin) : this()
        {
            Plugin = plugin;
            Loaded += (sender, args) =>
            {
                ShowToggle.IsChecked = OverlaySettings.IsEnabled(OverlayData.TrackMapKey);
                FillRates();
                BuildControls();
                if (_checkTimer == null)
                {
                    _checkTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                    _checkTimer.Tick += (timer, tick) =>
                    {
                        if (TrackLibrary.IsReady && !_shapesShown)
                        {
                            _shapesShown = true;
                            RefreshPreview();       // the track shapes were still loading when the preview was drawn
                        }

                        ShowTrackCheck();
                    };
                }

                TrackLibrary.EnsureLoading();
                ShowTrackCheck();
                _checkTimer.Start();
            };
            Unloaded += (sender, args) =>
            {
                if (_timer != null)
                {
                    _timer.Stop();
                }

                if (_radioTimer != null)
                {
                    _radioTimer.Stop();
                }

                if (_checkTimer != null)
                {
                    _checkTimer.Stop();
                }
            };
        }

        public Shiftline Plugin { get; }

        private void BuildControls()
        {
            _building = true;
            _rows.Clear();
            AddChoices(MapPanel, "Map");
            AddChoices(CarsPanel, "Cars");
            AddChoices(LinePanel, "Line");
            AddChoices(AnimationPanel, "Animation");
            AddChoices(RadioPanel, "Radio", 16);
            AddChoices(LookPanel, "Look");
            PreviewStill.IsChecked = !_previewDriving;
            PreviewDriving.IsChecked = _previewDriving;
            _building = false;
            UpdateEnabled();
            RefreshPreview();
        }

        private void AddChoices(StackPanel panel, string group, double indent = 0)
        {
            panel.Children.Clear();
            foreach (StandingsChoice choice in TrackMapOptions.AllChoices.Where(c => c.Column == group))
            {
                Grid row = new Grid { Margin = new System.Windows.Thickness(indent, 0, 0, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new System.Windows.GridLength(300 - indent) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = System.Windows.GridLength.Auto });
                string hint;
                string shortLabel = StandingsOptions.SplitHint(choice.Label, out hint);
                TextBlock label = new TextBlock { Text = shortLabel, ToolTip = hint, TextWrapping = System.Windows.TextWrapping.Wrap, Margin = new System.Windows.Thickness(0, 0, 12, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center, Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary") };
                ComboBox combo = new ComboBox { Width = 380, Height = 34, Tag = choice };
                foreach (string text in choice.Labels)
                {
                    string itemHint;
                    string shortText = StandingsOptions.SplitHint(text, out itemHint);
                    combo.Items.Add(new ComboBoxItem { Content = shortText, ToolTip = itemHint });
                }

                combo.SelectedIndex = Math.Max(0, Array.IndexOf(choice.Values, TrackMapOptions.Raw(choice)));
                combo.SelectionChanged += (sender, args) =>
                {
                    ComboBox changed = (ComboBox)sender;
                    StandingsChoice picked = (StandingsChoice)changed.Tag;
                    if (!_building && changed.SelectedIndex >= 0)
                    {
                        TrackMapOptions.SetChoice(picked.Key, picked.Values[changed.SelectedIndex]);
                        UpdateEnabled();
                        RefreshPreview();
                    }
                };

                Grid.SetColumn(combo, 1);
                row.Children.Add(label);
                row.Children.Add(combo);
                panel.Children.Add(row);
                _rows[choice.Key] = row;
            }
        }

        /// <summary>A choice that does not apply to the current ones stays visible but is greyed out.</summary>
        private void UpdateEnabled()
        {
            TrackMapOptions options = TrackMapOptions.Load();
            bool ring = options.Ring;
            SetRowEnabled("Orientation", !ring);
            SetRowEnabled("Length", !ring);
            SetRowEnabled("Range", !ring && options.Centred);
            SetRowEnabled("RingSize", ring);
            SetRowEnabled("StartAt", ring && !options.Centred);
            SetRowEnabled("Direction", ring);
            SetRowEnabled("Thickness", options.Get("LineStyle") != "Strip");
            SetRowEnabled("TrackOrientation", options.TrackShapes);
            SetRowEnabled("TrackSize", options.TrackShapes);
        }

        private void SetRowEnabled(string key, bool enabled)
        {
            Grid row;
            if (_rows.TryGetValue(key, out row))
            {
                row.IsEnabled = enabled;
                row.Opacity = enabled ? 1 : 0.45;
            }
        }

        // ---------------------------------------------------------------- preview

        private void RefreshPreview()
        {
            if (_preview == null)
            {
                _preview = new TrackMapOverlay();
                _preview.DesignSizeChanged += SizePreview;
                PreviewHost.Child = _preview;
            }

            StandingsFeatures features = OverlayData.PreviewFeatures();
            _preview.ApplyOptions(TrackMapOptions.Load());
            System.Windows.Size size = _preview.DesignSize;
            _preview.Width = size.Width;
            _preview.Height = size.Height;
            string game = OverlayData.SelectedGameName;
            PreviewGameText.Text = string.IsNullOrEmpty(game) ? string.Empty : "Preview for " + game + ".";
            PreviewGameText.Visibility = PreviewGameText.Text.Length == 0 ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
            ShowLegend();
            _preview.Update(Sample());
            SizePreview();
            UpdateRadio();
            ShowPreviewTrack();
        }

        private void SizePreview()
        {
            System.Windows.Size size = _preview.DesignSize;
            _preview.Width = size.Width;
            _preview.Height = size.Height;
        }

        /// <summary>The credit line of the track shapes and which track the preview shows.</summary>
        private void ShowPreviewTrack()
        {
            bool shapes = TrackMapOptions.Load().TrackShapes;
            TrackSourceText.Visibility = shapes ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            string text = string.Empty;
            if (shapes)
            {
                TrackLayout layout = _preview == null ? null : _preview.Layout;
                if (!TrackLibrary.IsReady)
                {
                    text = "Loading the track shapes ...";
                }
                else if (layout != null)
                {
                    TrackSource source = TrackLibrary.SourceOf(layout);
                    text = "Preview track: " + layout.Title + (source == null ? string.Empty : " (" + source.DisplayName + ")") + ".";
                }
                else
                {
                    text = TrackLibrary.Problem ?? "No track shape to show.";
                }
            }

            PreviewTrackText.Text = text;
            PreviewTrackText.Visibility = text.Length == 0 ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        }

        private void Link_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception)
            {
            }

            e.Handled = true;
        }

        /// <summary>
        /// How the running game names the track, as SimHub delivers it: the values the track shapes are matched by
        /// (iRacing: track name and layout; other games: SimHub's game and track id).
        /// </summary>
        private void ShowTrackCheck()
        {
            if (Plugin == null || Plugin.PluginManager == null)
            {
                TrackCheckText.Text = "-";
                return;
            }

            string game = Property("DataCorePlugin.CurrentGame");
            System.Text.StringBuilder text = new System.Text.StringBuilder();
            text.AppendLine("Game".PadRight(24) + Shown(game));
            if (game.Length == 0)
            {
                text.Append("No game is running. Start a game and join a session.");
                TrackCheckText.Text = text.ToString();
                return;
            }

            string trackId = Property("DataCorePlugin.GameData.TrackId");
            text.AppendLine("Track name".PadRight(24) + Shown(Property("DataCorePlugin.GameData.TrackName")));
            text.AppendLine("Track id".PadRight(24) + Shown(trackId));
            text.AppendLine("Track config".PadRight(24) + Shown(Property("DataCorePlugin.GameData.TrackConfig")));
            text.AppendLine("Track code".PadRight(24) + Shown(Property("DataCorePlugin.GameData.TrackCode")));
            text.AppendLine("Track length".PadRight(24) + Shown(Property("DataCorePlugin.GameData.TrackLength")));
            if (game == "IRacing")
            {
                StandingsSnapshot snapshot = OverlayData.Current;
                text.AppendLine("iRacing track name".PadRight(24) + Shown(snapshot.TrackName));
                text.AppendLine("iRacing track config".PadRight(24) + Shown(snapshot.TrackConfig));
            }

            AppendTrackShape(text);
            TrackCheckText.Text = text.ToString().TrimEnd();
        }

        /// <summary>Which track shape the plugin picked for the running track, how, and from which source.</summary>
        private void AppendTrackShape(System.Text.StringBuilder text)
        {
            if (!TrackLibrary.IsReady)
            {
                text.AppendLine("Track shape".PadRight(24) + "loading ...");
                return;
            }

            StandingsSnapshot snapshot = OverlayData.Current;
            if (!snapshot.HasData)
            {
                text.AppendLine("Track shape".PadRight(24) + "- (no session data yet)");
                return;
            }

            TrackMatch match = TrackMapTracks.Live(snapshot);
            text.AppendLine("Match key".PadRight(24) + (match.Key.Length == 0 ? "-" : match.Key));
            if (match.Layout == null)
            {
                text.AppendLine("Track shape".PadRight(24) + "none, shown as " + (TrackMapOptions.Load().Ring ? "a ring" : "a line"));
                return;
            }

            TrackSource source = TrackLibrary.SourceOf(match.Layout);
            text.AppendLine("Track shape".PadRight(24) + match.Layout.Id + " - " + match.Layout.Title);
            text.AppendLine("Found by".PadRight(24) + match.How);
            text.AppendLine("Source".PadRight(24) + (source == null ? match.Layout.Source : source.DisplayName + " (" + source.License + ")"));
            text.AppendLine("Length difference".PadRight(24) + (double.IsNaN(match.LengthDifference)
                ? "-"
                : (match.LengthDifference * 100).ToString("+0.0;-0.0;0.0", System.Globalization.CultureInfo.InvariantCulture) + " %"));
        }

        private string Property(string name)
        {
            object value = null;
            try
            {
                value = Plugin.PluginManager.GetPropertyValue(name);
            }
            catch (Exception)
            {
            }

            return value == null ? string.Empty : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string Shown(string value)
        {
            return string.IsNullOrEmpty(value) ? "(missing)" : value;
        }

        /// <summary>In the preview a random driver is on the radio, a new one every few seconds.</summary>
        private void UpdateRadio()
        {
            bool radio = TrackMapOptions.Load().GetBool("Radio");
            if (!radio)
            {
                if (_radioTimer != null)
                {
                    _radioTimer.Stop();
                }

                _preview.TalkingCarIdx = -1;
                return;
            }

            if (_radioTimer == null)
            {
                _radioTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                _radioTimer.Tick += (s, args) => PickTalker();
            }

            if (!_radioTimer.IsEnabled)
            {
                PickTalker();
                _radioTimer.Start();
            }
        }

        private void PickTalker()
        {
            if (_preview == null)
            {
                return;
            }

            StandingsRow[] rows = Sample().Rows.Where(r => !r.OnPitRoad && r.TrackSurface != -1).ToArray();
            _preview.TalkingCarIdx = rows.Length == 0 ? -1 : rows[_random.Next(rows.Length)].CarIdx;
        }

        private StandingsSnapshot Sample()
        {
            double seconds = _previewDriving ? _driving.Elapsed.TotalSeconds + 0.001 : 0;
            return TrackMapSample.Create(seconds).As(OverlayData.PreviewFeatures(), OverlayData.SelectedGame);
        }

        /// <summary>What the marks mean, according to the choices below.</summary>
        private void ShowLegend()
        {
            TrackMapOptions options = TrackMapOptions.Load();
            List<string> parts = new List<string>();
            if (options.GetBool("LapRings"))
            {
                parts.Add("Red ring: a lap ahead of you. Blue ring: a lap behind you (races only).");
            }

            if (options.GetBool("Fastest"))
            {
                parts.Add("Violet ring: fastest lap in your class.");
            }

            if (options.GetBool("Radio"))
            {
                parts.Add("Waves: on the radio.");
            }

            if (options.Get("Pit") == "Dim")
            {
                parts.Add("Hollow circle: in the pit.");
            }

            LegendText.Text = string.Join(" ", parts);
            LegendText.Visibility = parts.Count == 0 ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        }

        private void PreviewMode_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            _previewDriving = sender == PreviewDriving;
            PreviewStill.IsChecked = !_previewDriving;
            PreviewDriving.IsChecked = _previewDriving;
            if (_timer == null)
            {
                _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                _timer.Tick += (s, args) =>
                {
                    if (_preview != null)
                    {
                        _preview.Update(Sample());
                    }
                };
            }

            if (_previewDriving)
            {
                _driving.Restart();
                _timer.Start();
            }
            else
            {
                _timer.Stop();
                _driving.Reset();
            }

            RefreshPreview();
        }

        // ---------------------------------------------------------------- settings

        private void ShowToggle_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            OverlaySettings.SetEnabled(OverlayData.TrackMapKey, ShowToggle.IsChecked == true);
        }

        private void ResetDesign_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            TrackMapOptions.Reset();
            BuildControls();
        }

        private void ResetGroup_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            string group = (string)((Button)sender).Tag;
            TrackMapOptions.ResetGroup(group);
            if (group == "Animation")
            {
                TrackMapOptions.ResetGroup("Radio");
            }

            BuildControls();
        }

        private void FillRates()
        {
            _filling = true;
            RateCombo.Items.Clear();
            RateCombo.Items.Add("Global (" + OverlaySettings.GlobalRate + " per second)");
            foreach (int rate in OverlaySettings.Rates)
            {
                RateCombo.Items.Add(rate + " per second");
            }

            int own = OverlaySettings.OverrideFor(OverlayData.TrackMapKey);
            RateCombo.SelectedIndex = own == 0 ? 0 : Math.Max(0, Array.IndexOf(OverlaySettings.Rates, own) + 1);
            _filling = false;
        }

        private void RateCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int index = RateCombo.SelectedIndex;
            if (_filling || index < 0)
            {
                return;
            }

            OverlaySettings.SetOverride(OverlayData.TrackMapKey, index == 0 ? 0 : OverlaySettings.Rates[index - 1]);
        }
    }
}
