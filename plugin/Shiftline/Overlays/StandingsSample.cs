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
        /// <summary>
        /// The sample spread around the lap (it otherwise has every car at the same spot), with one car a lap down right in
        /// front of the player and the leader a lap up right behind: shows the lap marks when an overlay is sorted by the track.
        /// </summary>
        public static StandingsSnapshot OnTrack(StandingsSnapshot sample)
        {
            StandingsRow[] rows = new StandingsRow[sample.Rows.Length];
            int player = -1;
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i] = sample.Rows[i].Copy();
                rows[i].LapDistPct = ((0.9 - i * 0.03) % 1 + 1) % 1;     // the leader furthest round, the others behind
                if (rows[i].IsPlayer)
                {
                    player = i;
                }
            }

            if (player > 0 && rows.Length > player + 2)
            {
                StandingsRow me = rows[player];
                StandingsRow last = rows[rows.Length - 1];
                last.LapDistPct = (me.LapDistPct + 0.015) % 1;       // just in front on the track, a lap down
                last.LapsCompleted = me.LapsCompleted - 1;
                StandingsRow leader = rows[0];
                leader.LapDistPct = (me.LapDistPct + 0.985) % 1;     // just behind on the track, a lap up
                leader.LapsCompleted = me.LapsCompleted + 1;
            }

            StandingsSnapshot copy = sample.As(sample.Features, sample.Game);
            copy.Rows = rows;
            return copy;
        }

        /// <summary>
        /// The sample spread around the lap, where one pair of neighbouring cars swaps places every second step and swaps back
        /// in between: only two rows move at a time, a different pair each time (sometimes with the player). Drives the
        /// animation preview of the relatives.
        /// </summary>
        public static StandingsSnapshot Passing(int step)
        {
            StandingsSnapshot track = OnTrack(Create(true));
            StandingsRow[] rows = track.Rows;
            int player = Array.FindIndex(rows, r => r.IsPlayer);
            if (player < 0 || step % 2 != 0)
            {
                return track;
            }

            // the cars on the player's lap, from far behind to far in front of him
            StandingsRow me = rows[player];
            List<StandingsRow> order = new List<StandingsRow>();
            foreach (StandingsRow row in rows)
            {
                if (row.LapsCompleted == me.LapsCompleted)
                {
                    order.Add(row);
                }
            }

            order.Sort((a, b) => Signed(a.LapDistPct - me.LapDistPct).CompareTo(Signed(b.LapDistPct - me.LapDistPct)));
            int at = order.IndexOf(me);
            int[][] pairs = { new[] { 1, 2 }, new[] { -2, -1 }, new[] { 0, 1 }, new[] { 2, 3 }, new[] { -1, 0 }, new[] { -3, -2 } };
            int[] pair = pairs[(step / 2) % pairs.Length];
            int first = at + pair[0];
            int second = at + pair[1];
            if (first >= 0 && second < order.Count)
            {
                double spot = order[first].LapDistPct;
                order[first].LapDistPct = order[second].LapDistPct;
                order[second].LapDistPct = spot;
            }

            return track;
        }

        /// <summary>A difference of lap positions the shorter way around: -0.5 .. 0.5.</summary>
        private static double Signed(double difference)
        {
            return difference - Math.Round(difference);
        }

        public static StandingsSnapshot Shuffled(int step)
        {
            return Shuffled(step, false);
        }

        /// <param name="aroundPlayer">Swap only cars right around the player (and the player itself): for the Battle Bar.</param>
        public static StandingsSnapshot Shuffled(int step, bool aroundPlayer)
        {
            StandingsSnapshot race = Create(true);
            StandingsRow[] rows = new StandingsRow[race.Rows.Length];
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i] = race.Rows[i].Copy();
            }

            int first = 1 + step * 3 % (rows.Length - 2);
            int second = first + 1;
            if (aroundPlayer)
            {
                // Neighbours in the player's class: the pairs (p-2, p-1), (p-1, p), (p, p+1), (p+1, p+2) in turn, so the cards
                // around the player move and the player gains and loses a place.
                int player = Array.FindIndex(rows, r => r.IsPlayer);
                List<int> mates = new List<int>();
                for (int i = 0; i < rows.Length; i++)
                {
                    if (player >= 0 && rows[i].ClassId == rows[player].ClassId)
                    {
                        mates.Add(i);
                    }
                }

                int[] offsets = { -2, -1, 0, 1 };
                int at = mates.IndexOf(player) + offsets[(step / 2) % offsets.Length];
                at = Math.Max(0, Math.Min(mates.Count - 2, at));
                if (mates.Count >= 2)
                {
                    first = mates[at];
                    second = mates[at + 1];
                }
            }

            if (step % 2 == 1)
            {
                StandingsRow moved = rows[first];
                rows[first] = rows[second];
                rows[second] = moved;
            }

            // positions follow the new order, overall and inside each class
            Dictionary<int, int> inClass = new Dictionary<int, int>();
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i].Position = i + 1;
                int count;
                inClass.TryGetValue(rows[i].ClassId, out count);
                inClass[rows[i].ClassId] = count + 1;
                rows[i].ClassPosition = count + 1;
            }

            return new StandingsSnapshot
            {
                Rows = rows,
                PlayerCarIdx = race.PlayerCarIdx,
                Track = race.Track,
                SessionType = race.SessionType,
                CarName = race.CarName,
                TimeRemaining = race.TimeRemaining,
                TimeTotal = race.TimeTotal,
                LapsTotal = race.LapsTotal,
                TrackTemp = race.TrackTemp,
                Incidents = race.Incidents,
                IncidentLimit = race.IncidentLimit,
                StrengthOfField = race.StrengthOfField,
                TrackShort = race.TrackShort,
                TimeOfDay = race.TimeOfDay,
                OilTemp = race.OilTemp,
                WaterTemp = race.WaterTemp,
                AirTemp = race.AirTemp,
                Humidity = race.Humidity,
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
                TimeRemaining = 1834,
                TimeTotal = 3600,
                LapsTotal = race ? 20 : 0,
                TrackTemp = "31.5 C",
                Incidents = 2,
                IncidentLimit = "25",
                IsSample = true,
                Features = StandingsFeatures.All,
                StrengthOfField = 3120,
                TrackShort = "Sample Raceway",
                TimeOfDay = 14 * 3600 + 20 * 60,
                OilTemp = 98.4,
                WaterTemp = 91.2,
                AirTemp = 24.6,
                Humidity = 52,
                DeltaToBest = -0.24,
                HasDelta = race,
                Diagnostics = new string[0]
            };
        }
    }
}
