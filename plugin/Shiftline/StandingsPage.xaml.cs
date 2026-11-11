using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Shiftline.Plugin
{
    /// <summary>The Standings page. For now it shows what the data layer reads, so the values can be checked.</summary>
    public partial class StandingsPage : UserControl
    {
        private DispatcherTimer _timer;

        /// <summary>The overlay this page sets up: OverlayData.StandingsKey or OverlayData.RelativesKey.</summary>
        private readonly string _overlay;

        private bool IsRelatives
        {
            get { return _overlay == OverlayData.RelativesKey; }
        }

        public StandingsPage() : this(OverlayData.StandingsKey)
        {
        }

        public StandingsPage(string overlay)
        {
            _overlay = overlay;
            InitializeComponent();
            ApplyKind();
            _reorder = new ToggleReorder(this, ColumnPanel, keys =>
            {
                StandingsOptions.SetOrder(keys, _overlay);
                RefreshPreview();
            });
        }

        /// <summary>The same page serves the standings and the relatives; this words the parts that differ.</summary>
        private void ApplyKind()
        {
            if (!IsRelatives)
            {
                return;
            }

            SetTitle("Standings", "Relatives");
            PageHint.Text = "The cars right around you on the track: the ones in front, you, and the ones behind, with the gap in seconds. Place and size it in edit mode on the General page.";
            ResetButton.ToolTip = "Puts every design choice of the relatives back to the default: columns, cars, size, look and animation. Whether the overlay is shown, its position and its update rate stay.";
            ColumnsHint.Text = "Choose what each row shows. The gap is the time to you in seconds: minus for cars in front, plus for cars behind. White: same lap. Red: a car a lap up on you. Blue: a car you have lapped. In practice and qualifying all gaps are white.";
            SetTitle("Rows and size", "Size");
            SizeHint.Text = "How large the overlay is drawn. How many cars it shows follows from the cars in front and behind you, which you set below under Cars. In edit mode you can also pull the right or the bottom edge to change the size.";
            RowsLabel.Visibility = System.Windows.Visibility.Collapsed;
            RowsCombo.Visibility = System.Windows.Visibility.Collapsed;
            SizeLabel.Margin = new System.Windows.Thickness(0, 0, 12, 0);
            SetTitle("Classes", "Cars");
            ClassesHint.Text = "How many cars in front of you and behind you are shown. A missing car leaves an empty row, so you always stay in the same row.";
            AnimationHint.Text = "When you pass a car or a car passes you, its row slides to the new place. Choose Animation above the preview to see it.";
            LiveCard.Visibility = System.Windows.Visibility.Collapsed;
        }

        /// <summary>Changes the text of a card heading (found by its text; the headings have no names of their own).</summary>
        private void SetTitle(string from, string to)
        {
            IconTitle title = FindTitle(this, from);
            if (title != null)
            {
                title.Text = to;
            }
        }

        private static IconTitle FindTitle(System.Windows.DependencyObject parent, string text)
        {
            foreach (object child in System.Windows.LogicalTreeHelper.GetChildren(parent))
            {
                IconTitle title = child as IconTitle;
                if (title != null && title.Text == text)
                {
                    return title;
                }

                System.Windows.DependencyObject inner = child as System.Windows.DependencyObject;
                IconTitle found = inner == null ? null : FindTitle(inner, text);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        public StandingsPage(Shiftline plugin) : this(plugin, OverlayData.StandingsKey)
        {
        }

        public StandingsPage(Shiftline plugin, string overlay) : this(overlay)
        {
            Plugin = plugin;
            Loaded += (sender, args) =>
            {
                FillRates();
                ShowToggle.IsChecked = OverlaySettings.IsEnabled(_overlay);
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
            foreach (StandingsColumn column in StandingsOptions.OrderedColumns(_overlay))
            {
                System.Windows.Controls.Primitives.ToggleButton toggle = new System.Windows.Controls.Primitives.ToggleButton
                {
                    Content = column.Label,
                    Tag = column.Key,
                    Margin = new System.Windows.Thickness(0, 0, 8, 8),
                    Style = (System.Windows.Style)FindResource("GlassToggleButton"),
                    IsChecked = StandingsOptions.IsColumnOn(column.Key, _overlay)
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
                _reorder.Attach(toggle);
                toggle.Click += (sender, args) =>
                {
                    System.Windows.Controls.Primitives.ToggleButton clicked = (System.Windows.Controls.Primitives.ToggleButton)sender;
                    if (_reorder.SwallowClick(clicked))
                    {
                        return;
                    }

                    StandingsOptions.SetColumn((string)clicked.Tag, clicked.IsChecked == true, _overlay);
                    RefreshPreview();
                };
                ColumnPanel.Children.Add(toggle);
            }

            StandingsOptions options = StandingsOptions.Load(_overlay);
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

        private ToggleReorder _reorder;

        private void ResetOrder_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            StandingsOptions.ResetOrder(_overlay);
            BuildDesignControls();
        }

        private void RefreshPreview()
        {
            if (_preview == null)
            {
                _preview = new StandingsOverlay(true, _overlay);
                _preview.ColumnClicked += ShowColumnOptions;
                PreviewHost.Child = _preview;
            }

            StandingsFeatures features = OverlayData.PreviewFeatures();
            _preview.ApplyOptions(StandingsOptions.Load(_overlay).ForFeatures(features));
            ShowPreviewGame(features);

            System.Windows.Size size = _preview.DesignSize;
            _preview.Width = size.Width;
            _preview.Height = size.Height;
            _preview.Update(PreviewSample(PreviewData()));
        }

        /// <summary>The invented data of the preview: the field for the standings, cars spread around the lap for the relatives.</summary>
        private StandingsSnapshot PreviewData()
        {
            if (IsRelatives)
            {
                return _previewAnimation ? StandingsSample.Passing(_animationStep) : StandingsSample.OnTrack(StandingsSample.Create(_previewRace));
            }

            return _previewAnimation ? StandingsSample.Shuffled(_animationStep) : StandingsSample.Create(_previewRace);
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
                        _preview.Update(PreviewSample(PreviewData()));
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
            StandingsColumn info = Array.Find(StandingsOptions.ColumnsOf(_overlay), c => c.Key == column);
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
            StandingsOptions options = StandingsOptions.Load(_overlay);
            bool any = false;
            Action refreshSlots = null;
            List<ComboBox> slotCombos = new List<ComboBox>();
            foreach (StandingsChoice choice in StandingsOptions.AllChoices)
            {
                if (choice.Column != column || !StandingsOptions.AppliesTo(choice, _overlay))
                {
                    continue;
                }

                any = true;
                Grid row = new Grid { Margin = new System.Windows.Thickness(0, 0, 0, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new System.Windows.GridLength(260) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = System.Windows.GridLength.Auto });
                string hint;
                string shortLabel = StandingsOptions.SplitHint(choice.Label, out hint);
                TextBlock label = new TextBlock { Text = shortLabel, ToolTip = hint, VerticalAlignment = System.Windows.VerticalAlignment.Center, Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary") };
                ComboBox combo = new ComboBox { Width = 320, Height = 34, Tag = choice };
                foreach (string text in choice.Labels)
                {
                    string itemHint;
                    string shortText = StandingsOptions.SplitHint(text, out itemHint);
                    combo.Items.Add(new ComboBoxItem { Content = shortText, ToolTip = itemHint });
                }

                combo.SelectedIndex = Math.Max(0, Array.IndexOf(choice.Values, options.Get(choice.Key)));
                combo.SelectionChanged += (sender, args) =>
                {
                    ComboBox changed = (ComboBox)sender;
                    StandingsChoice picked = (StandingsChoice)changed.Tag;
                    if (changed.SelectedIndex >= 0)
                    {
                        StandingsOptions.SetChoice(picked.Key, picked.Values[changed.SelectedIndex], _overlay);
                        refreshSlots?.Invoke();
                        RefreshPreview();
                    }
                };
                if (StandingsOptions.IsHeaderSlot(choice.Key))
                {
                    slotCombos.Add(combo);
                }

                Grid.SetColumn(combo, 1);
                row.Children.Add(label);
                row.Children.Add(combo);
                panel.Children.Add(row);
            }

            // A place of the header can only show what no other place shows; taken entries are grey and cannot be picked.
            refreshSlots = HeaderSlotLock.Wire(slotCombos);
            refreshSlots();

            if (any && column == "Header")
            {
                Button reset = new Button { Style = (System.Windows.Style)FindResource("GlassButton"), Content = "Reset the header", HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = new System.Windows.Thickness(0, 4, 0, 0) };
                reset.Click += (sender, args) =>
                {
                    StandingsOptions.ResetGroup("Header", _overlay);
                    AddChoices(HeaderPanel, "Header");
                    RefreshPreview();
                };
                panel.Children.Add(reset);
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

            StandingsOptions.SetRows(StandingsOptions.RowChoices[RowsCombo.SelectedIndex], _overlay);
            RefreshPreview();
        }

        private void ResetDesign_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            StandingsOptions.Reset(_overlay);
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

            StandingsOptions.SetScale(StandingsOptions.ScaleChoices[ScaleCombo.SelectedIndex], _overlay);
            RefreshPreview();
        }

        private void FooterToggle_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            StandingsOptions.SetFooter(FooterToggle.IsChecked == true, _overlay);
            RefreshPreview();
        }

        private void HeaderToggle_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            StandingsOptions.SetHeader(HeaderToggle.IsChecked == true, _overlay);
            RefreshPreview();
        }

        private void ShowToggle_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            OverlaySettings.SetEnabled(_overlay, ShowToggle.IsChecked == true);
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

            int own = OverlaySettings.OverrideFor(_overlay);
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

            OverlaySettings.SetOverride(_overlay, index == 0 ? 0 : OverlaySettings.Rates[index - 1]);
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
