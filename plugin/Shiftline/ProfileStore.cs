using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Shiftline.Plugin
{
    /// <summary>
    /// Saves and loads complete sets of settings (settings profiles and backups) as JSON files in
    /// SimHub\PluginsData\Shiftline\Profiles and ...\Backups.
    /// </summary>
    public static class ProfileStore
    {
        public static string RootFolder
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PluginsData", "Shiftline"); }
        }

        public static string ProfilesFolder
        {
            get { return Path.Combine(RootFolder, "Profiles"); }
        }

        public static string BackupsFolder
        {
            get { return Path.Combine(RootFolder, "Backups"); }
        }

        /// <summary>Names (without extension) of the saved sets in a folder; newest backups first.</summary>
        public static List<string> List(string folder)
        {
            if (!Directory.Exists(folder))
            {
                return new List<string>();
            }

            List<string> names = Directory.GetFiles(folder, "*.json")
                .Select(path => Path.GetFileNameWithoutExtension(path))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (folder == BackupsFolder)
            {
                names.Reverse();
            }

            return names;
        }

        /// <summary>The name without characters that are not allowed in file names, or null when nothing is left.</summary>
        public static string CleanName(string name)
        {
            if (name == null)
            {
                return null;
            }

            string cleaned = new string(name.Where(c => Array.IndexOf(Path.GetInvalidFileNameChars(), c) < 0).ToArray()).Trim();
            return cleaned.Length == 0 ? null : cleaned;
        }

        public static void Save(Settings settings, string folder, string name)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(PathOf(folder, name), JsonConvert.SerializeObject(settings, Formatting.Indented));
        }

        /// <summary>Reads a saved set into a fresh Settings object (missing values keep their defaults).</summary>
        public static Settings Read(string folder, string name)
        {
            Settings settings = new Settings();
            JsonConvert.PopulateObject(File.ReadAllText(PathOf(folder, name)), settings);
            settings.EnsureThemes();
            return settings;
        }

        public static void Delete(string folder, string name)
        {
            string path = PathOf(folder, name);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        /// <summary>Writes a backup named after the current time and returns its name.</summary>
        public static string CreateBackup(Settings settings)
        {
            string name = "Backup " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss");
            Save(settings, BackupsFolder, name);
            return name;
        }

        private static string PathOf(string folder, string name)
        {
            return Path.Combine(folder, name + ".json");
        }
    }
}
