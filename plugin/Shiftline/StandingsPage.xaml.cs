using System;
using System.Globalization;
using System.Text;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Shiftline.Plugin
{
    /// <summary>The Standings page. For now it shows what the data layer reads, so the values can be checked.</summary>
    public partial class StandingsPage : UserControl
    {
        private DispatcherTimer _timer;

        public StandingsPage()
        {
            InitializeComponent();
        }

        public StandingsPage(Shiftline plugin) : this()
        {
            Plugin = plugin;
            Loaded += (sender, args) =>
            {
                FillRates();
                ShowToggle.IsChecked = OverlaySettings.IsEnabled(OverlayData.StandingsKey);
                BuildDesignControls();
                Start();
            };
            Unloaded += (sender, args) =>
            {
                Stop();
                if (_animationTimer != null)
                {
                    _animationTimer.Stop();
                }
            };
        }

        public Shiftline Plugin { get; }

        // ---------------------------------------------------------------- design and preview

        private StandingsOverlay _preview;
        private bool _previewRace = true;
        private bool _building;

        private void BuildDesignControls()
        {
            _building = true;
            ColumnPanel.Children.Clear();
            foreach (StandingsColumn column in StandingsOptions.AllColumns)
            {
                System.Windows.Controls.Primitives.ToggleButton toggle = new System.Windows.Controls.Primitives.ToggleButton
                {
                    Content = column.Label,
                    Tag = column.Key,
                    Margin = new System.Windows.Thickness(0, 0, 8, 8),
                    Style = (System.Windows.Style)FindResource("GlassToggleButton"),
                    IsChecked = StandingsOptions.IsColumnOn(column.Key)
                };

                // A column the running game cannot fill stays as chosen, but is greyed out and hidden in the overlay.
                StandingsSnapshot live = OverlayData.Current;
                StandingsFeatures available = live.HasData ? live.Features : OverlayData.PreviewFeatures();
                if ((StandingsOptions.FeatureOf(column.Key) & ~available) != 0)
                {
                    toggle.IsEnabled = false;
                    toggle.Opacity = 0.45;
                    toggle.ToolTip = "Not available in " + OverlayData.SelectedGameName;
                    ToolTipService.SetShowOnDisabled(toggle, true);
                }
                toggle.Click += (sender, args) =>
                {
                    System.Windows.Controls.Primitives.ToggleButton clicked = (System.Windows.Controls.Primitives.ToggleButton)sender;
                    StandingsOptions.SetColumn((string)clicked.Tag, clicked.IsChecked == true);
                    RefreshPreview();
                };
                ColumnPanel.Children.Add(toggle);
            }

            StandingsOptions options = StandingsOptions.Load();
            RowsCombo.Items.Clear();
            foreach (int rows in StandingsOptions.RowChoices)
            {
                RowsCombo.Items.Add(rows + " rows");
            }

            RowsCombo.SelectedIndex = Math.Max(0, Array.IndexOf(StandingsOptions.RowChoices, options.Rows));
            HeaderToggle.IsChecked = options.Header;
            AddChoices(LookPanel, "Look");
            AddChoices(HeaderPanel, "Header");
            AddChoices(FooterPanel, "Footer");
            FooterToggle.IsChecked = options.Footer;
            AddChoices(ClassesPanel, "General");
            AddChoices(AnimationPanel, "Animation");
            ScaleCombo.Items.Clear();
            foreach (int percent in StandingsOptions.ScaleChoices)
            {
                ScaleCombo.Items.Add(percent + " %");
            }

            ScaleCombo.SelectedIndex = Math.Max(0, Array.IndexOf(StandingsOptions.ScaleChoices, (int)Math.Round(options.Scale * 100)));
            PreviewRace.IsChecked = _previewRace;
            PreviewPractice.IsChecked = !_previewRace;
            _building = false;
            RefreshPreview();
        }

        private void RefreshPreview()
        {
            if (_preview == null)
            {
                _preview = new StandingsOverlay(true);
                _preview.ColumnClicked += ShowColumnOptions;
                PreviewHost.Child = _preview;
            }

            StandingsFeatures features = OverlayData.PreviewFeatures();
            _preview.ApplyOptions(StandingsOptions.Load().ForFeatures(features));
            ShowPreviewGame(features);

            System.Windows.Size size = _preview.DesignSize;
            _preview.Width = size.Width;
            _preview.Height = size.Height;
            _preview.Update(PreviewSample(_previewAnimation ? StandingsSample.Shuffled(_animationStep) : StandingsSample.Create(_previewRace)));
        }

        /// <summary>The sample, made to look like the game selected in SimHub.</summary>
        private static StandingsSnapshot PreviewSample(StandingsSnapshot sample)
        {
            return sample.As(OverlayData.PreviewFeatures(), OverlayData.SelectedGame);
        }

        private void ShowPreviewGame(StandingsFeatures features)
        {
            string game = OverlayData.SelectedGameName;
            if (string.IsNullOrEmpty(game) || features == StandingsFeatures.All)
            {
                PreviewGameText.Text = string.IsNullOrEmpty(game) ? string.Empty : "Preview for " + game + ".";
            }
            else if (OverlayData.PreviewFeaturesKnown())
            {
                PreviewGameText.Text = "Preview for " + game + ". Columns the game does not provide are hidden.";
            }
            else
            {
                PreviewGameText.Text = "Preview for " + game + ". Columns the game probably does not provide are hidden; this is exact after the game ran once.";
            }

            PreviewGameText.Visibility = PreviewGameText.Text.Length == 0 ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        }

        private bool _previewAnimation;
        private int _animationStep;
        private DispatcherTimer _animationTimer;

        private void SetAnimationPreview(bool on)
        {
            _previewAnimation = on;
            if (_animationTimer == null)
            {
                _animationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                _animationTimer.Tick += (sender, args) =>
                {
                    _animationStep++;
                    if (_preview != null)
                    {
                        _preview.Update(PreviewSample(StandingsSample.Shuffled(_animationStep)));
                    }
                };
            }

            if (on)
            {
                _animationTimer.Start();
            }
            else
            {
                _animationTimer.Stop();
            }
        }

        private string _optionsColumn;

        /// <summary>Shows the choices of one column (for example the name format) under the preview.</summary>
        private void ShowColumnOptions(string column)
        {
            _optionsColumn = column;
            ColumnOptionsPanel.Children.Clear();
            StandingsColumn info = Array.Find(StandingsOptions.AllColumns, c => c.Key == column);
            ColumnOptionsTitle.Text = "Column options: " + (info == null ? column : info.Label);
            bool any = AddChoices(ColumnOptionsPanel, column);
            ColumnOptionsHint.Text = any
                ? "Click another column in the preview to change it."
                : "This column has no further options. Click another column in the preview.";
        }

        /// <summary>Adds one drop-down per choice of a column (or of a group such as "Animation"). False when there is none.</summary>
        private bool AddChoices(StackPanel panel, string column)
        {
            panel.Children.Clear();
            StandingsOptions options = StandingsOptions.Load();
            bool any = false;
            foreach (StandingsChoice choice in StandingsOptions.AllChoices)
            {
                if (choice.Column != column)
                {
                    continue;
                }

                any = true;
                Grid row = new Grid { Margin = new System.Windows.Thickness(0, 0, 0, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new System.Windows.GridLength(260) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = System.Windows.GridLength.Auto });
                TextBlock label = new TextBlock { Text = choice.Label, VerticalAlignment = System.Windows.VerticalAlignment.Center, Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary") };
                ComboBox combo = new ComboBox { Width = 320, Height = 34, Tag = choice };
                foreach (string text in choice.Labels)
                {
                    combo.Items.Add(text);
                }

                combo.SelectedIndex = Math.Max(0, Array.IndexOf(choice.Values, options.Get(choice.Key)));
                combo.SelectionChanged += (sender, args) =>
                {
                    ComboBox changed = (ComboBox)sender;
                    StandingsChoice picked = (StandingsChoice)changed.Tag;
                    if (changed.SelectedIndex >= 0)
                    {
                        StandingsOptions.SetChoice(picked.Key, picked.Values[changed.SelectedIndex]);
                        RefreshPreview();
                    }
                };
                Grid.SetColumn(combo, 1);
                row.Children.Add(label);
                row.Children.Add(combo);
                panel.Children.Add(row);
            }

            return any;
        }

        private void PreviewMode_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            bool animation = sender == PreviewAnimation;
            _previewRace = animation || sender == PreviewRace;
            PreviewRace.IsChecked = !animation && _previewRace;
            PreviewPractice.IsChecked = !_previewRace;
            PreviewAnimation.IsChecked = animation;
            SetAnimationPreview(animation);
            RefreshPreview();
        }

        private void RowsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_building || RowsCombo.SelectedIndex < 0)
            {
                return;
            }

            StandingsOptions.SetRows(StandingsOptions.RowChoices[RowsCombo.SelectedIndex]);
            RefreshPreview();
        }

        private void ResetDesign_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            StandingsOptions.Reset();
            BuildDesignControls();
            if (_optionsColumn != null)
            {
                ShowColumnOptions(_optionsColumn);
            }
        }

        private void ScaleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_building || ScaleCombo.SelectedIndex < 0)
            {
                return;
            }

            StandingsOptions.SetScale(StandingsOptions.ScaleChoices[ScaleCombo.SelectedIndex]);
            RefreshPreview();
        }

        private void FooterToggle_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            StandingsOptions.SetFooter(FooterToggle.IsChecked == true);
            RefreshPreview();
        }

        private void HeaderToggle_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            StandingsOptions.SetHeader(HeaderToggle.IsChecked == true);
            RefreshPreview();
        }

        private void ShowToggle_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            OverlaySettings.SetEnabled(OverlayData.StandingsKey, ShowToggle.IsChecked == true);
        }

        private bool _filling;

        private void FillRates()
        {
            _filling = true;
            RateCombo.Items.Clear();
            RateCombo.Items.Add("Global (" + OverlaySettings.GlobalRate + " per second)");
            foreach (int rate in OverlaySettings.Rates)
            {
                RateCombo.Items.Add(rate + " per second");
            }

            int own = OverlaySettings.OverrideFor(OverlayData.StandingsKey);
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

            OverlaySettings.SetOverride(OverlayData.StandingsKey, index == 0 ? 0 : OverlaySettings.Rates[index - 1]);
        }

        private void Start()
        {
            if (_timer == null)
            {
                _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                _timer.Tick += (sender, args) => Refresh();
            }

            Refresh();
            _timer.Start();
        }

        private void Stop()
        {
            if (_timer != null)
            {
                _timer.Stop();
            }
        }

        private void Refresh()
        {
            StandingsSnapshot snapshot = OverlayData.Current;
            string game = Plugin.PluginManager == null ? null : Plugin.PluginManager.GetPropertyValue("DataCorePlugin.CurrentGame") as string;

            StringBuilder summary = new StringBuilder();
            summary.AppendLine("Game".PadRight(22) + (string.IsNullOrEmpty(game) ? "-" : game));
            if (string.IsNullOrEmpty(game))
            {
                summary.Append("No game is running. Start a game and join a session.");
            }
            else if (!snapshot.HasData)
            {
                summary.AppendLine("Standings".PadRight(22) + "no data yet (no session loaded, or no cars in the list)");
                AppendDiagnostics(summary, snapshot);
            }
            else
            {
                double age = (DateTime.UtcNow.Ticks - snapshot.TakenUtcTicks) / (double)TimeSpan.TicksPerSecond;
                summary.AppendLine("Track".PadRight(22) + (snapshot.Track.Length == 0 ? "-" : snapshot.Track));
                summary.AppendLine("Session".PadRight(22) + (snapshot.SessionType.Length == 0 ? "-" : snapshot.SessionType));
                summary.AppendLine("Cars".PadRight(22) + snapshot.Rows.Length);
                summary.AppendLine("Own car index".PadRight(22) + snapshot.PlayerCarIdx);
                summary.AppendLine("Snapshot".PadRight(22) + "#" + snapshot.Sequence + ", " + age.ToString("0.0", CultureInfo.InvariantCulture) + " s old");
                AppendDiagnostics(summary, snapshot);
            }

            SummaryText.Text = summary.ToString().TrimEnd();
            TableText.Text = Table(snapshot);
        }

        private static void AppendDiagnostics(StringBuilder text, StandingsSnapshot snapshot)
        {
            foreach (string line in snapshot.Diagnostics)
            {
                text.AppendLine(line);
            }
        }

        private static string Table(StandingsSnapshot snapshot)
        {
            if (!snapshot.HasData)
            {
                return "-";
            }

            StringBuilder text = new StringBuilder();
            text.AppendLine("Pos Cls  #     Name                   Mk  Class     Lap  Dist%  Last      Best      Pit  iR");
            foreach (StandingsRow row in snapshot.Rows)
            {
                text.Append((row.Position > 0 ? row.Position.ToString(CultureInfo.InvariantCulture) : "-").PadRight(4));
                text.Append((row.ClassPosition > 0 ? row.ClassPosition.ToString(CultureInfo.InvariantCulture) : "-").PadRight(5));
                text.Append(Cut(row.Number, 5).PadRight(6));
                text.Append(Cut(row.Name, 21).PadRight(23));
                text.Append(Cut(row.Make, 3).PadRight(4));
                text.Append(Cut(row.ClassName, 8).PadRight(10));
                text.Append(row.LapsCompleted.ToString(CultureInfo.InvariantCulture).PadRight(5));
                text.Append((row.LapDistPct < 0 ? "-" : (row.LapDistPct * 100).ToString("0.0", CultureInfo.InvariantCulture)).PadRight(7));
                text.Append(Lap(row.LastLap).PadRight(10));
                text.Append(Lap(row.BestLap).PadRight(10));
                text.Append((row.TrackSurface < 0 ? "out" : row.OnPitRoad ? "yes" : string.Empty).PadRight(5));
                text.Append(row.IRating);
                text.AppendLine(row.IsPlayer ? "  <- you" : string.Empty);
            }

            return text.ToString().TrimEnd();
        }

        private static string Cut(string value, int length)
        {
            value = value ?? string.Empty;
            return value.Length <= length ? value : value.Substring(0, length);
        }

        private static string Lap(double seconds)
        {
            if (seconds <= 0)
            {
                return "-";
            }

            int minutes = (int)(seconds / 60);
            return minutes + ":" + (seconds - minutes * 60).ToString("00.000", CultureInfo.InvariantCulture);
        }
    }
}
