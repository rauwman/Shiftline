using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Shiftline.Plugin
{
    /// <summary>A version such as 0.9.0 or 0.9.0-beta.2. A beta is older than the final version with the same numbers.</summary>
    public sealed class ReleaseVersion : IComparable<ReleaseVersion>
    {
        private static readonly Regex Pattern = new Regex(@"^v?(\d+)\.(\d+)\.(\d+)(?:-beta\.(\d+))?", RegexOptions.IgnoreCase);

        public int Major { get; set; }

        public int Minor { get; set; }

        public int Patch { get; set; }

        /// <summary>The beta number, or 0 for a final version.</summary>
        public int Beta { get; set; }

        public static ReleaseVersion Parse(string text)
        {
            Match match = Pattern.Match((text ?? string.Empty).Trim());
            if (!match.Success)
            {
                return null;
            }

            return new ReleaseVersion
            {
                Major = int.Parse(match.Groups[1].Value),
                Minor = int.Parse(match.Groups[2].Value),
                Patch = int.Parse(match.Groups[3].Value),
                Beta = match.Groups[4].Success ? int.Parse(match.Groups[4].Value) : 0
            };
        }

        public int CompareTo(ReleaseVersion other)
        {
            int result = Major.CompareTo(other.Major);
            if (result == 0)
            {
                result = Minor.CompareTo(other.Minor);
            }

            if (result == 0)
            {
                result = Patch.CompareTo(other.Patch);
            }

            if (result == 0)
            {
                // 0 means final, which is newer than every beta of the same numbers.
                int mine = Beta == 0 ? int.MaxValue : Beta;
                int theirs = other.Beta == 0 ? int.MaxValue : other.Beta;
                result = mine.CompareTo(theirs);
            }

            return result;
        }

        public override string ToString()
        {
            return Major + "." + Minor + "." + Patch + (Beta > 0 ? "-beta." + Beta : string.Empty);
        }
    }

    /// <summary>A release on GitHub that is newer than the installed plugin.</summary>
    public sealed class UpdateInfo
    {
        public ReleaseVersion Version { get; set; }

        public string Tag { get; set; }

        public string PageUrl { get; set; }

        public string DllUrl { get; set; }

        public string ChecksumUrl { get; set; }
    }

    /// <summary>
    /// Looks for a newer release on GitHub, downloads its Shiftline.Plugin.dll and replaces the installed one after SimHub has
    /// closed. SimHub locks the DLL while it runs, so a small PowerShell script waits for SimHub to exit, copies the new file
    /// (keeping the old one as a backup) and starts SimHub again.
    /// </summary>
    public static class UpdateService
    {
        private const string Repository = "rauwman/Shiftline";
        private const string DllName = "Shiftline.Plugin.dll";

        private static readonly HttpClient Http = CreateClient();

        /// <summary>The newest newer release found by the last check, or null.</summary>
        public static UpdateInfo Available { get; private set; }

        /// <summary>True once a check has finished in this session (successful or not).</summary>
        public static bool Checked { get; private set; }

        /// <summary>Text about the last check, for the Home page.</summary>
        public static string Status { get; private set; }

        public static string UpdateFolder
        {
            get { return Path.Combine(ProfileStore.RootFolder, "Update"); }
        }

        private static string StateFile
        {
            get { return Path.Combine(UpdateFolder, "update.json"); }
        }

        /// <summary>The installed version, taken from the informational version of the DLL (for example 0.9.0-beta.1).</summary>
        public static ReleaseVersion Installed
        {
            get
            {
                Assembly assembly = typeof(UpdateService).Assembly;
                var attribute = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(assembly, typeof(AssemblyInformationalVersionAttribute));
                ReleaseVersion version = ReleaseVersion.Parse(attribute == null ? null : attribute.InformationalVersion);
                if (version != null)
                {
                    return version;
                }

                Version plain = assembly.GetName().Version;
                return new ReleaseVersion { Major = plain.Major, Minor = plain.Minor, Patch = plain.Build };
            }
        }

        /// <summary>Whether the plugin looks for updates by itself (when SimHub starts). The choice is kept in its own small file.</summary>
        public static bool AutoCheck
        {
            get
            {
                try
                {
                    if (File.Exists(StateFile))
                    {
                        JObject state = JObject.Parse(File.ReadAllText(StateFile));
                        JToken value = state["AutoCheck"];
                        return value == null || value.Type != JTokenType.Boolean || (bool)value;
                    }
                }
                catch (Exception)
                {
                }

                return true;
            }

            set
            {
                try
                {
                    Directory.CreateDirectory(UpdateFolder);
                    File.WriteAllText(StateFile, new JObject { { "AutoCheck", value } }.ToString());
                }
                catch (Exception ex)
                {
                    PluginLog.Info("Could not save the update setting: " + ex.Message);
                }
            }
        }

        private static HttpClient CreateClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Shiftline-Updater");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }

        /// <summary>Asks GitHub for the releases and remembers the newest one that is newer than the installed plugin.</summary>
        public static async Task CheckAsync()
        {
            try
            {
                string json = await Http.GetStringAsync("https://api.github.com/repos/" + Repository + "/releases?per_page=10").ConfigureAwait(false);
                UpdateInfo best = null;
                foreach (JToken release in JArray.Parse(json))
                {
                    if ((bool?)release["draft"] == true)
                    {
                        continue;
                    }

                    string tag = (string)release["tag_name"];
                    ReleaseVersion version = ReleaseVersion.Parse(tag);
                    if (version == null || (best != null && version.CompareTo(best.Version) <= 0))
                    {
                        continue;
                    }

                    UpdateInfo info = new UpdateInfo { Version = version, Tag = tag, PageUrl = (string)release["html_url"] };
                    foreach (JToken asset in release["assets"] ?? new JArray())
                    {
                        string name = (string)asset["name"];
                        if (string.Equals(name, DllName, StringComparison.OrdinalIgnoreCase))
                        {
                            info.DllUrl = (string)asset["browser_download_url"];
                        }
                        else if (string.Equals(name, DllName + ".sha256", StringComparison.OrdinalIgnoreCase))
                        {
                            info.ChecksumUrl = (string)asset["browser_download_url"];
                        }
                    }

                    best = info;
                }

                if (best != null && best.Version.CompareTo(Installed) > 0)
                {
                    Available = best;
                    Status = best.DllUrl == null
                        ? "Version " + best.Version + " is available, but the release has no " + DllName + " attached."
                        : "Version " + best.Version + " is available.";
                }
                else
                {
                    Available = null;
                    Status = "You have the newest version.";
                }
            }
            catch (Exception ex)
            {
                Available = null;
                Status = "Could not check for updates: " + ex.Message;
                PluginLog.Info("Update check failed: " + ex.Message);
            }

            Checked = true;
        }

        /// <summary>
        /// Downloads the new DLL next to the update script and checks it: it must be a Shiftline.Plugin assembly with the version
        /// of the release, and match the checksum when the release has one. Returns the path of the file.
        /// </summary>
        public static async Task<string> DownloadAsync(UpdateInfo info)
        {
            Directory.CreateDirectory(UpdateFolder);
            string target = Path.Combine(UpdateFolder, DllName + ".new");
            byte[] data = await Http.GetByteArrayAsync(info.DllUrl).ConfigureAwait(false);
            if (data.Length < 20000)
            {
                throw new InvalidDataException("The downloaded file is too small to be the plugin.");
            }

            if (info.ChecksumUrl != null)
            {
                string expected = (await Http.GetStringAsync(info.ChecksumUrl).ConfigureAwait(false)).Trim().Split(' ', '\t', '\r', '\n')[0];
                string actual;
                using (SHA256 sha = SHA256.Create())
                {
                    actual = BitConverter.ToString(sha.ComputeHash(data)).Replace("-", string.Empty);
                }

                if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("The checksum of the download does not match the release.");
                }
            }

            File.WriteAllBytes(target, data);
            AssemblyName name = AssemblyName.GetAssemblyName(target);
            if (name.Name != "Shiftline.Plugin" || name.Version.Major != info.Version.Major || name.Version.Minor != info.Version.Minor || name.Version.Build != info.Version.Patch)
            {
                File.Delete(target);
                throw new InvalidDataException("The downloaded file does not match the plugin version of the release.");
            }

            return target;
        }

        /// <summary>
        /// Starts the script that replaces the installed DLL after SimHub has exited and starts SimHub again. It asks for
        /// administrator rights when the SimHub folder cannot be written to (for example under Program Files). The caller closes SimHub.
        /// Returns false when the script could not be started (for example the administrator question was cancelled).
        /// </summary>
        public static bool StartInstall(string newDll)
        {
            string target = typeof(UpdateService).Assembly.Location;
            string simHubExe = Process.GetCurrentProcess().MainModule.FileName;
            string script = Path.Combine(UpdateFolder, "apply-update.ps1");
            string previous = Path.Combine(UpdateFolder, "previous");
            Directory.CreateDirectory(previous);
            File.WriteAllText(script, ScriptText, new UTF8Encoding(true));

            string arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File " + Quote(script)
                + " -ProcessId " + Process.GetCurrentProcess().Id
                + " -Source " + Quote(newDll)
                + " -Target " + Quote(target)
                + " -Backup " + Quote(Path.Combine(previous, DllName))
                + " -Exe " + Quote(simHubExe)
                + " -Log " + Quote(Path.Combine(UpdateFolder, "update.log"));

            ProcessStartInfo start = new ProcessStartInfo("powershell.exe", arguments) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden };
            if (!IsWritable(Path.GetDirectoryName(target)))
            {
                start.Verb = "runas";
            }

            try
            {
                Process.Start(start);
                return true;
            }
            catch (Win32Exception ex)
            {
                PluginLog.Info("The update script was not started: " + ex.Message);
                return false;
            }
        }

        private static string Quote(string path)
        {
            return "\"" + path + "\"";
        }

        private static bool IsWritable(string folder)
        {
            try
            {
                string probe = Path.Combine(folder, "shiftline-write-test.tmp");
                File.WriteAllText(probe, "x");
                File.Delete(probe);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private const string ScriptText = @"param([int]$ProcessId, [string]$Source, [string]$Target, [string]$Backup, [string]$Exe, [string]$Log)
function Write-Log($text) { Add-Content -LiteralPath $Log -Value ((Get-Date -Format s) + ' ' + $text) }
try {
    Write-Log 'Waiting for SimHub to close'
    $process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if ($process) {
        if (-not $process.WaitForExit(20000)) {
            Write-Log 'SimHub did not close in time, ending it'
            Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
            $null = $process.WaitForExit(15000)
        }
    }
    Write-Log 'SimHub has closed'
    $done = $false
    for ($i = 0; $i -lt 60 -and -not $done; $i++) {
        try {
            Copy-Item -LiteralPath $Target -Destination $Backup -Force
            Copy-Item -LiteralPath $Source -Destination $Target -Force
            $done = $true
        } catch {
            Start-Sleep -Milliseconds 500
        }
    }
    if ($done) {
        Write-Log 'Plugin replaced, the old file is kept as a backup'
        Remove-Item -LiteralPath $Source -Force -ErrorAction SilentlyContinue
        Write-Log 'Starting SimHub'
        Start-Process explorer.exe -ArgumentList ('""' + $Exe + '""')
    } else {
        Write-Log 'The plugin could not be replaced (the file stayed locked)'
    }
} catch {
    Write-Log ('Error: ' + $_)
}
";
    }
}
