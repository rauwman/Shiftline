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
            System.Windows.Controls.ComboBox drawn = new System.Windows.Controls.ComboBox { Width = 320, Height = 34 };
            drawn.Items.Add("WPF");
            drawn.Items.Add("HTML");
            drawn.SelectedIndex = OverlaySettings.UseHtml ? 1 : 0;
            drawn.SelectionChanged += (sender, args) =>
            {
                if (!_filling && drawn.SelectedIndex >= 0)
                {
                    OverlaySettings.UseHtml = drawn.SelectedIndex == 1;
                }
            };
            AddMainRow("Drawn with", drawn, "HTML uses the browser engine of Windows (WebView2), more memory.");

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
                ToolTip = "Puts the update rate, frame rate, drawing, GPU priority and the look of General back to the defaults. Overlays with a value of their own keep it."
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
            OverlaySettings.UseHtml = false;
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
    }
}
