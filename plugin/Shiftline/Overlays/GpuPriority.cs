using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Shiftline.Plugin
{
    /// <summary>
    /// GPU priority of the overlays (setting in General). Some games keep the graphics card fully busy (AMS2 at high settings);
    /// Windows then lets every other program wait and the overlays stutter. Realtime lets the overlays draw first, at the cost of
    /// some game frames per second (less at a lower overlay frame rate). Set with the D3DKMT scheduling priority class, the same
    /// call OBS uses; it works without administrator rights on the maintainer's PC (2026-11-17). Applies to SimHub's process
    /// (WPF overlays) and to the GPU process of the HTML overlays' browser (see HtmlHost).
    /// </summary>
    internal static class GpuPriority
    {
        public const int Normal = 2;
        public const int Realtime = 5;
        private static int _applied = Normal;

        /// <summary>What Windows answered for SimHub's process, for the settings page ("" when nothing went wrong).</summary>
        public static string Problem { get; private set; }

        /// <summary>The priority the setting asks for.</summary>
        public static int Wanted
        {
            get { return OverlaySettings.GpuRealtime ? Realtime : Normal; }
        }

        /// <summary>Applies the setting to SimHub's process and the browser's GPU process; does nothing when it is unchanged.</summary>
        public static void Apply()
        {
            int wanted = Wanted;
            if (wanted == _applied)
            {
                return;
            }

            _applied = wanted;
            using (Process own = Process.GetCurrentProcess())
            {
                Problem = Set(own.Handle, wanted);
            }

            HtmlHost.ApplyGpuPriority();
        }

        /// <summary>Back to normal when the plugin ends.</summary>
        public static void Reset()
        {
            if (_applied == Normal)
            {
                return;
            }

            _applied = Normal;
            try
            {
                using (Process own = Process.GetCurrentProcess())
                {
                    Set(own.Handle, Normal);
                }
            }
            catch (Exception)
            {
                // SimHub is closing
            }
        }

        /// <summary>Sets the priority of one process; returns "" or why Windows refused.</summary>
        public static string Set(IntPtr process, int priority)
        {
            try
            {
                int status = D3DKMTSetProcessSchedulingPriorityClass(process, priority);
                if (status != 0)
                {
                    string problem = "Windows refused the GPU priority (0x" + status.ToString("X8", CultureInfo.InvariantCulture) + ")";
                    PluginLog.Info(problem);
                    return problem;
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                PluginLog.Info("GPU priority: " + ex.Message);
                return ex.Message;
            }
        }

        [DllImport("gdi32.dll")]
        private static extern int D3DKMTSetProcessSchedulingPriorityClass(IntPtr process, int priority);
    }
}
