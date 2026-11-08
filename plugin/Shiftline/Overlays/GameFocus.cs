using System;
using System.Diagnostics;

namespace Shiftline.Plugin
{
    /// <summary>
    /// Tells whether the game's window is in front. The process of the foreground window is looked up only when the foreground
    /// window changes, so the check is cheap enough to run at every overlay update.
    /// </summary>
    internal static class GameFocus
    {
        private static IntPtr _lastWindow;
        private static string _lastProcess = string.Empty;

        /// <summary>Process names (without .exe) per SimHub game. Matched by start, so DX11 and DX12 builds both count.</summary>
        private static string ProcessPrefixFor(string game)
        {
            if (string.Equals(game, "IRacing", StringComparison.OrdinalIgnoreCase))
            {
                return "iRacingSim";
            }

            return null;
        }

        public static bool IsGameInFront(string game)
        {
            string prefix = ProcessPrefixFor(game);
            if (prefix == null)
            {
                return true;                // unknown game: do not hide the overlays
            }

            return ForegroundProcess().StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static string ForegroundProcess()
        {
            IntPtr window = NativeMethods.GetForegroundWindow();
            if (window == _lastWindow)
            {
                return _lastProcess;
            }

            _lastWindow = window;
            _lastProcess = string.Empty;
            try
            {
                uint pid;
                NativeMethods.GetWindowThreadProcessId(window, out pid);
                if (pid != 0)
                {
                    using (Process process = Process.GetProcessById((int)pid))
                    {
                        _lastProcess = process.ProcessName ?? string.Empty;
                    }
                }
            }
            catch (Exception)
            {
                // the process ended or is not accessible: treat as "not the game"
            }

            return _lastProcess;
        }
    }
}
