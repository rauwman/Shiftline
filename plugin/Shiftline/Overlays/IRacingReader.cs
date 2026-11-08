using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace Shiftline.Plugin
{
    /// <summary>One entry of iRacing's driver list (rarely changes during a session).</summary>
    internal sealed class DriverEntry
    {
        public int CarIdx;
        public string Name;
        public string Number;
        public int ClassId;
        public string ClassName;
        public string ClassColor;
        public int IRating;
        public string License;
    }

    /// <summary>
    /// Reads the field from the properties SimHub publishes for iRacing. The reader gets a function that returns a property by
    /// name, so it does not depend on SimHub and can be tried with any data.
    /// </summary>
    internal static class IRacingReader
    {
        private const string Raw = "DataCorePlugin.GameRawData.";
        private const string Telemetry = Raw + "Telemetry.";
        private const string Session = Raw + "SessionData.";
        private const int MaxDrivers = 64;

        /// <summary>The driver list, in the order iRacing reports it. Pace car and spectators are left out.</summary>
        public static List<DriverEntry> ReadDrivers(Func<string, object> get)
        {
            List<DriverEntry> list = new List<DriverEntry>();
            for (int n = 1; n <= MaxDrivers; n++)
            {
                string prefix = Session + "DriverInfo.Drivers" + n.ToString("00", CultureInfo.InvariantCulture) + ".";
                object idx = get(prefix + "CarIdx");
                if (idx == null)
                {
                    break;                  // the list has no gaps: the first missing entry is its end
                }

                if (IsTrue(get(prefix + "IsSpectator")) || IsTrue(get(prefix + "CarIsPaceCar")) || IsTrue(get(prefix + "IsPaceCar")))
                {
                    continue;
                }

                list.Add(new DriverEntry
                {
                    CarIdx = ToInt(idx, -1),
                    Name = Text(get(prefix + "UserName")),
                    Number = Text(get(prefix + "CarNumber")),
                    ClassId = ToInt(get(prefix + "CarClassID"), 0),
                    ClassName = Text(get(prefix + "CarClassShortName")),
                    ClassColor = Text(get(prefix + "CarClassColor")),
                    IRating = ToInt(get(prefix + "IRating"), 0),
                    License = Text(get(prefix + "LicString"))
                });
            }

            return list;
        }

        /// <summary>Builds the standings from the per-car arrays and the driver list.</summary>
        public static StandingsSnapshot Build(Func<string, object> get, List<DriverEntry> drivers, long sequence)
        {
            object position = get(Telemetry + "CarIdxPosition");
            object classPosition = get(Telemetry + "CarIdxClassPosition");
            object lapCompleted = get(Telemetry + "CarIdxLapCompleted");
            object lapDist = get(Telemetry + "CarIdxLapDistPct");
            object lastLap = get(Telemetry + "CarIdxLastLapTime");
            object bestLap = get(Telemetry + "CarIdxBestLapTime");
            object onPit = get(Telemetry + "CarIdxOnPitRoad");
            object surface = get(Telemetry + "CarIdxTrackSurface");
            object f2 = get(Telemetry + "CarIdxF2Time");
            object est = get(Telemetry + "CarIdxEstTime");
            int player = ToInt(get(Telemetry + "PlayerCarIdx"), -1);

            List<StandingsRow> rows = new List<StandingsRow>(drivers.Count);
            foreach (DriverEntry driver in drivers)
            {
                int i = driver.CarIdx;
                if (i < 0)
                {
                    continue;
                }

                // Cars that are not in the world (garage, disconnected) stay in the list: standings show every entrant.
                double dist = At(lapDist, i, -1);
                int pos = (int)At(position, i, 0);

                rows.Add(new StandingsRow
                {
                    CarIdx = i,
                    Position = pos,
                    ClassPosition = (int)At(classPosition, i, 0),
                    Number = driver.Number,
                    Name = driver.Name,
                    ClassId = driver.ClassId,
                    ClassName = driver.ClassName,
                    ClassColor = driver.ClassColor,
                    IRating = driver.IRating,
                    License = driver.License,
                    LapsCompleted = (int)At(lapCompleted, i, 0),
                    LapDistPct = dist,
                    LastLap = At(lastLap, i, -1),
                    BestLap = At(bestLap, i, -1),
                    OnPitRoad = At(onPit, i, 0) != 0,
                    TrackSurface = (int)At(surface, i, -1),
                    F2Time = At(f2, i, 0),
                    EstTime = At(est, i, 0),
                    IsPlayer = i == player
                });
            }

            // Classified cars by position; the rest (practice before the first lap, for example) by distance driven.
            rows.Sort(Compare);

            int sessionNum = ToInt(get(Session + "SessionInfo.CurrentSessionNum"), 0);
            string sessionType = Text(get(Session + "SessionInfo.Sessions" + (sessionNum + 1).ToString("00", CultureInfo.InvariantCulture) + ".SessionType"));

            return new StandingsSnapshot
            {
                Rows = rows.ToArray(),
                PlayerCarIdx = player,
                Track = Text(get(Session + "WeekendInfo.TrackDisplayName")),
                SessionType = sessionType,
                Sequence = sequence,
                TakenUtcTicks = DateTime.UtcNow.Ticks,
                Diagnostics = new[]
                {
                    Describe("CarIdxPosition", position), Describe("CarIdxClassPosition", classPosition),
                    Describe("CarIdxLapCompleted", lapCompleted), Describe("CarIdxLapDistPct", lapDist),
                    Describe("CarIdxLastLapTime", lastLap), Describe("CarIdxBestLapTime", bestLap),
                    Describe("CarIdxOnPitRoad", onPit), Describe("CarIdxTrackSurface", surface),
                    Describe("CarIdxF2Time", f2), Describe("CarIdxEstTime", est),
                    "drivers in the list: " + drivers.Count
                }
            };
        }

        private static int Compare(StandingsRow a, StandingsRow b)
        {
            bool aClassified = a.Position > 0;
            bool bClassified = b.Position > 0;
            if (aClassified != bClassified)
            {
                return aClassified ? -1 : 1;
            }

            if (aClassified)
            {
                int byPosition = a.Position.CompareTo(b.Position);
                if (byPosition != 0)
                {
                    return byPosition;
                }
            }
            else
            {
                int byProgress = b.Progress.CompareTo(a.Progress);
                if (byProgress != 0)
                {
                    return byProgress;
                }
            }

            return a.CarIdx.CompareTo(b.CarIdx);
        }

        // ---------------------------------------------------------------- tolerant value access (the element types are not known for sure)

        private static string Describe(string name, object array)
        {
            if (array == null)
            {
                return name + ": missing";
            }

            Array typed = array as Array;
            if (typed != null)
            {
                return name + ": " + typed.GetType().Name + " (" + typed.Length + ")";
            }

            ICollection collection = array as ICollection;
            return name + ": " + array.GetType().Name + (collection == null ? string.Empty : " (" + collection.Count + ")");
        }

        /// <summary>Element i of an array or list as a number (booleans become 0 or 1); the fallback when anything is off.</summary>
        internal static double At(object array, int i, double fallback)
        {
            if (array == null || i < 0)
            {
                return fallback;
            }

            try
            {
                IList list = array as IList;
                if (list != null)
                {
                    return i < list.Count ? ToDouble(list[i], fallback) : fallback;
                }
            }
            catch (Exception)
            {
            }

            return fallback;
        }

        internal static double ToDouble(object value, double fallback)
        {
            if (value == null)
            {
                return fallback;
            }

            try
            {
                if (value is bool)
                {
                    return (bool)value ? 1 : 0;
                }

                string text = value as string;
                if (text != null)
                {
                    double parsed;
                    return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
                }

                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        internal static int ToInt(object value, int fallback)
        {
            double d = ToDouble(value, double.NaN);
            return double.IsNaN(d) || double.IsInfinity(d) ? fallback : (int)d;
        }

        private static bool IsTrue(object value)
        {
            if (value == null)
            {
                return false;
            }

            if (value is bool)
            {
                return (bool)value;
            }

            string text = value.ToString();
            return text == "1" || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static string Text(object value)
        {
            return value == null ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }
    }
}
