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
            };
            Unloaded += (sender, args) =>
            {
                if (_timer != null)
                {
                    _timer.Stop();
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
            AddChoices(LookPanel, "Look");
            PreviewStill.IsChecked = !_previewDriving;
            PreviewDriving.IsChecked = _previewDriving;
            _building = false;
            UpdateEnabled();
            RefreshPreview();
        }

        private void AddChoices(StackPanel panel, string group)
        {
            panel.Children.Clear();
            foreach (StandingsChoice choice in TrackMapOptions.AllChoices.Where(c => c.Column == group))
            {
                Grid row = new Grid { Margin = new System.Windows.Thickness(0, 0, 0, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new System.Windows.GridLength(300) });
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
            TrackMapOptions.ResetGroup((string)((Button)sender).Tag);
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
