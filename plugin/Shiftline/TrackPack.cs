using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Shiftline.Plugin
{
    /// <summary>
    /// The track shapes ship as a file of their own, not inside the DLL: Public Documents\Shiftline\Tracks\Shiftline.Tracks.zip,
    /// with Shiftline.Tracks.version next to it naming the plugin version the file belongs to. Every release carries its own
    /// track file; the installer and the in-plugin updater write it together with the plugin. When the file is missing or
    /// belongs to another version (an update by an older updater that only replaced the DLL, or a DLL copied by hand), the
    /// plugin downloads the file of its own release once at start. The Track Map loader reads the zip and notices a new file
    /// by itself; until then it shows a ring or a line.
    /// </summary>
    internal static class TrackPack
    {
        public const string ZipName = "Shiftline.Tracks.zip";
        public const string VersionName = "Shiftline.Tracks.version";

        public static string Folder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments), "Shiftline", "Tracks"); }
        }

        public static string ZipPath
        {
            get { return Path.Combine(Folder, ZipName); }
        }

        public static string VersionPath
        {
            get { return Path.Combine(Folder, VersionName); }
        }

        /// <summary>The plugin version the installed track file belongs to, or null when there is no file.</summary>
        public static string InstalledVersion
        {
            get
            {
                try
                {
                    return File.Exists(ZipPath) && File.Exists(VersionPath) ? File.ReadAllText(VersionPath).Trim() : null;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// Makes sure the track file of this plugin version is installed, downloading it from the plugin's own GitHub release
        /// when needed. Never throws; a failure is logged and tried again at the next start. Debug builds skip it: building
        /// puts the current data\tracks there (Shiftline.csproj).
        /// </summary>
        public static Task EnsureAsync()
        {
#if DEBUG
            return Task.CompletedTask;
#else
            return EnsureReleaseAsync();
#endif
        }

        private static async Task EnsureReleaseAsync()
        {
            string version = UpdateService.Installed.ToString();
            if (InstalledVersion == version)
            {
                return;
            }

            string baseUrl = "https://github.com/" + UpdateService.Repository + "/releases/download/v" + version + "/";
            try
            {
                Directory.CreateDirectory(Folder);
                string file = await DownloadAsync(baseUrl + ZipName, baseUrl + ZipName + ".sha256", ZipPath + ".download").ConfigureAwait(false);
                Install(file, version);
                PluginLog.Info("Track shapes of version " + version + " downloaded and installed.");
            }
            catch (Exception ex)
            {
                PluginLog.Info("The track shapes of version " + version + " could not be installed: " + ex.Message
                    + " The Track Map shows a ring or a line until they are there; the next start tries again.");
            }
        }

        /// <summary>Downloads a track file, checks that it is a zip and matches its checksum, and saves it at "target".</summary>
        internal static async Task<string> DownloadAsync(string zipUrl, string checksumUrl, string target)
        {
            byte[] data = await UpdateService.Http.GetByteArrayAsync(zipUrl).ConfigureAwait(false);
            if (data.Length < 1000 || data[0] != (byte)'P' || data[1] != (byte)'K')
            {
                throw new InvalidDataException("The downloaded track file is not a zip file.");
            }

            string expected = (await UpdateService.Http.GetStringAsync(checksumUrl).ConfigureAwait(false)).Trim().Split(' ', '\t', '\r', '\n')[0];
            string actual;
            using (SHA256 sha = SHA256.Create())
            {
                actual = BitConverter.ToString(sha.ComputeHash(data)).Replace("-", string.Empty);
            }

            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The checksum of the track file does not match the release.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.WriteAllBytes(target, data);
            return target;
        }

        /// <summary>Puts a downloaded track file in place in one step, then writes the version it belongs to.</summary>
        private static void Install(string downloaded, string version)
        {
            if (File.Exists(ZipPath))
            {
                File.Replace(downloaded, ZipPath, null);
            }
            else
            {
                File.Move(downloaded, ZipPath);
            }

            File.WriteAllText(VersionPath, version);
        }
    }
}
