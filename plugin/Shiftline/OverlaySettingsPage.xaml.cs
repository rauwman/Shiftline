using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;

namespace Shiftline.Plugin
{
    /// <summary>General settings of the overlays: edit mode, when they show, positions, monitors, and the update rate and look of all overlays.</summary>
    public partial class OverlaySettingsPage : System.Windows.Controls.UserControl
    {
        public OverlaySettingsPage()
        {
            InitializeComponent();
#if DEBUG
            AddHtmlTestCard();
#endif
            Loaded += (sender, args) =>
            {
                ShowMonitors();
                FillMainSettings();
                FillMargins();
                EditToggle.IsChecked = OverlayHost.EditMode;
                FrontToggle.IsChecked = OverlaySettings.OnlyWhenGameInFront;
                ReplayToggle.IsChecked = OverlaySettings.ShowInReplays;
                GarageToggle.IsChecked = OverlaySettings.ShowInGarage;
                OverlayHost.EditModeChanged += OnEditModeChanged;
            };
            Unloaded += (sender, args) => OverlayHost.EditModeChanged -= OnEditModeChanged;
        }

        public OverlaySettingsPage(Shiftline plugin) : this()
        {
            Plugin = plugin;
        }

        public Shiftline Plugin { get; }

        private void OnEditModeChanged()
        {
            EditToggle.IsChecked = OverlayHost.EditMode;
        }

        private void EditToggle_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            OverlayHost.SetEditMode(EditToggle.IsChecked == true);
        }

