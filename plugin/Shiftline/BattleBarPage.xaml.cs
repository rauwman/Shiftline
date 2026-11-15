using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Shiftline.Plugin
{
    /// <summary>The settings of the Battle Bar: what it shows, how it looks, and a live preview with invented data.</summary>
    public partial class BattleBarPage : UserControl
    {
        private BattleBarOverlay _preview;
        private bool _previewRace = true;
        private bool _previewAnimation;
        private int _animationStep;
        private DispatcherTimer _animationTimer;
        private bool _building;
        private bool _filling;

        public BattleBarPage()
        {
            InitializeComponent();
        }

        public BattleBarPage(Shiftline plugin) : this()
        {
            Plugin = plugin;
            Loaded += (sender, args) =>
            {
                ShowToggle.IsChecked = OverlaySettings.IsEnabled(OverlayData.BattleBarKey);
                FillRates();
                BuildControls();
            };
            Unloaded += (sender, args) =>
            {
                if (_animationTimer != null)
                {
                    _animationTimer.Stop();
                }
            };
        }

        public Shiftline Plugin { get; }

        private void BuildControls()
        {
            _building = true;
            ItemPanel.Children.Clear();
            StandingsFeatures available = OverlayData.Current.HasData ? OverlayData.Current.Features : OverlayData.PreviewFeatures();
            foreach (StandingsColumn item in BattleBarOptions.OrderedItems())
            {
                System.Windows.Controls.Primitives.ToggleButton toggle = new System.Windows.Controls.Primitives.ToggleButton
                {
                    Content = item.Label,
                    Tag = item.Key,
                    Margin = new System.Windows.Thickness(0, 0, 8, 8),
                    Style = (System.Windows.Style)FindResource("GlassToggleButton"),
                    IsChecked = BattleBarOptions.IsItemOn(item.Key)
                };

                // An item the game cannot fill stays as chosen, but is greyed out and hidden in the bar.
                if ((StandingsOptions.FeatureOf(item.Key) & ~available) != 0)
                {
                    toggle.IsEnabled = false;
                    toggle.Opacity = 0.45;
                    toggle.ToolTip = "Not available in " + OverlayData.SelectedGameName;
                    ToolTipService.SetShowOnDisabled(toggle, true);
                }

                toggle.Click += (sender, args) =>
                {
                    System.Windows.Controls.Primitives.ToggleButton clicked = (System.Windows.Controls.Primitives.ToggleButton)sender;

                    BattleBarOptions.SetItem((string)clicked.Tag, clicked.IsChecked == true);
                    RefreshPreview();
                };
                ItemPanel.Children.Add(toggle);
            }

            BattleBarOptions options = BattleBarOptions.Load();
            AddChoices(ModePanel, "Mode");
            AddChoices(NamePanel, "Name");
            AddChoices(FieldPanel, "Field");
            AddChoices(LookPanel, "Look");
            AddChoices(HeaderPanel, "Header");
            AddChoices(FooterPanel, "Footer");
            AddChoices(SidesPanel, "Sides");
            AddChoices(AnimationPanel, "Animation");
            HeaderToggle.IsChecked = options.Header;
            FooterToggle.IsChecked = options.Footer;
            ScaleCombo.Items.Clear();
            foreach (int percent in BattleBarOptions.ScaleChoices)
            {
                ScaleCombo.Items.Add(percent + " %");
            }

            ScaleCombo.SelectedIndex = Math.Max(0, Array.IndexOf(BattleBarOptions.ScaleChoices, (int)Math.Round(options.Scale * 100)));
            PreviewRace.IsChecked = !_previewAnimation && _previewRace;
            PreviewPractice.IsChecked = !_previewRace;
            PreviewAnimation.IsChecked = _previewAnimation;
            _building = false;
            RefreshPreview();
        }

        private void AddChoices(StackPanel panel, string group)
        {
            panel.Children.Clear();
            BattleBarOptions options = BattleBarOptions.Load();
            Action refreshSlots = null;
            List<ComboBox> slotCombos = new List<ComboBox>();
            foreach (StandingsChoice choice in BattleBarOptions.AllChoices.Where(c => c.Column == group))
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

                combo.SelectedIndex = Math.Max(0, Array.IndexOf(choice.Values, BattleBarOptions.Raw(choice)));
                combo.SelectionChanged += (sender, args) =>
                {
                    ComboBox changed = (ComboBox)sender;
                    StandingsChoice picked = (StandingsChoice)changed.Tag;
                    if (changed.SelectedIndex >= 0)
                    {
                        BattleBarOptions.SetChoice(picked.Key, picked.Values[changed.SelectedIndex]);
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

            // A place of the header can only show what no other place shows; taken entries are grey.
            refreshSlots = HeaderSlotLock.Wire(slotCombos);
            refreshSlots();
            if (group == "Header" && slotCombos.Count > 0)
            {
                Button reset = new Button { Style = (System.Windows.Style)FindResource("GlassButton"), Content = "Reset the header", HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = new System.Windows.Thickness(0, 4, 0, 0) };
                reset.Click += (sender, args) =>
                {
                    BattleBarOptions.ResetGroup("Header");
                    AddChoices(panel, group);
                    RefreshPreview();
                };
                panel.Children.Add(reset);
            }
        }

        // ---------------------------------------------------------------- preview

        private void RefreshPreview()
        {
            if (_preview == null)
            {
                _preview = new BattleBarOverlay(true);
                PreviewHost.Child = _preview;
            }

            StandingsFeatures features = OverlayData.PreviewFeatures();
            _preview.ApplyOptions(BattleBarOptions.Load().ForFeatures(features));
            System.Windows.Size size = _preview.DesignSize;
            _preview.Width = size.Width;
            _preview.Height = size.Height;
            string game = OverlayData.SelectedGameName;
            PreviewGameText.Text = string.IsNullOrEmpty(game) ? string.Empty
                : features == StandingsFeatures.All ? "Preview for " + game + "."
                : "Preview for " + game + ". Items the game does not provide are hidden.";
            PreviewGameText.Visibility = PreviewGameText.Text.Length == 0 ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
            ShowLegend();
            _preview.Update(Sample());

            // a bar wider than the page starts scrolled to the middle, where the player is
            Dispatcher.BeginInvoke(new Action(() => PreviewScroll.ScrollToHorizontalOffset(Math.Max(0, (PreviewScroll.ExtentWidth - PreviewScroll.ViewportWidth) / 2))), DispatcherPriority.Loaded);
        }

        /// <summary>What the frames mean, with the colour and the time chosen below.</summary>
        private void ShowLegend()
        {
            BattleBarOptions options = BattleBarOptions.Load();
            string frame = options.Get("Frame.Player");
            string name = frame == "BlueFlow" ? "Blue flow" : frame == "GreenFlow" ? "Green flow" : frame;
            string text = frame == "Off" ? string.Empty : name + " frame: you.";
            double limit = options.GetDouble("Battle");
            if (limit > 0)
            {
                text += (text.Length > 0 ? " " : string.Empty) + options.Get("Battle.Colour") + " frame: a car closer to you than "
                    + limit.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s (in races).";
            }

            LegendText.Text = text;
            LegendText.Visibility = text.Length == 0 ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        }

        private StandingsSnapshot Sample()
        {
            StandingsSnapshot sample = _previewAnimation ? StandingsSample.Shuffled(_animationStep, true) : StandingsSample.Create(_previewRace);
            if (BattleBarOptions.Load().Get("Sort") == "Track")
            {
                sample = StandingsSample.OnTrack(sample);
            }

            return sample.As(OverlayData.PreviewFeatures(), OverlayData.SelectedGame);
        }

        private void PreviewMode_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            bool animation = sender == PreviewAnimation;
            _previewRace = animation || sender == PreviewRace;
            _previewAnimation = animation;
            PreviewRace.IsChecked = !animation && _previewRace;
            PreviewPractice.IsChecked = !_previewRace;
            PreviewAnimation.IsChecked = animation;
            if (_animationTimer == null)
            {
                _animationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                _animationTimer.Tick += (s, args) =>
                {
                    _animationStep++;
                    if (_preview != null)
                    {
                        _preview.Update(Sample());
                    }
                };
            }

            if (animation)
            {
                _animationTimer.Start();
            }
            else
            {
                _animationTimer.Stop();
            }

            RefreshPreview();
        }

        // ---------------------------------------------------------------- settings

        private void ShowToggle_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            OverlaySettings.SetEnabled(OverlayData.BattleBarKey, ShowToggle.IsChecked == true);
        }

        private void ResetDesign_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            BattleBarOptions.Reset();
            BuildControls();
        }

        private void HeaderToggle_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            BattleBarOptions.SetHeader(HeaderToggle.IsChecked == true);
            RefreshPreview();
        }

        private void FooterToggle_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            BattleBarOptions.SetFooter(FooterToggle.IsChecked == true);
            RefreshPreview();
        }

        private void ScaleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_building || ScaleCombo.SelectedIndex < 0)
            {
                return;
            }

            BattleBarOptions.SetScale(BattleBarOptions.ScaleChoices[ScaleCombo.SelectedIndex] / 100.0);
            RefreshPreview();
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

            int own = OverlaySettings.OverrideFor(OverlayData.BattleBarKey);
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

            OverlaySettings.SetOverride(OverlayData.BattleBarKey, index == 0 ? 0 : OverlaySettings.Rates[index - 1]);
        }
    }
}
