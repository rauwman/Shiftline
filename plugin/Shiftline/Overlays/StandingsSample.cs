using System;
using System.Collections.Generic;

namespace Shiftline.Plugin
{
    /// <summary>Invented standings for the preview and for edit mode without a session. No real drivers.</summary>
    internal static class StandingsSample
    {
        private static readonly string[] Names =
        {
            "Lena Hartwig", "Marco Delvecchio", "Tom Ashby", "Ines Varga", "Jonas Brekke", "Akira Tanabe", "Paul Renaud",
            "Sofia Lindqvist", "(Player)", "Diego Ferreyra", "Hanna Kowal", "Lucas Moreau", "Rui Santos", "Emil Strand",
            "Clara Bianchi", "Felix Auer", "Nora Ellis", "Owen Price", "Mila Novak", "Sven Larsen", "Iris Kemp", "Ben Hollis",
            "Yara Haddad", "Tim Kessler", "Ada Quinn"
        };

        private static readonly string[] Makes = { "POR", "BMW", "FER", "MER", "AUD", "MCL", "LAM", "FOR", "AST", "CHE" };

        private static readonly string[] Licences = { "A", "B", "C", "A", "B", "D", "A", "C", "B", "A" };

        private static readonly string[] LicenceColors = { "0x0153db", "0x33cc00", "0xfeec04", "0x0153db", "0x33cc00", "0xfc8a27", "0x0153db", "0xfeec04", "0x33cc00", "0x0153db" };

        private static StandingsSnapshot _race;
        private static StandingsSnapshot _practice;

        /// <summary>The sample for a race or a practice session. Made once and then reused (snapshots never change).</summary>
        public static StandingsSnapshot Create(bool race)
        {
            if (race)
            {
                return _race ?? (_race = Build(true));
            }

            return _practice ?? (_practice = Build(false));
        }

        /// <summary>
        /// The race sample with two neighbouring cars swapped, a different pair at every step: drives the animation preview.
        /// </summary>
        public static StandingsSnapshot Shuffled(int step)
        {
            StandingsSnapshot race = Create(true);
            StandingsRow[] rows = new StandingsRow[race.Rows.Length];
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i] = race.Rows[i].Copy();
            }

            int pair = 1 + step * 3 % (rows.Length - 2);
            if (step % 2 == 1)
            {
                StandingsRow moved = rows[pair];
                rows[pair] = rows[pair + 1];
                rows[pair + 1] = moved;
            }

            for (int i = 0; i < rows.Length; i++)
            {
                rows[i].Position = i + 1;
            }

            return new StandingsSnapshot
            {
                Rows = rows,
                PlayerCarIdx = race.PlayerCarIdx,
                Track = race.Track,
                SessionType = race.SessionType,
                CarName = race.CarName,
                TimeRemaining = race.TimeRemaining,
                LapsTotal = race.LapsTotal,
                TrackTemp = race.TrackTemp,
                Incidents = race.Incidents,
                IncidentLimit = race.IncidentLimit,
                StrengthOfField = race.StrengthOfField,
                TrackShort = race.TrackShort,
                TimeOfDay = race.TimeOfDay,
                DeltaToBest = race.DeltaToBest,
                HasDelta = race.HasDelta,
                Features = StandingsFeatures.All,
                IsSample = true,
                Diagnostics = new string[0]
            };
        }

        private static StandingsSnapshot Build(bool race)
        {
            Random random = new Random(7);
            const int player = 8;
            List<StandingsRow> rows = new List<StandingsRow>();
            double gap = 0;
            double bestOverall = 85.400;
            int gt3 = 0;
            int gt4 = 0;
            for (int i = 0; i < Names.Length; i++)
            {
                bool second = i % 4 == 3;
                int classPosition = second ? ++gt4 : ++gt3;
                double best = bestOverall + i * 0.11 + random.NextDouble() * 0.2;
                double last = best + random.NextDouble() * 0.9;
                if (i > 0)
                {
                    gap += 0.4 + random.NextDouble() * 2.5;
                }

                bool lapped = race && i >= Names.Length - 3;
                rows.Add(new StandingsRow
                {
                    CarIdx = i + 1,
                    Position = i + 1,
                    ClassPosition = classPosition,
                    ClassId = second ? 2 : 1,
                    Number = (3 + i * 7 % 97).ToString(),
                    Name = Names[i],
                    Make = Makes[i % Makes.Length],
                    ClassColor = second ? "0x33ceff" : "0xffda59",
                    ClassName = second ? "GT4" : "GT3",
                    License = Licences[i % Licences.Length] + " " + (1.2 + random.NextDouble() * 3.4).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                    LicenseColor = LicenceColors[i % LicenceColors.Length],
                    IRating = 1200 + random.Next(0, 4800),
                    LapsCompleted = race ? (lapped ? 11 : 12) : 6,
                    LapDistPct = 0.5,
                    LastLap = i == 0 ? bestOverall : last,
                    BestLap = i == 0 ? bestOverall : best,
                    OnPitRoad = i == 5 || i == 13,
                    TrackSurface = 3,
                    F2Time = race ? gap : best,
                    IsPlayer = i == player,
                    StartPosition = race ? Math.Max(1, i + 1 + random.Next(-4, 5)) : 0,
                    StartClassPosition = race ? Math.Max(1, classPosition + random.Next(-2, 3)) : 0,
                    PitLap = i % 3 == 0 ? 7 + i % 4 : 0,
                    PitCount = i % 3 == 0 ? 1 : 0,
                    StintLaps = i % 3 == 0 ? 5 - i % 4 : 12,
                    Towing = race && i == 16,
                    PitLaneSeconds = i == 5 || i == 13 ? 23 + i : -1,
                    RecentLaps = new[] { last, last + 0.1 + random.NextDouble() * 0.4, last - 0.2 + random.NextDouble() * 0.5, last + random.NextDouble() * 0.3, last + 0.2 },
                    TireType = i % 7 == 3 ? "Soft" : i % 5 == 2 ? "Medium" : "Hard",
                    IRatingChange = race ? 60 - i * 7 + random.Next(-6, 7) : 0,
                    HasIRatingChange = race
                });
            }

            return new StandingsSnapshot
            {
                Rows = rows.ToArray(),
                PlayerCarIdx = player + 1,
                Track = "Sample Raceway",
                SessionType = race ? "Race" : "Practice",
                CarName = "Sample GT3",
                TimeRemaining = race ? -1 : 1834,
                LapsTotal = race ? 20 : 0,
                TrackTemp = "31.5 C",
                Incidents = 2,
                IncidentLimit = "25",
                IsSample = true,
                Features = StandingsFeatures.All,
                StrengthOfField = 3120,
                TrackShort = "Sample Raceway",
                TimeOfDay = 14 * 3600 + 20 * 60,
                DeltaToBest = -0.24,
                HasDelta = race,
                Diagnostics = new string[0]
            };
        }
    }
}