        private void FrontToggle_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            OverlaySettings.OnlyWhenGameInFront = FrontToggle.IsChecked == true;
        }

        private void ReplayToggle_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            OverlaySettings.ShowInReplays = ReplayToggle.IsChecked == true;
        }

        private void GarageToggle_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            OverlaySettings.ShowInGarage = GarageToggle.IsChecked == true;
        }

        private void ResetPositions_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            OverlayHost.ResetPositions();
        }

        private bool _filling;

        /// <summary>The settings all overlays share: the update rate and the look (opacity, font, weight, boxes).</summary>
        private void FillMainSettings()
        {
            _filling = true;
            MainSettingsPanel.Children.Clear();

            System.Windows.Controls.ComboBox rate = new System.Windows.Controls.ComboBox { Width = 320, Height = 34 };
            foreach (int value in OverlaySettings.Rates)
            {
                rate.Items.Add(value + " per second");
            }

            rate.SelectedIndex = Math.Max(0, Array.IndexOf(OverlaySettings.Rates, OverlaySettings.GlobalRate));
            rate.SelectionChanged += (sender, args) =>
            {
                if (!_filling && rate.SelectedIndex >= 0)
                {
                    OverlaySettings.GlobalRate = OverlaySettings.Rates[rate.SelectedIndex];
                }
            };
            AddMainRow("Update rate", rate);

            System.Windows.Controls.ComboBox frames = new System.Windows.Controls.ComboBox { Width = 320, Height = 34 };
            foreach (int value in OverlaySettings.FrameRates)
            {
                frames.Items.Add(value == 0 ? "Unlimited" : value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            frames.SelectedIndex = Math.Max(0, Array.IndexOf(OverlaySettings.FrameRates, OverlaySettings.FrameRate));
            frames.SelectionChanged += (sender, args) =>
            {
                if (!_filling && frames.SelectedIndex >= 0)
                {
                    OverlaySettings.FrameRate = OverlaySettings.FrameRates[frames.SelectedIndex];
                }
            };
            AddMainRow("Overlay frame rate", frames, "Lower saves game fps.");

            System.Windows.Controls.ComboBox gpu = new System.Windows.Controls.ComboBox { Width = 320, Height = 34 };
            gpu.Items.Add("Normal");
            gpu.Items.Add("Realtime");
            gpu.SelectedIndex = OverlaySettings.GpuRealtime ? 1 : 0;
            gpu.SelectionChanged += (sender, args) =>
            {
                if (!_filling && gpu.SelectedIndex >= 0)
                {
                    OverlaySettings.GpuRealtime = gpu.SelectedIndex == 1;
                }
            };
            AddMainRow("GPU priority", gpu, "Realtime if overlays stutter in games like AMS2. Costs game fps; use it with a frame rate of 30 or 60.");

            foreach (StandingsChoice choice in LookSettings.General)
            {
                System.Windows.Controls.ComboBox combo = new System.Windows.Controls.ComboBox { Width = 320, Height = 34, Tag = choice };
                foreach (string text in choice.Labels)
                {
                    combo.Items.Add(text);
                }

                combo.SelectedIndex = Math.Max(0, Array.IndexOf(choice.Values, LookSettings.GeneralValue(choice.Key)));
                combo.SelectionChanged += (sender, args) =>
                {
                    System.Windows.Controls.ComboBox changed = (System.Windows.Controls.ComboBox)sender;
                    StandingsChoice picked = (StandingsChoice)changed.Tag;
                    if (!_filling && changed.SelectedIndex >= 0)
                    {
                        LookSettings.SetGeneral(picked.Key, picked.Values[changed.SelectedIndex]);
                    }
                };
                AddMainRow(choice.Label, combo);
            }

            System.Windows.Controls.Button reset = new System.Windows.Controls.Button
            {
                Style = (Style)FindResource("GlassButton"),
                Content = "Reset main settings",
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                Margin = new Thickness(0, 8, 0, 0),
                ToolTip = "Puts the update rate, frame rate, GPU priority and the look of General back to the defaults. Overlays with a value of their own keep it."
            };
            reset.Click += (sender, args) => ResetMainSettings();
            MainSettingsPanel.Children.Add(reset);
            _filling = false;
        }

        private void ResetMainSettings()
        {
            OverlaySettings.ClearOptions(LookSettings.GeneralKey);
            OverlaySettings.GlobalRate = OverlaySettings.DefaultRate;
            OverlaySettings.FrameRate = OverlaySettings.DefaultFrameRate;
            OverlaySettings.GpuRealtime = false;
            FillMainSettings();
        }

        private void AddMainRow(string label, System.Windows.Controls.ComboBox combo, string hint = null)
        {
            Grid row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary") };
            Grid.SetColumn(combo, 1);
            row.Children.Add(text);
            row.Children.Add(combo);
            if (!string.IsNullOrEmpty(hint))
            {
                row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                TextBlock note = new TextBlock { Text = hint, Style = (Style)FindResource("CardHint"), MaxWidth = 320, TextWrapping = TextWrapping.Wrap };
                Grid.SetRow(note, 1);
                Grid.SetColumn(note, 1);
                row.Children.Add(note);
            }

            MainSettingsPanel.Children.Add(row);
        }

        private void FillMargins()
        {
            _filling = true;
            MarginSlider.Value = OverlaySettings.AlignMargin;
            MarginText.Text = OverlaySettings.AlignMargin + " px";
            _filling = false;
        }

        private void MarginSlider_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            int margin = (int)Math.Round(MarginSlider.Value / 5) * 5;
            if (MarginText != null)
            {
                MarginText.Text = margin + " px";
            }

            if (!_filling && IsLoaded)
            {
                OverlaySettings.AlignMargin = margin;
            }
        }

        private void ShowMonitors()
        {
            StringBuilder text = new StringBuilder();
            int number = 1;
            foreach (Screen screen in Screen.AllScreens)
            {
                text.AppendLine(number + "  " + screen.Bounds.Width + " x " + screen.Bounds.Height + (screen.Primary ? "  (main screen)" : string.Empty));
                number++;
            }

            MonitorText.Text = text.ToString().TrimEnd();
        }

#if DEBUG
        /// <summary>
        /// TEST ONLY (Debug builds): the measuring step before the overlays move to HTML. Opens HTML test windows (HtmlBench)
        /// and shows how often the browser draws them, their longest gap and the memory of the browser.
        /// </summary>
        private void AddHtmlTestCard()
        {
            System.Windows.Media.Brush text = (System.Windows.Media.Brush)FindResource("TextPrimary");
            TextBlock status = new TextBlock { FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 12, Margin = new Thickness(0, 12, 0, 0), Foreground = text };
            System.Windows.Controls.Button start = TestButton();
            System.Windows.Controls.Button mouse = TestButton();
            System.Windows.Controls.Button count = TestButton();
            System.Windows.Controls.Button copy = TestButton();
            copy.Content = "Copy results";
            Action refresh = () =>
            {
                start.Content = HtmlBench.IsRunning ? "Stop HTML test" : "Start HTML test";
                mouse.Content = HtmlBench.Mouse ? "Mouse off" : "Mouse on (drag test)";
                mouse.IsEnabled = HtmlBench.IsRunning;
                count.Content = HtmlBench.Single ? "Windows: 1" : "Windows: 4";
                status.Text = HtmlBench.Status;
            };
            start.Click += (sender, args) =>
            {
                if (HtmlBench.IsRunning)
                {
                    HtmlBench.Stop();
                }
                else
                {
                    HtmlBench.Start();
                }

                refresh();
            };
            mouse.Click += (sender, args) =>
            {
                HtmlBench.Mouse = !HtmlBench.Mouse;
                refresh();
            };
            count.Click += (sender, args) =>
            {
                bool running = HtmlBench.IsRunning;
                HtmlBench.Stop();
                HtmlBench.Single = !HtmlBench.Single;
                if (running)
                {
                    HtmlBench.Start();
                }

                refresh();
            };
            copy.Click += (sender, args) => System.Windows.Clipboard.SetText(status.Text);

            WrapPanel buttons = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
            buttons.Children.Add(start);
            buttons.Children.Add(mouse);
            buttons.Children.Add(count);
            buttons.Children.Add(copy);
            StackPanel body = new StackPanel();
            body.Children.Add(new IconTitle { IconKey = "Flask", Text = "HTML test (developer)" });
            body.Children.Add(new TextBlock
            {
                Style = (Style)FindResource("CardHint"),
                Text = "Debug builds only. Opens test windows drawn by the browser engine, the size of the real overlays, with made-up cars. Frame rate and GPU priority come from the main settings above. Numbers also go to html-test.csv."
            });
            body.Children.Add(buttons);
            body.Children.Add(status);
            Cards.Children.Add(new Border { Style = (Style)FindResource("GlassCard"), Child = body });

            System.Windows.Threading.DispatcherTimer timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (sender, args) => refresh();
            Loaded += (sender, args) =>
            {
                refresh();
                timer.Start();
            };
            Unloaded += (sender, args) => timer.Stop();
        }

        private System.Windows.Controls.Button TestButton()
        {
            return new System.Windows.Controls.Button { Style = (Style)FindResource("GlassButton"), Margin = new Thickness(0, 0, 12, 8) };
        }
#endif
    }
}
