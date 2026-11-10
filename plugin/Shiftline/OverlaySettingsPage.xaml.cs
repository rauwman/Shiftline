using System;
using System.Text;
using System.Windows.Controls;
using System.Windows.Forms;

namespace Shiftline.Plugin
{
    /// <summary>General settings of the overlays: edit mode, when they show, positions, monitors and the update rate.</summary>
    public partial class OverlaySettingsPage : System.Windows.Controls.UserControl
    {
        public OverlaySettingsPage()
        {
            InitializeComponent();
            Loaded += (sender, args) =>
            {
                ShowMonitors();
                FillRates();
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

        private void FillRates()
        {
            _filling = true;
            RateCombo.Items.Clear();
            foreach (int rate in OverlaySettings.Rates)
            {
                RateCombo.Items.Add(rate + " per second");
            }

            RateCombo.SelectedIndex = Math.Max(0, Array.IndexOf(OverlaySettings.Rates, OverlaySettings.GlobalRate));
            _filling = false;
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

        private void RateCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_filling && RateCombo.SelectedIndex >= 0)
            {
                OverlaySettings.GlobalRate = OverlaySettings.Rates[RateCombo.SelectedIndex];
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
