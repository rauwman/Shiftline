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
        public string Make;
        public string LicenseColor;
        public string CarName;
        public string Flair;        // FlairName, for example the country; empty when iRacing sends none
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

                // Only iRacing's own CarIsPaceCar marks the pace car. The field IsPaceCar was True for the player's own car in a
                // real session, so it is not used.
                if (IsTrue(get(prefix + "IsSpectator")) || IsTrue(get(prefix + "CarIsPaceCar")))
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
                    IRating = SessionTracker.RealRating(ToInt(get(prefix + "IRating"), 0)),
                    License = Text(get(prefix + "LicString")),
                    Make = CarMakes.CodeFor(Text(get(prefix + "CarScreenName"))),
                    LicenseColor = Text(get(prefix + "LicColor")),
                    CarName = Text(get(prefix + "CarScreenNameShort")),
                    Flair = Text(get(prefix + "FlairName"))
                });
            }

            return list;
        }

        /// <summary>Builds the standings from the per-car arrays and the driver list.</summary>
        public static StandingsSnapshot Build(Func<string, object> get, List<DriverEntry> drivers, long sequence, SessionTracker tracker)
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
            object tire = get(Telemetry + "CarIdxTireCompound");
            Dictionary<int, string> tireTypes = ReadTireTypes(get);
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
                    Make = driver.Make,
                    LicenseColor = driver.LicenseColor,
                    Flair = driver.Flair,
                    LapsCompleted = (int)At(lapCompleted, i, 0),
                    LapDistPct = dist,
                    LastLap = At(lastLap, i, -1),
                    BestLap = At(bestLap, i, -1),
                    OnPitRoad = At(onPit, i, 0) != 0,
                    TrackSurface = (int)At(surface, i, -1),
                    F2Time = At(f2, i, 0),
                    EstTime = At(est, i, 0),
                    IsPlayer = i == player,
                    TireType = TireTypeOf(tireTypes, (int)At(tire, i, -1))
                });
            }

            // Classified cars by position; the rest (practice before the first lap, for example) by distance driven.
            rows.Sort(Compare);

            int sessionNum = ToInt(get(Session + "SessionInfo.CurrentSessionNum"), 0);
            string sessionType = Text(get(Session + "SessionInfo.Sessions" + (sessionNum + 1).ToString("00", CultureInfo.InvariantCulture) + ".SessionType"));
            if (tracker != null)
            {
                string key = Text(get(Session + "WeekendInfo.SubSessionID")) + "/" + sessionNum;
                tracker.Apply(rows, key, sessionType.IndexOf("Race", StringComparison.OrdinalIgnoreCase) >= 0, ToDouble(get(Telemetry + "SessionTime"), 0));
            }

            if (sessionType.IndexOf("Race", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                IRatingEstimate.Apply(rows);
            }

            DriverEntry own = drivers.Find(d => d.CarIdx == player);
            int lapsTotal = ToInt(get(Telemetry + "SessionLapsTotal"), 0);

            return new StandingsSnapshot
            {
                Rows = rows.ToArray(),
                PlayerCarIdx = player,
                CarName = own == null ? string.Empty : own.CarName,
                StrengthOfField = SessionTracker.StrengthOfField(rows),
                TimeRemaining = ToDouble(get(Telemetry + "SessionTimeRemain"), -1),
                TimeTotal = ToDouble(get(Telemetry + "SessionTimeTotal"), -1),
                LapsTotal = lapsTotal > 0 && lapsTotal < 32767 ? lapsTotal : 0,
                TrackTemp = Text(get(Session + "WeekendInfo.TrackSurfaceTemp")),
                Incidents = ToInt(get(Telemetry + "PlayerCarMyIncidentCount"), 0),
                IncidentLimit = Text(get(Session + "WeekendInfo.WeekendOptions.IncidentLimit")),
                IsReplay = ToInt(get(Telemetry + "IsReplayPlaying"), 0) != 0,
                IsInGarage = IsTrue(get(Telemetry + "IsGarageVisible")) || IsTrue(get(Telemetry + "IsInGarage")),
                Features = StandingsFeatures.All,
                Game = "IRacing",
                TrackShort = Text(get(Session + "WeekendInfo.TrackDisplayShortName")),
                TimeOfDay = ToDouble(get(Telemetry + "SessionTimeOfDay"), -1),
                OilTemp = ToDouble(get(Telemetry + "OilTemp"), double.NaN),
                WaterTemp = ToDouble(get(Telemetry + "WaterTemp"), double.NaN),
                AirTemp = ToDouble(get(Telemetry + "AirTemp"), double.NaN),
                Humidity = Percent(ToDouble(get(Telemetry + "RelativeHumidity"), double.NaN)),
                DeltaToBest = ToDouble(get(Telemetry + "LapDeltaToBestLap"), 0),
                HasDelta = IsTrue(get(Telemetry + "LapDeltaToBestLap_OK")),
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

        /// <summary>
        /// The tyre compounds of the player's car (index to name, for example 0 = "Hard"). iRacing lists them only for the player's
        /// car, so in multi-class races the names may not fit the other classes.
        /// </summary>
        private static Dictionary<int, string> ReadTireTypes(Func<string, object> get)
        {
            Dictionary<int, string> types = new Dictionary<int, string>();
            for (int n = 1; n <= 8; n++)
            {
                string prefix = Session + "DriverInfo.DriverTires" + n.ToString("00", CultureInfo.InvariantCulture) + ".";
                object index = get(prefix + "TireIndex");
                if (index == null)
                {
                    break;
                }

                types[ToInt(index, -1)] = Text(get(prefix + "TireCompoundType"));
            }

            return types;
        }

        private static string TireTypeOf(Dictionary<int, string> types, int compound)
        {
            string type;
            return compound >= 0 && types.TryGetValue(compound, out type) ? type : string.Empty;
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

        /// <summary>iRacing reports the humidity as a fraction (0..1); a value above 1 is taken as percent already.</summary>
        private static double Percent(double value)
        {
            return double.IsNaN(value) ? value : value <= 1.0 ? value * 100 : value;
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
