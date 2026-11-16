using System;
using System.Collections.Generic;
using GameReaderCommon;

namespace Shiftline.Plugin
{
    /// <summary>
    /// Where every car is on the lap, read on every SimHub update (about 60 times per second) for the Track Map, apart from
    /// the full standings, which are read at their own, slower rate. Reading only these few numbers is cheap; the standings
    /// (sorting, gaps, texts) and their redraw are not. Indexed by CarIdx (iRacing's, or the id the SimHub reader gives).
    /// The object never changes after it was published.
    /// </summary>
    public sealed class CarPositions
    {
        public const int Slots = 64;

        public static readonly CarPositions Empty = new CarPositions(0, 0, new double[0], new bool[0]);

        private CarPositions(long sequence, double takenSeconds, double[] pct, bool[] pit)
        {
            Sequence = sequence;
            TakenSeconds = takenSeconds;
            Pct = pct;
            Pit = pit;
        }

        public long Sequence { get; private set; }

        /// <summary>UTC seconds when the places were read from the game.</summary>
        public double TakenSeconds { get; private set; }

        /// <summary>Place on the lap (0..1) per CarIdx; -1 when unknown.</summary>
        public double[] Pct { get; private set; }

        public bool[] Pit { get; private set; }

        /// <summary>The place of a car, or -1 when this feed does not know it.</summary>
        public double PctOf(int carIdx)
        {
            return carIdx >= 0 && carIdx < Pct.Length ? Pct[carIdx] : -1;
        }

        private static double[] NewPct()
        {
            double[] pct = new double[Slots];
            for (int i = 0; i < Slots; i++)
            {
                pct[i] = -1;
            }

            return pct;
        }

        /// <summary>
        /// Publishes new places only when a car has moved: a game that delivers slower than SimHub calls (or a paused game)
        /// must not get the same place stamped twice, which would make the cars stop between two real updates.
        /// </summary>
        internal static CarPositions Next(CarPositions last, double[] pct, bool[] pit)
        {
            bool changed = last.Pct.Length != pct.Length;
            for (int i = 0; !changed && i < pct.Length; i++)
            {
                changed = pct[i] != last.Pct[i] || pit[i] != last.Pit[i];
            }

            if (!changed)
            {
                return last;
            }

            return new CarPositions(last.Sequence + 1, DateTime.UtcNow.Ticks / (double)TimeSpan.TicksPerSecond, pct, pit);
        }

        /// <summary>iRacing: the per-car arrays of the telemetry.</summary>
        internal static CarPositions ReadIRacing(Func<string, object> get, CarPositions last)
        {
            const string telemetry = "DataCorePlugin.GameRawData.Telemetry.";
            object lapDist = get(telemetry + "CarIdxLapDistPct");
            object onPit = get(telemetry + "CarIdxOnPitRoad");
            if (lapDist == null)
            {
                return last;
            }

            double[] pct = NewPct();
            bool[] pit = new bool[Slots];
            for (int i = 0; i < Slots; i++)
            {
                pct[i] = IRacingReader.At(lapDist, i, -1);
                pit[i] = IRacingReader.At(onPit, i, 0) != 0;
            }

            return Next(last, pct, pit);
        }

        /// <summary>Every other game: SimHub's opponent list, with the ids the standings reader gave the cars.</summary>
        internal static CarPositions ReadSimHub(GameData data, SimHubReader reader, CarPositions last)
        {
            StatusDataBase status = data == null ? null : data.NewData;
            if (status == null || status.Opponents == null || status.Opponents.Count == 0)
            {
                return last;
            }

            double[] pct = NewPct();
            bool[] pit = new bool[Slots];
            foreach (Opponent o in status.Opponents)
            {
                int id;
                if (o == null || !reader.TryIdOf(o.Id ?? o.Name ?? string.Empty, out id) || id < 0 || id >= Slots)
                {
                    continue;       // a car the standings have not seen yet: it joins with the next standings update
                }

                pct[id] = o.TrackPositionPercent ?? -1;
                pit[id] = o.IsCarInPitLane || o.IsCarInPit;
            }

            return Next(last, pct, pit);
        }
    }
}
