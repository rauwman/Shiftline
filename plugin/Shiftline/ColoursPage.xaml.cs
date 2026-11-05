using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Shiftline.Plugin
{
    /// <summary>The Colours page: create, duplicate, delete and edit colour themes.</summary>
    public partial class ColoursPage : UserControl
    {
        public ColoursPage()
        {
            InitializeComponent();
            Loaded += (sender, args) => SelectFirstTheme();
        }

        private Settings Data
        {
            get { return DataContext as Settings; }
        }

        private ColourTheme SelectedTheme
        {
            get { return ThemeList.SelectedItem as ColourTheme; }
        }

        private void SelectFirstTheme()
        {
            if (ThemeList.SelectedItem == null && ThemeList.Items.Count > 0)
            {
                ThemeList.SelectedIndex = 0;
            }
        }

        private string UniqueThemeName(string baseName)
        {
            string name = baseName;
            int counter = 2;
            while (Data.Themes.Any(theme => theme.Name == name))
            {
                name = baseName + " " + counter;
                counter++;
            }

            return name;
        }

        private void NewTheme_Click(object sender, RoutedEventArgs e)
        {
            ColourTheme theme = new ColourTheme();
            theme.Name = UniqueThemeName("New theme");
            theme.Stops.Add(new ColourStop("#00BFFF"));
            theme.Stops.Add(new ColourStop("#FF1493"));
            Data.Themes.Add(theme);
            ThemeList.SelectedItem = theme;
            PluginLog.Info("Colour theme created: " + theme.Name);
        }

        private void DuplicateTheme_Click(object sender, RoutedEventArgs e)
        {
            ColourTheme source = SelectedTheme;
            if (source == null)
            {
                return;
            }

            ColourTheme copy = source.Duplicate(UniqueThemeName(source.Name + " copy"));
            Data.Themes.Add(copy);
            ThemeList.SelectedItem = copy;
            PluginLog.Info("Colour theme duplicated: " + copy.Name);
        }

        private void DeleteTheme_Click(object sender, RoutedEventArgs e)
        {
            ColourTheme theme = SelectedTheme;
            if (theme == null || theme.IsBuiltIn || Data.Themes.Count <= 1)
            {
                return;
            }

            Data.Themes.Remove(theme);
            Data.EnsureThemes();      // selections that pointed to the deleted theme move to an existing one
            ThemeList.SelectedIndex = 0;
            PluginLog.Info("Colour theme deleted: " + theme.Name);
        }

        private void AddStop_Click(object sender, RoutedEventArgs e)
        {
            ColourTheme theme = SelectedTheme;
            if (theme == null || theme.IsBuiltIn || theme.Stops.Count >= ColourTheme.MaxStops)
            {
                return;
            }

            string last = theme.Stops.Count > 0 ? theme.Stops[theme.Stops.Count - 1].Hex : "#FFFFFF";
            theme.Stops.Add(new ColourStop(last));
        }

        private void RemoveStop_Click(object sender, RoutedEventArgs e)
        {
            ColourTheme theme = SelectedTheme;
            ColourStop stop = ((FrameworkElement)sender).DataContext as ColourStop;
            if (theme == null || theme.IsBuiltIn || stop == null || theme.Stops.Count <= ColourTheme.MinStops)
            {
                return;
            }

            theme.Stops.Remove(stop);
        }
    }
}
