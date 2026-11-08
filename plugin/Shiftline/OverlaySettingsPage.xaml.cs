using System;
using System.Text;
using System.Windows.Controls;
using System.Windows.Forms;

namespace Shiftline.Plugin
{
    /// <summary>General settings of the overlays. For now it lists the monitors.</summary>
    public partial class OverlaySettingsPage : System.Windows.Controls.UserControl
    {
        public OverlaySettingsPage()
        {
            InitializeComponent();
            Loaded += (sender, args) =>
            {
                ShowMonitors();
                FillRates();
            };
        }

        public OverlaySettingsPage(Shiftline plugin) : this()
        {
            Plugin = plugin;
        }

        public Shiftline Plugin { get; }

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
