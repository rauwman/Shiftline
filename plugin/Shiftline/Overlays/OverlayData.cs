using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using GameReaderCommon;
using SimHub.Plugins;

namespace Shiftline.Plugin
{
    /// <summary>
    /// The data side of the overlays. DataUpdate hands over to Update, which takes a standings snapshot at its own rate
    /// (not on every SimHub update) and publishes it as one immutable object. Overlay windows read Current and never
    /// touch SimHub's property system themselves. Works for iRacing only for now.
    /// </summary>
    public static class OverlayData
    {
        /// <summary>How many rows are published as flat properties (Shiftline.Standings.NN.*), for checking the data in SimHub.</summary>
        public const int FlatRows = 30;

        /// <summary>The name under which the standings overlay keeps its own settings.</summary>
        public const string StandingsKey = "Standings";

        /// <summary>The name under which the Battle Bar keeps its own settings.</summary>
        public const string BattleBarKey = "BattleBar";
        public const string RelativesKey = "Relatives";

        private static readonly RateClock StandingsClock = new RateClock(10);
        private static readonly RateClock DriverClock = new RateClock(0.5);

        private static StandingsSnapshot _current = StandingsSnapshot.Empty;
        private static List<DriverEntry> _drivers = new List<DriverEntry>();
        private static long _sequence;
        private static readonly SessionTracker Tracker = new SessionTracker();
        private static readonly SimHubReader Generic = new SimHubReader();

        private static string _selectedGame = string.Empty;
        private static string _selectedGameName = string.Empty;

        /// <summary>The game selected in SimHub (its code, for example "IRacing" or "F12025"), also while it does not run.</summary>
        public static string SelectedGame
        {
            get { return Volatile.Read(ref _selectedGame); }
        }

        /// <summary>The name SimHub shows for the selected game, for example "F1 25". Falls back to the code.</summary>
        public static string SelectedGameName
        {
            get { return Volatile.Read(ref _selectedGameName); }
        }

        /// <summary>
        /// The columns the preview and the edit-mode sample show: everything for iRacing (and when no game is selected), what
        /// the selected game delivered the last time it ran, or the usual set of SimHub's data for a game that never ran.
        /// </summary>
        /// <summary>The data is read as often as the fastest overlay that is switched on needs it (the standings count always).</summary>
        private static int DataRate()
        {
            int rate = OverlaySettings.RateFor(StandingsKey);
            foreach (string key in new[] { BattleBarKey, RelativesKey })
            {
                if (OverlaySettings.IsEnabled(key))
                {
                    rate = Math.Max(rate, OverlaySettings.RateFor(key));
                }
            }

            return rate;
        }

        public static StandingsFeatures PreviewFeatures()
        {
            string game = SelectedGame;
            if (string.IsNullOrEmpty(game) || string.Equals(game, "IRacing", StringComparison.OrdinalIgnoreCase))
            {
                return StandingsFeatures.All;
            }

            int? known = OverlaySettings.KnownFeatures(game);
            return known.HasValue ? (StandingsFeatures)known.Value : StandingsFeatures.Tyres | StandingsFeatures.Delta;
        }

        /// <summary>True when the preview's columns come from an earlier run of the selected game (not assumed).</summary>
        public static bool PreviewFeaturesKnown()
        {
            string game = SelectedGame;
            return string.IsNullOrEmpty(game) || string.Equals(game, "IRacing", StringComparison.OrdinalIgnoreCase)
                || OverlaySettings.KnownFeatures(game).HasValue;
        }

        /// <summary>The latest standings. Never null; the object never changes after it was returned.</summary>
        public static StandingsSnapshot Current
        {
            get { return Volatile.Read(ref _current); }
        }

