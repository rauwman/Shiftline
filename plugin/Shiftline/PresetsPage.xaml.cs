using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Shiftline.Plugin
{
    /// <summary>One line of the preset list.</summary>
    public class PresetRow : INotifyPropertyChanged
    {
        private string _game;
        private bool _favourite;
        private string _car = string.Empty;

        public string Name { get; set; }

        public bool IsActive { get; set; }

        public string Game
        {
            get { return _game; }
            set
            {
                if (_game != value && value != null)
                {
                    _game = value;
                    Raise("Game");
                    Raise("Badge");
                    Raise("CarOptions");
                }
            }
        }

        /// <summary>The car the preset is for; empty for every car.</summary>
        public string Car
        {
            get { return _car; }
            set
            {
                string text = (value ?? string.Empty).Trim();
                if (_car != text)
                {
                    _car = text;
                    Raise("Car");
                    Raise("Badge");
                }
            }
        }

        /// <summary>The choice of cars: "Any car" and the cars SimHub reported for the game (plus the car already set, so that it never disappears).</summary>
        public List<GameOption> CarOptions
        {
            get
            {
                List<GameOption> options = new List<GameOption> { new GameOption(string.Empty, "Any car") };
                List<string> names = KnownCars.For(Game ?? "any");
                if (!string.IsNullOrEmpty(Car) && !names.Any(n => string.Equals(n, Car, StringComparison.OrdinalIgnoreCase)))
                {
                    names.Add(Car);
                }

                options.AddRange(names.Select(n => new GameOption(n, n)));
                return options;
            }
        }

        public bool Favourite
        {
            get { return _favourite; }
            set
            {
                if (_favourite != value)
                {
                    _favourite = value;
                    Raise("Favourite");
                    Raise("Badge");
                }
            }
        }

        /// <summary>The small line under the name: active, and what the preset is loaded for.</summary>
        public string Badge
        {
            get
            {
                GameOption option = GameOption.All.FirstOrDefault(o => o.Key == Game);
                string game = option == null ? "Any game" : option.Label;
                string car = string.IsNullOrEmpty(Car) ? string.Empty : ", car " + Car;
                string part = Favourite ? "Loads with " + game + car + " (favourite)" : "Assigned to " + game + car;
                return IsActive ? "Active  -  " + part : part;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Raise(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(name));
            }
        }
    }

    /// <summary>The Presets page: create, rename, delete, favourite, assign to games, import and export presets.</summary>
    public partial class PresetsPage : UserControl
    {
        private bool _refreshing;

        public PresetsPage()
        {
            InitializeComponent();
        }

        public PresetsPage(Shiftline plugin) : this()
        {
            Plugin = plugin;
            Loaded += (sender, args) => RefreshList();
        }

        public Shiftline Plugin { get; }

        private Settings Data
        {
            get { return Plugin.Settings; }
        }

        private void RefreshList()
        {
            _refreshing = true;
            List<PresetRow> rows = new List<PresetRow>();
            foreach (PresetInfo info in PresetStore.List().OrderByDescending(p => p.Favourite).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                PresetRow row = new PresetRow { Name = info.Name, Game = info.Game, Favourite = info.Favourite, Car = info.Car, IsActive = info.Name == Data.ActiveProfile };
                row.PropertyChanged += Row_PropertyChanged;
                rows.Add(row);
            }

            PresetList.ItemsSource = rows;
            CurrentName.Text = Data.ActiveProfile;
            PresetInfo current = PresetStore.List().FirstOrDefault(p => p.Name == Data.ActiveProfile);
            GameOption option = current == null ? null : GameOption.All.FirstOrDefault(o => o.Key == current.Game);
            CurrentGame.Text = option == null ? string.Empty : "Assigned to: " + option.Label;
            _refreshing = false;
        }

        private void Row_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            PresetRow row = sender as PresetRow;
            if (_refreshing || row == null || (e.PropertyName != "Game" && e.PropertyName != "Favourite" && e.PropertyName != "Car"))
            {
                return;
            }

            try
            {
                PresetStore.SetMeta(row.Name, row.Game, row.Favourite, row.Car);
                if (e.PropertyName == "Favourite")
                {
                    PresetStore.SetFavourite(row.Name, row.Favourite);
                }

                Dispatcher.BeginInvoke(new Action(RefreshList));
            }
            catch (Exception ex)
            {
                PluginLog.Info("Changing the preset failed: " + ex.Message);
            }
        }

        private static PresetRow RowOf(object sender)
        {
            FrameworkElement element = sender as FrameworkElement;
            return element == null ? null : element.DataContext as PresetRow;
        }

        // ---------------------------------------------------------------- buttons

        private void NewPreset_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Plugin.SaveSettings();                                 // what was set up so far stays in the preset that is active now
                string name = PresetStore.UniqueName("Preset");
                PresetStore.Save(name, Data, "any", false);
                Data.ActiveProfile = name;                             // the new preset is the one to set up: it becomes the active one
                Plugin.SaveSettings();
                PluginLog.Info("Preset created from the current settings and made active: " + name);
            }
            catch (Exception ex)
            {
                PluginLog.Info("Creating the preset failed: " + ex.Message);
            }

            RefreshList();
        }

        private void Use_Click(object sender, RoutedEventArgs e)
        {
            PresetRow row = RowOf(sender);
            if (row != null)
            {
                Plugin.LoadPreset(row.Name);
                RefreshList();
            }
        }

        private void Duplicate_Click(object sender, RoutedEventArgs e)
        {
            PresetRow row = RowOf(sender);
            if (row == null)
            {
                return;
            }

            try
            {
                if (row.IsActive)
                {
                    Plugin.SaveSettings();
                }

                PluginLog.Info("Preset duplicated: " + PresetStore.Duplicate(row.Name));
            }
            catch (Exception ex)
            {
                PluginLog.Info("Duplicating the preset failed: " + ex.Message);
            }

            RefreshList();
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            PresetRow row = RowOf(sender);
            if (row == null)
            {
                return;
            }

            if (PresetStore.List().Count <= 1)
            {
                PluginLog.Info("The last preset cannot be deleted.");
                return;
            }

            if (!ConfirmDialog.Ask(this, "Delete preset", "Delete the preset \"" + row.Name + "\"?", "Delete", "Cancel"))
            {
                return;
            }

            try
            {
                PresetStore.Delete(row.Name);
                PluginLog.Info("Preset deleted: " + row.Name);
                if (row.IsActive)
                {
                    string next = PresetStore.ChooseFor(null) ?? PresetStore.List().First().Name;
                    Data.ActiveProfile = string.Empty;            // the deleted preset must not be written again
                    Plugin.LoadPreset(next);
                }
            }
            catch (Exception ex)
            {
                PluginLog.Info("Deleting the preset failed: " + ex.Message);
            }

            RefreshList();
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            PresetRow row = RowOf(sender);
            if (row == null)
            {
                return;
            }

            Microsoft.Win32.SaveFileDialog dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = row.Name + ".json",
                Filter = "Shiftline preset (*.json)|*.json",
                Title = "Export preset"
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                if (row.IsActive)
                {
                    Plugin.SaveSettings();
                }

                PresetStore.Export(row.Name, dialog.FileName);
                PluginLog.Info("Preset exported: " + dialog.FileName);
            }
            catch (Exception ex)
            {
                PluginLog.Info("Exporting the preset failed: " + ex.Message);
            }
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Shiftline preset (*.json)|*.json",
                Title = "Import preset"
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                PluginLog.Info("Preset imported: " + PresetStore.Import(dialog.FileName));
            }
            catch (Exception ex)
            {
                PluginLog.Info("Importing the preset failed (is it a Shiftline preset?): " + ex.Message);
            }

            RefreshList();
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(PresetStore.Folder);
                Process.Start(new ProcessStartInfo(PresetStore.Folder) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                PluginLog.Info("Could not open the folder: " + ex.Message);
            }
        }

        // ---------------------------------------------------------------- renaming

        private void Name_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Keyboard.ClearFocus();
                CommitName(sender);
            }
        }

        private void Name_LostFocus(object sender, RoutedEventArgs e)
        {
            CommitName(sender);
        }

        private void CommitName(object sender)
        {
            TextBox box = sender as TextBox;
            PresetRow row = RowOf(sender);
            if (box == null || row == null || _refreshing)
            {
                return;
            }

            string wanted = ProfileStore.CleanName(box.Text);
            if (wanted == null || wanted == row.Name)
            {
                box.Text = row.Name;
                return;
            }

            try
            {
                string name = PresetStore.Rename(row.Name, wanted);
                if (row.IsActive)
                {
                    Data.ActiveProfile = name;
                    Plugin.SaveSettings();
                }

                PluginLog.Info("Preset renamed: " + row.Name + " -> " + name);
            }
            catch (Exception ex)
            {
                PluginLog.Info("Renaming the preset failed: " + ex.Message);
            }

            RefreshList();
        }
    }
}
