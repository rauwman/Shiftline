using System;

namespace Shiftline.Plugin
{
    /// <summary>One car in the standings. Treated as immutable once it is part of a published snapshot.</summary>
    public sealed class StandingsRow
    {
        public int CarIdx { get; internal set; }
        public int Position { get; internal set; }          // overall, 0 = not classified yet
        public int ClassPosition { get; internal set; }     // inside the own class, 0 = not classified yet
        public string Number { get; internal set; }
        public string Name { get; internal set; }
        public int ClassId { get; internal set; }
        public string ClassName { get; internal set; }
        public string ClassColor { get; internal set; }     // as iRacing reports it, for example 0xffda59
        public int IRating { get; internal set; }
        public string License { get; internal set; }
        public string LicenseColor { get; internal set; }   // as iRacing reports it, for example 0x0153db
        public string Make { get; internal set; }           // three-letter make code, for example POR
        public int LapsCompleted { get; internal set; }
        public double LapDistPct { get; internal set; }     // 0..1, -1 = not on the track
        public double LastLap { get; internal set; }        // seconds, -1 = none
        public double BestLap { get; internal set; }        // seconds, -1 = none
        public bool OnPitRoad { get; internal set; }
        public int TrackSurface { get; internal set; }      // iRacing: -1 not in world, 0 off track, 1 pit stall, 2 pit road, 3 on track
        public double F2Time { get; internal set; }         // raw iRacing value, not checked yet
        public double EstTime { get; internal set; }        // raw iRacing value, not checked yet
        public bool IsPlayer { get; internal set; }
        public int StartPosition { get; internal set; }     // 0 = not known (not a race, or not started yet)
        public int PitLap { get; internal set; }            // lap of the last pit stop, 0 = none
        public int PitCount { get; internal set; }
        public int StintLaps { get; internal set; }         // laps since the last pit exit (or since the plugin saw the car)
        public bool Towing { get; internal set; }           // guessed, see SessionTracker
        public double PitLaneSeconds { get; internal set; } // time in the pit lane while on pit road, -1 otherwise
        public double[] RecentLaps { get; internal set; }   // last completed lap times, newest first (up to 5)
        public string TireType { get; internal set; }       // for example "Hard", "Wet"; empty when unknown

        /// <summary>Laps plus the part of the current lap: the order of cars that have no position yet.</summary>
        public double Progress
        {
            get { return LapsCompleted + Math.Max(LapDistPct, 0); }
        }
    }

    /// <summary>
    /// Everything an overlay needs to draw the standings, taken at one moment. The object is never changed after it is
    /// published, so any thread may read it without a lock.
    /// </summary>
    public sealed class StandingsSnapshot
    {
        public static readonly StandingsSnapshot Empty = new StandingsSnapshot
        {
            Rows = new StandingsRow[0],
            PlayerCarIdx = -1,
            Track = string.Empty,
            SessionType = string.Empty,
            CarName = string.Empty,
            TrackTemp = string.Empty,
            IncidentLimit = string.Empty,
            TimeRemaining = -1,
            Diagnostics = new string[0]
        };

        public StandingsRow[] Rows { get; internal set; }
        public int PlayerCarIdx { get; internal set; }
        public string Track { get; internal set; }
        public string SessionType { get; internal set; }
        public string CarName { get; internal set; }          // the player's car, short name
        public double TimeRemaining { get; internal set; }    // seconds, -1 = unknown
        public int LapsTotal { get; internal set; }           // 0 = not limited by laps
        public string TrackTemp { get; internal set; }        // as iRacing reports it at session start, for example "28.06 C"
        public int Incidents { get; internal set; }           // the player's incident points
        public string IncidentLimit { get; internal set; }    // for example "25" or "unlimited"
        public bool IsSample { get; internal set; }           // invented data for the preview
        public int StrengthOfField { get; internal set; }     // 0 = unknown

        public bool IsRace
        {
            get { return SessionType != null && SessionType.IndexOf("Race", StringComparison.OrdinalIgnoreCase) >= 0; }
        }
        public long Sequence { get; internal set; }
        public long TakenUtcTicks { get; internal set; }

        /// <summary>One line per raw source: its type and length, or that it is missing. For checking the data, not for drawing.</summary>
        public string[] Diagnostics { get; internal set; }

        public bool HasData
        {
            get { return Rows != null && Rows.Length > 0; }
        }
    }
}
