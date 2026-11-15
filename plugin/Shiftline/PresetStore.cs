using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Shiftline.Plugin
{
    /// <summary>The games a preset can be assigned to. Key "any" is the preset for every game that has none of its own.</summary>
    public class GameOption
    {
        public GameOption(string key, string label)
        {
            Key = key;
            Label = label;
        }

        public string Key { get; private set; }

        public string Label { get; private set; }

        private static readonly List<GameOption> Fixed = new List<GameOption>
        {
            new GameOption("any", "Any game"),
            new GameOption("AssettoCorsa", "Assetto Corsa"),
            new GameOption("AssettoCorsaCompetizione", "Assetto Corsa Competizione"),
            new GameOption("AssettoCorsaEVO", "Assetto Corsa Evo"),
            new GameOption("Automobilista2", "Automobilista 2"),
            new GameOption("F1", "F1 games"),
            new GameOption("IRacing", "iRacing"),
            new GameOption("LMU", "Le Mans Ultimate"),
            new GameOption("RFactor2", "rFactor 2"),
            new GameOption("RRRE", "RaceRoom"),
            new GameOption("BeamNgDrive", "BeamNG.drive")
        };

        /// <summary>The games to choose from: the usual ones, plus every other game SimHub has reported to this plugin.</summary>
        public static List<GameOption> All
        {
            get
            {
                List<GameOption> options = new List<GameOption>(Fixed);
                foreach (string raw in KnownGames.List())
                {
                    if (!options.Any(o => o.Key == raw))
                    {
                        options.Add(new GameOption(raw, Pretty(raw)));
                    }
                }

                return options;
            }
        }

        /// <summary>"DirtRally2" becomes "Dirt Rally 2".</summary>
        public static string Pretty(string raw)
        {
            string spaced = System.Text.RegularExpressions.Regex.Replace(raw ?? string.Empty, "(?<=[a-z])(?=[A-Z0-9])|(?<=[0-9])(?=[A-Z])", " ");
            return spaced.Length == 0 ? raw : spaced;
        }

        /// <summary>True for the games of the fixed list (they are not remembered separately).</summary>
        public static bool IsFixed(string key)
        {
            return Fixed.Any(o => o.Key == key);
        }

        /// <summary>The preset key of the game SimHub reports: all F1 games share one key, every other game is its SimHub name; null when there is none.</summary>
        public static string KeyOf(string simHubGame)
        {
            if (string.IsNullOrEmpty(simHubGame))
            {
                return null;
            }

            return simHubGame.StartsWith("F12") ? "F1" : simHubGame;
        }
    }

    /// <summary>What a preset file says about itself (the settings inside are read only when the preset is loaded).</summary>
    public class PresetInfo
    {
        public string Name { get; set; }

        public string Game { get; set; }

        public bool Favourite { get; set; }

        /// <summary>The car this preset is for (as SimHub names it); empty for every car.</summary>
        public string Car { get; set; }
    }

    /// <summary>
    /// Presets are small JSON files in PluginsData\Shiftline\Presets that anybody can read and edit in a text editor:
    /// { "Format": "Shiftline preset", "Version": 1, "Name": "...", "Game": "any", "Favourite": true, "Settings": { ... } }.
    /// A file without the "Settings" part is read as plain settings (the older settings profiles).
    /// </summary>
    public static class PresetStore
    {
        public const string Format = "Shiftline preset";

        public static string Folder
        {
            get { return Path.Combine(ProfileStore.RootFolder, "Presets"); }
        }

        private static string PathOf(string name)
        {
            return Path.Combine(Folder, name + ".json");
        }

        /// <summary>Moves the older settings profiles into the preset folder, once.</summary>
        public static void MigrateOldProfiles()
        {
            if (Directory.Exists(Folder) || !Directory.Exists(ProfileStore.ProfilesFolder))
            {
                return;
            }

            Directory.CreateDirectory(Folder);
            foreach (string file in Directory.GetFiles(ProfileStore.ProfilesFolder, "*.json"))
            {
                File.Copy(file, Path.Combine(Folder, Path.GetFileName(file)));
            }
        }

        public static bool Exists(string name)
        {
            return !string.IsNullOrEmpty(name) && File.Exists(PathOf(name));
        }

        /// <summary>All presets, favourites of each game first, then by name.</summary>
        public static List<PresetInfo> List()
        {
            List<PresetInfo> presets = new List<PresetInfo>();
            if (!Directory.Exists(Folder))
            {
                return presets;
            }

            foreach (string file in Directory.GetFiles(Folder, "*.json"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                PresetInfo info = new PresetInfo { Name = name, Game = "any", Favourite = false, Car = string.Empty };
                try
                {
                    JObject root = JObject.Parse(File.ReadAllText(file));
                    if (root["Settings"] != null)
                    {
                        info.Game = (string)root["Game"] ?? "any";
                        info.Favourite = root["Favourite"] != null && (bool)root["Favourite"];
                        info.Car = (string)root["Car"] ?? string.Empty;
                    }
                }
                catch (Exception ex)
                {
                    PluginLog.Info("Preset " + name + " could not be read: " + ex.Message);
                    continue;
                }

                presets.Add(info);
            }

            return presets.OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static Settings ReadSettings(string name)
        {
            JObject root = JObject.Parse(File.ReadAllText(PathOf(name)));
            JToken settingsPart = root["Settings"] ?? root;
            Settings settings = new Settings();
            JsonConvert.PopulateObject(settingsPart.ToString(), settings);
            settings.EnsureThemes();
            return settings;
        }

        /// <summary>Writes a preset. The game and favourite flag of an existing preset are kept unless given.</summary>
        public static void Save(string name, Settings settings, string game = null, bool? favourite = null, string car = null)
        {
            Directory.CreateDirectory(Folder);
            PresetInfo existing = Exists(name) ? List().FirstOrDefault(p => p.Name == name) : null;
            JObject root = new JObject
            {
                { "Format", Format },
                { "Version", 1 },
                { "Name", name },
                { "Game", game ?? (existing != null ? existing.Game : "any") },
                { "Favourite", favourite ?? (existing != null && existing.Favourite) },
                { "Car", car ?? (existing != null ? existing.Car : string.Empty) },
                { "Settings", JObject.FromObject(settings) }
            };
            File.WriteAllText(PathOf(name), root.ToString(Formatting.Indented));
        }

        /// <summary>Changes the game or the favourite flag without touching the settings inside.</summary>
        public static void SetMeta(string name, string game, bool favourite, string car = null)
        {
            JObject root = JObject.Parse(File.ReadAllText(PathOf(name)));
            if (root["Settings"] == null)
            {
                root = new JObject { { "Format", Format }, { "Version", 1 }, { "Name", name }, { "Settings", root } };
            }

            root["Game"] = game;
            root["Favourite"] = favourite;
            if (car != null)
            {
                root["Car"] = car;
            }

            File.WriteAllText(PathOf(name), root.ToString(Formatting.Indented));
        }

        /// <summary>Makes this preset the favourite of its game: the others of the same game lose the flag.</summary>
        public static void SetFavourite(string name, bool favourite)
        {
            List<PresetInfo> all = List();
            PresetInfo target = all.First(p => p.Name == name);
            if (favourite)
            {
                foreach (PresetInfo other in all.Where(p => p.Game == target.Game && p.Car == target.Car && p.Favourite && p.Name != name))
                {
                    SetMeta(other.Name, other.Game, false);
                }
            }

            SetMeta(name, target.Game, favourite);
        }

        public static void Delete(string name)
        {
            if (Exists(name))
            {
                File.Delete(PathOf(name));
            }
        }

        public static string UniqueName(string wanted)
        {
            string clean = ProfileStore.CleanName(wanted) ?? "Preset";
            string name = clean;
            for (int i = 2; Exists(name); i++)
            {
                name = clean + " " + i;
            }

            return name;
        }

        public static string Rename(string oldName, string newName)
        {
            string clean = ProfileStore.CleanName(newName);
            if (clean == null || clean == oldName)
            {
                return oldName;
            }

            if (Exists(clean))
            {
                throw new IOException("A preset named " + clean + " exists already.");
            }

            JObject root = JObject.Parse(File.ReadAllText(PathOf(oldName)));
            root["Name"] = clean;
            File.WriteAllText(PathOf(clean), root.ToString(Formatting.Indented));
            File.Delete(PathOf(oldName));
            return clean;
        }

        public static string Duplicate(string name)
        {
            string copy = UniqueName(name + " copy");
            JObject root = JObject.Parse(File.ReadAllText(PathOf(name)));
            root["Name"] = copy;
            root["Favourite"] = false;
            File.WriteAllText(PathOf(copy), root.ToString(Formatting.Indented));
            return copy;
        }

        public static void Export(string name, string targetPath)
        {
            File.Copy(PathOf(name), targetPath, true);
        }

        /// <summary>Copies a preset file into the preset folder under a free name; returns that name.</summary>
        public static string Import(string sourcePath)
        {
            JObject root = JObject.Parse(File.ReadAllText(sourcePath));
            string wanted = root["Settings"] != null && root["Name"] != null ? (string)root["Name"] : Path.GetFileNameWithoutExtension(sourcePath);
            string name = UniqueName(wanted);
            if (root["Settings"] == null)
            {
                root = new JObject { { "Format", Format }, { "Version", 1 }, { "Game", "any" }, { "Favourite", false }, { "Settings", root } };
            }

            root["Name"] = name;
            root["Favourite"] = false;
            Directory.CreateDirectory(Folder);
            File.WriteAllText(PathOf(name), root.ToString(Formatting.Indented));
            ReadSettings(name);              // proves that the file holds usable settings; throws otherwise
            return name;
        }

        /// <summary>
        /// The preset to use for a game and car. Best match first: the game and the car, then the game, then "any game" with the car,
        /// then "any game"; inside a group the favourite wins.
        /// </summary>
        public static string ChooseFor(string gameKey, string car = null)
        {
            List<PresetInfo> all = List();
            bool hasCar = !string.IsNullOrEmpty(car);
            Func<PresetInfo, bool> sameCar = p => hasCar && string.Equals(p.Car ?? string.Empty, car, StringComparison.OrdinalIgnoreCase);
            Func<PresetInfo, bool> noCar = p => string.IsNullOrEmpty(p.Car);
            Func<PresetInfo, bool>[] tiers =
            {
                p => gameKey != null && p.Game == gameKey && sameCar(p),
                p => gameKey != null && p.Game == gameKey && noCar(p),
                p => p.Game == "any" && sameCar(p),
                p => p.Game == "any" && noCar(p)
            };
            foreach (Func<PresetInfo, bool> tier in tiers)
            {
                PresetInfo found = all.Where(tier).OrderByDescending(p => p.Favourite).FirstOrDefault();
                if (found != null)
                {
                    return found.Name;
                }
            }

            return null;
        }
    }

    /// <summary>A car that SimHub has reported in a game.</summary>
    public class KnownCar
    {
        public string Game { get; set; }

        public string Name { get; set; }
    }

    /// <summary>
    /// The cars SimHub reported so far, per game (the list starts empty and grows while you play). It is kept in one file for all
    /// presets and is used for the car choice of a preset.
    /// </summary>
    public static class KnownCars
    {
        private static readonly object Gate = new object();
        private static List<KnownCar> _cars;

        private static string FilePath
        {
            get { return Path.Combine(ProfileStore.RootFolder, "KnownCars.json"); }
        }

        private static List<KnownCar> Load()
        {
            if (_cars == null)
            {
                try
                {
                    _cars = File.Exists(FilePath) ? JsonConvert.DeserializeObject<List<KnownCar>>(File.ReadAllText(FilePath)) ?? new List<KnownCar>() : new List<KnownCar>();
                }
                catch (Exception ex)
                {
                    PluginLog.Info("The list of known cars could not be read: " + ex.Message);
                    _cars = new List<KnownCar>();
                }
            }

            return _cars;
        }

        public static void Add(string game, string name)
        {
            if (string.IsNullOrEmpty(game) || string.IsNullOrEmpty(name))
            {
                return;
            }

            lock (Gate)
            {
                List<KnownCar> cars = Load();
                if (cars.Any(c => c.Game == game && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    return;
                }

                cars.Add(new KnownCar { Game = game, Name = name });
                try
                {
                    Directory.CreateDirectory(ProfileStore.RootFolder);
                    File.WriteAllText(FilePath, JsonConvert.SerializeObject(cars, Formatting.Indented));
                }
                catch (Exception ex)
                {
                    PluginLog.Info("The list of known cars could not be saved: " + ex.Message);
                }
            }
        }

        /// <summary>The car names of a game, sorted; for "any" the cars of every game.</summary>
        public static List<string> For(string game)
        {
            lock (Gate)
            {
                return Load().Where(c => game == "any" || c.Game == game)
                    .Select(c => c.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }
    }

    /// <summary>The games SimHub reported that are not in the fixed list; they show up in the game choice of a preset.</summary>
    public static class KnownGames
    {
        private static readonly object Gate = new object();
        private static List<string> _games;

        private static string FilePath
        {
            get { return Path.Combine(ProfileStore.RootFolder, "KnownGames.json"); }
        }

        private static List<string> Load()
        {
            if (_games == null)
            {
                try
                {
                    _games = File.Exists(FilePath) ? JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(FilePath)) ?? new List<string>() : new List<string>();
                }
                catch (Exception)
                {
                    _games = new List<string>();
                }
            }

            return _games;
        }

        public static List<string> List()
        {
            lock (Gate)
            {
                return new List<string>(Load());
            }
        }

        public static void Add(string simHubGame)
        {
            string key = GameOption.KeyOf(simHubGame);
            if (key == null || GameOption.IsFixed(key))
            {
                return;
            }

            lock (Gate)
            {
                List<string> games = Load();
                if (games.Contains(key))
                {
                    return;
                }

                games.Add(key);
                try
                {
                    Directory.CreateDirectory(ProfileStore.RootFolder);
                    File.WriteAllText(FilePath, JsonConvert.SerializeObject(games, Formatting.Indented));
                }
                catch (Exception)
                {
                    // the list is a convenience; failing to save it must never disturb the game
                }
            }
        }
    }
}
