using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using SimHub.Plugins;

namespace Shiftline.Plugin
{
    /// <summary>
    /// Tells whether the game's window is in front. The game's process names come from SimHub itself (each game is set up
    /// with its executables in SimHub, which uses them to detect the running game), so every game SimHub knows works without
    /// a list of our own. The process of the foreground window is looked up only when the foreground window changes, so
    /// the check is cheap enough to run at every overlay update.
    /// </summary>
    internal static class GameFocus
    {
        private sealed class Known
        {
            public Known(string game, HashSet<string> processes)
            {
                Game = game;
                Processes = processes;
            }

            public string Game { get; }

            public HashSet<string> Processes { get; }
        }

        // Processes SimHub counts as the game that are not the game's window, e.g. iRacing's menu (iRacingUI), which
        // keeps running next to the sim. The overlays belong on the driving window only.
        private static readonly HashSet<string> NotTheGame = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "iRacingUI"
        };

        private static Known _known = new Known(null, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        private static IntPtr _lastWindow;
        private static string _lastProcess = string.Empty;

        /// <summary>
        /// Takes the process names of the current game from SimHub's game manager. Called from the data thread; does work
        /// only when the game changes (or until SimHub has handed out the names).
        /// </summary>
        public static void Learn(PluginManager pluginManager, string game)
        {
            Known known = Volatile.Read(ref _known);
            if (string.Equals(known.Game, game, StringComparison.OrdinalIgnoreCase) && known.Processes.Count > 0)
            {
                return;
            }

            var processes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                // GameManagerBase<...>.GetProcesseNames() is public but the class is generic, hence reflection.
                object manager = pluginManager?.GameManager;
                MethodInfo method = manager?.GetType().GetMethod("GetProcesseNames", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                if (method?.Invoke(manager, null) is IEnumerable<string> names)
                {
                    foreach (string name in names)
                    {
                        if (!string.IsNullOrWhiteSpace(name) && !NotTheGame.Contains(name.Trim()))
                        {
                            processes.Add(name.Trim());
                        }
                    }
                }
            }
            catch (Exception)
            {
                // older or changed SimHub: no names, the overlays are then never hidden
            }

            Volatile.Write(ref _known, new Known(game, processes));
        }

        public static bool IsGameInFront(string game)
        {
            Known known = Volatile.Read(ref _known);
            // The names always belong to the game SimHub has selected (Learn runs at every data update), so the game
            // passed in is not compared: SimHub's game codes and GameData.GameName are not guaranteed to be spelled alike.
            if (known.Processes.Count == 0)
            {
                return true;                // names unknown: do not hide the overlays
            }

            return known.Processes.Contains(ForegroundProcess());
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
