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
        private TrackMapHtmlView _preview;
        private System.Windows.FrameworkElement _previewElement;
        private DispatcherTimer _radioTimer;
        private DispatcherTimer _checkTimer;
        private bool _shapesShown;
        private DispatcherTimer _sampleTimer;      // watches how often the own position changes (data check)
        private double _lastPct = -2;
        private int _pctChanges;
        private int _pctRate;
        private long _feedSeen;
        private long _framesSeen;
        private long _feedRate;
        private long _frameRate;
        private readonly Random _random = new Random();
        private DispatcherTimer _timer;
        private bool _previewDriving;
        private bool _building;

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

                        _pctRate = _pctChanges;
                        _pctChanges = 0;
                        long feed = OverlayData.Positions.Sequence;
                        _feedRate = feed >= _feedSeen ? feed - _feedSeen : 0;
                        _feedSeen = feed;
                        long frames = TrackMapModel.FramesDrawn;
                        _frameRate = frames - _framesSeen;
                        _framesSeen = frames;
                        ShowTrackCheck();
                    };
                }

                TrackLibrary.EnsureLoading();
                if (_sampleTimer == null)
                {
                    _sampleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
                    _sampleTimer.Tick += (timer, tick) => SamplePosition();
                }

                _sampleTimer.Start();
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

                if (_sampleTimer != null)
                {
                    _sampleTimer.Stop();
                }

                ClosePreview();
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
            bool shapes = options.TrackShapes;
            bool ring = options.Ring || shapes;     // with track shapes on, a track without a shape is a ring of the track size
            SetRowEnabled("Shape", !shapes);
            SetRowEnabled("Orientation", !ring);
            SetRowEnabled("Length", !ring);
            SetRowEnabled("Range", !ring && options.Centred);
            SetRowEnabled("RingSize", ring && !shapes);
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
                // the shared preview (already loaded, see HtmlPreview) switches to the Track Map with its layout
                HtmlPreview host = HtmlPreview.Shared;
                TrackMapHtmlView view = new TrackMapHtmlView();
                view.Attach(host);
                host.MoveTo(PreviewHost);
                _preview = view;
                _previewElement = host;

                _preview.DesignSizeChanged += SizePreview;
            }

            StandingsFeatures features = OverlayData.PreviewFeatures();
            _preview.ApplyOptions(TrackMapOptions.Load());
            System.Windows.Size size = _preview.DesignSize;
            _previewElement.Width = size.Width;
            _previewElement.Height = size.Height;
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
            _previewElement.Width = size.Width;
            _previewElement.Height = size.Height;
        }

        private void ClosePreview()
        {
            if (_preview == null)
            {
                return;
            }

            _preview.DesignSizeChanged -= SizePreview;
            HtmlPreview html = _previewElement as HtmlPreview;
            if (html != null)
            {
                // the shared preview is kept: it goes back to its hidden spot, unless another page has taken it already
                ((TrackMapHtmlView)_preview).Detach();
                html.Release(PreviewHost);
            }
            else
            {
                PreviewHost.Child = null;
            }

            _preview = null;
            _previewElement = null;
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

        private void RecordPosition_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (TrackPositionLog.IsRecording)
            {
                TrackPositionLog.Stop();
            }
            else
            {
                TrackPositionLog.Start(120);
            }

            ShowTrackCheck();
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
            RecordButton.Content = TrackPositionLog.IsRecording ? "Stop recording and save" : "Record my position (2 minutes)";
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
            text.AppendLine("Car coordinates".PadRight(24) + Shown(Coordinates("DataCorePlugin.GameData.CarCoordinates")));
            if (game == "IRacing")
            {
                StandingsSnapshot snapshot = OverlayData.Current;
                text.AppendLine("iRacing track name".PadRight(24) + Shown(snapshot.TrackName));
                text.AppendLine("iRacing track config".PadRight(24) + Shown(snapshot.TrackConfig));
            }

            if (TrackPositionLog.IsRecording)
            {
                text.AppendLine("Recording".PadRight(24) + TrackPositionLog.SecondsLeft + " s left");
            }
            else if (TrackPositionLog.LastFile.Length > 0)
            {
                text.AppendLine("Recording saved".PadRight(24) + TrackPositionLog.LastFile);
            }

            StandingsRow me = OwnRow(OverlayData.Current);
            text.AppendLine("Your position".PadRight(24) + (me == null || me.LapDistPct < 0
                ? "-"
                : (me.LapDistPct * 100).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " % of the lap, changes " + _pctRate + " times per second"));
            text.AppendLine("Position feed".PadRight(24) + _feedRate + " updates per second");
            text.AppendLine("Map drawn".PadRight(24) + _frameRate + " times per second, "
                + (TrackMapModel.ShownDelay * 1000).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " ms behind the data");
            AppendTrackShape(text);
            TrackCheckText.Text = text.ToString().TrimEnd();
        }

        /// <summary>Counts how often the own position on the lap changes (some games deliver it less often than others).</summary>
        private void SamplePosition()
        {
            StandingsRow me = OwnRow(OverlayData.Current);
            double pct = me == null ? -1 : me.LapDistPct;
            if (Math.Abs(pct - _lastPct) > 1e-9)
            {
                _pctChanges++;
                _lastPct = pct;
            }
        }

        private static StandingsRow OwnRow(StandingsSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Rows == null)
            {
                return null;
            }

            return snapshot.Rows.FirstOrDefault(r => r.IsPlayer || r.CarIdx == snapshot.PlayerCarIdx);
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
            TrackAlignmentResult alignment = TrackAlignment.For(snapshot, match.Layout);
            text.AppendLine("SimHub map".PadRight(24) + (alignment.State == "looking" ? "looking ..." : alignment.Source.Length == 0 ? "none" : alignment.Source + " (" + System.IO.Path.GetFileName(alignment.File) + ")"));
            text.AppendLine("Start offset".PadRight(24) + (alignment.Usable
                ? (alignment.Offset * 100).ToString("+0.0;-0.0;0.0", System.Globalization.CultureInfo.InvariantCulture) + " %, fit "
                    + (alignment.Fit * 100).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " %"
                : alignment.State == "no fit"
                    ? "SimHub's map does not fit this shape (" + (alignment.Fit * 100).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " %), not moved"
                    : "0 %, not known yet"));
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

        /// <summary>A list of numbers (the car's position in the game world) as "x, y, z"; empty when missing.</summary>
        private string Coordinates(string name)
        {
            object value = null;
            try
            {
                value = Plugin.PluginManager.GetPropertyValue(name);
            }
            catch (Exception)
            {
            }

            System.Collections.IEnumerable list = value as System.Collections.IEnumerable;
            if (list == null || value is string)
            {
                return value == null ? string.Empty : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            }

            List<string> parts = new List<string>();
            foreach (object item in list)
            {
                parts.Add(Convert.ToDouble(item, System.Globalization.CultureInfo.InvariantCulture).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
            }

            return string.Join(", ", parts);
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
    }
}