        /// <summary>Called from DataUpdate on every update; cheap when it is not due.</summary>
        public static void Update(PluginManager pluginManager, GameData data)
        {
            try
            {
                string game = pluginManager.GetPropertyValue("DataCorePlugin.CurrentGame") as string;
                bool running = IRacingReader.ToInt(pluginManager.GetPropertyValue("DataCorePlugin.GameRunning"), 0) != 0;
                bool iracing = string.Equals(game, "IRacing", StringComparison.OrdinalIgnoreCase);
                GameFocus.Learn(pluginManager, game);
                if (!string.Equals(game ?? string.Empty, _selectedGame, StringComparison.Ordinal))
                {
                    string name = null;
                    try
                    {
                        name = pluginManager.GameManager?.GameDisplayName;
                    }
                    catch (Exception)
                    {
                        // older SimHub: keep the code
                    }

                    Volatile.Write(ref _selectedGameName, string.IsNullOrEmpty(name) ? game ?? string.Empty : name);
                    Volatile.Write(ref _selectedGame, game ?? string.Empty);
                }

                if (running && !iracing)
                {
                    // Every other game: SimHub's own opponent list.
                    StandingsClock.SetRate(DataRate());
                    if (StandingsClock.Due())
                    {
                        StandingsSnapshot built = Generic.Build(data, ++_sequence, Tracker);
                        Volatile.Write(ref _current, built);
                        if (built.HasData)
                        {
                            OverlaySettings.RememberFeatures(game, (int)built.Features);
                        }
                    }

                    return;
                }

                if (!running)
                {
                    if (Current.HasData)
                    {
                        Volatile.Write(ref _current, StandingsSnapshot.Empty);
                        _drivers = new List<DriverEntry>();
                        DriverClock.Reset();
                    }

                    return;
                }

                StandingsClock.SetRate(DataRate());
                if (!StandingsClock.Due())
                {
                    return;
                }

                Func<string, object> get = name => Read(pluginManager, name);
                if (DriverClock.Due())
                {
                    _drivers = IRacingReader.ReadDrivers(get);
                }

                Volatile.Write(ref _current, IRacingReader.Build(get, _drivers, ++_sequence, Tracker));
            }
            catch (Exception ex)
            {
                // Reading must never disturb the LED part of the plugin; the page shows the diagnostics.
                if (Interlocked.Increment(ref _errors) <= 3)
                {
                    PluginLog.Info("Overlay data: " + ex.Message);
                }
            }
        }

        private static int _errors;

        private static object Read(PluginManager pluginManager, string name)
        {
            try
            {
                return pluginManager.GetPropertyValue(name);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Publishes the check properties: Shiftline.Standings.Count, .Valid and .NN.* for the first rows.</summary>
        public static void Attach(Shiftline plugin)
        {
            plugin.AttachDelegate("Standings.Count", () => Current.Rows.Length);
            plugin.AttachDelegate("Standings.Sequence", () => Current.Sequence);
            plugin.AttachDelegate("Standings.Track", () => Current.Track);
            plugin.AttachDelegate("Standings.SessionType", () => Current.SessionType);

            KeyValuePair<string, Func<StandingsRow, object>>[] fields =
            {
                Field("Position", r => r.Position), Field("ClassPosition", r => r.ClassPosition), Field("Number", r => r.Number),
                Field("Name", r => r.Name), Field("CarIdx", r => r.CarIdx), Field("ClassName", r => r.ClassName),
                Field("ClassColor", r => r.ClassColor), Field("IRating", r => r.IRating), Field("License", r => r.License), Field("Make", r => r.Make), Field("Flair", r => r.Flair),
                Field("Laps", r => r.LapsCompleted), Field("LapDistPct", r => r.LapDistPct), Field("LastLap", r => r.LastLap),
                Field("BestLap", r => r.BestLap), Field("OnPitRoad", r => r.OnPitRoad), Field("IsPlayer", r => r.IsPlayer),
                Field("StartPosition", r => r.StartPosition), Field("PitLap", r => r.PitLap), Field("PitCount", r => r.PitCount), Field("StintLaps", r => r.StintLaps), Field("Towing", r => r.Towing), Field("F2Time", r => r.F2Time), Field("EstTime", r => r.EstTime)
            };

            for (int n = 1; n <= FlatRows; n++)
            {
                int index = n - 1;
                string prefix = "Standings." + n.ToString("00", CultureInfo.InvariantCulture) + ".";
                foreach (KeyValuePair<string, Func<StandingsRow, object>> field in fields)
                {
                    Func<StandingsRow, object> read = field.Value;
                    plugin.AttachDelegate(prefix + field.Key, () =>
                    {
                        StandingsRow[] rows = Current.Rows;
                        return index < rows.Length ? read(rows[index]) : null;
                    });
                }
            }
        }

        private static KeyValuePair<string, Func<StandingsRow, object>> Field(string name, Func<StandingsRow, object> read)
        {
            return new KeyValuePair<string, Func<StandingsRow, object>>(name, read);
        }
    }
}
