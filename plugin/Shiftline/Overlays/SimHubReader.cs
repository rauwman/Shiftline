using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GameReaderCommon;

namespace Shiftline.Plugin
{
    /// <summary>
    /// Builds the standings for every game other than iRacing from SimHub's own opponent list, which SimHub fills for all the
    /// games it supports. What a game does not deliver (iRating, licence, incidents ...) is reported as missing, so the overlay
    /// can hide those columns.
    /// </summary>
    internal sealed class SimHubReader
    {
        private readonly Dictionary<string, int> _carIds = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _classIds = new Dictionary<string, int>(StringComparer.Ordinal);
        private string _sessionKey;

        public StandingsSnapshot Build(GameData data, long sequence, SessionTracker tracker)
        {
            StatusDataBase status = data == null ? null : data.NewData;
            if (status == null || status.Opponents == null || status.Opponents.Count == 0)
            {
                return StandingsSnapshot.Empty;
            }

            string sessionKey = (data.GameName ?? string.Empty) + "/" + status.TrackId + "/" + status.SessionTypeName;
            if (!string.Equals(sessionKey, _sessionKey, StringComparison.Ordinal))
            {
                _sessionKey = sessionKey;
                _carIds.Clear();
                _classIds.Clear();
            }

            // The F1 games (Codemasters/EA reader) put the team name into the car class, which would split a single-class
            // field into one class per team. Treat them as one class.
            bool oneClass = (data.GameName ?? string.Empty).StartsWith("F1", StringComparison.OrdinalIgnoreCase);

            StandingsFeatures features = StandingsFeatures.None;
            List<StandingsRow> rows = new List<StandingsRow>(status.Opponents.Count);
            string playerCar = string.Empty;
            foreach (Opponent o in status.Opponents)
            {
                if (o == null)
                {
                    continue;
                }

                double rating = o.IRacing_IRating ?? 0;
                if (rating > 0)
                {
                    features |= StandingsFeatures.IRating;
                }

                if (!string.IsNullOrEmpty(o.LicenceString))
                {
                    features |= StandingsFeatures.Licence;
                }

                if (!string.IsNullOrEmpty(o.FrontTyreCompound))
                {
                    features |= StandingsFeatures.Tyres;
                }

                if (o.IsPlayer)
                {
                    playerCar = o.CarName ?? string.Empty;
                }

                bool gone = !o.IsConnected || o.IsCarInGarage == true;
                rows.Add(new StandingsRow
                {
                    CarIdx = IdOf(_carIds, o.Id ?? o.Name ?? string.Empty, 63),
                    Position = o.Position,
                    ClassPosition = oneClass ? o.Position : o.PositionInClass,
                    Number = o.CarNumber ?? string.Empty,
                    Name = o.Name ?? string.Empty,
                    ClassId = oneClass ? 0 : IdOf(_classIds, o.CarClassID ?? o.CarClass ?? string.Empty, 1000),
                    ClassName = oneClass ? string.Empty : o.CarClass ?? string.Empty,
                    ClassColor = oneClass ? string.Empty : o.CarClassColor ?? string.Empty,
                    IRating = (int)rating,
                    License = o.LicenceString ?? string.Empty,
                    Make = CarMakes.CodeFor(o.CarName),
                    LapsCompleted = Math.Max(0, (o.CurrentLap ?? 1) - 1),
                    LapDistPct = o.TrackPositionPercent ?? -1,
                    LastLap = Seconds(o.LastLapTime),
                    BestLap = Seconds(o.BestLapTime),
                    OnPitRoad = o.IsCarInPitLane || o.IsCarInPit,
                    TrackSurface = gone ? -1 : 3,
                    F2Time = o.GaptoLeader ?? 0,
                    IsPlayer = o.IsPlayer,
                    StartPosition = o.StartPosition ?? 0,
                    StartClassPosition = o.StartPositionClass ?? 0,
                    TireType = TyreName(o.FrontTyreCompound)
                });
            }

            rows.Sort((a, b) => (a.Position <= 0 ? int.MaxValue : a.Position).CompareTo(b.Position <= 0 ? int.MaxValue : b.Position));
            string sessionType = status.SessionTypeName ?? string.Empty;
            if (tracker != null)
            {
                tracker.Apply(rows, sessionKey, sessionType.IndexOf("Race", StringComparison.OrdinalIgnoreCase) >= 0, Environment.TickCount / 1000.0);
            }

            double? delta = status.DeltaToSessionBest;
            if (delta.HasValue)
            {
                features |= StandingsFeatures.Delta;
            }

            StandingsRow player = rows.Find(r => r.IsPlayer);
            return new StandingsSnapshot
            {
                Rows = rows.ToArray(),
                PlayerCarIdx = player == null ? -1 : player.CarIdx,
                Track = status.TrackNameWithConfig ?? string.Empty,
                TrackShort = status.TrackName ?? string.Empty,
                SessionType = sessionType,
                CarName = playerCar,
                TimeRemaining = status.SessionTimeLeft.TotalSeconds > 0 ? status.SessionTimeLeft.TotalSeconds : -1,
                LapsTotal = status.TotalLaps > 0 ? status.TotalLaps : 0,
                TrackTemp = status.RoadTemperature > 0 ? status.RoadTemperature.ToString("0.0", CultureInfo.InvariantCulture) + " C" : string.Empty,
                IncidentLimit = string.Empty,
                TimeOfDay = -1,
                DeltaToBest = delta ?? 0,
                HasDelta = delta.HasValue,
                IsReplay = status.IsGameReplay,
                IsInGarage = data.GameInMenu,
                StrengthOfField = (features & StandingsFeatures.IRating) != 0 ? SessionTracker.StrengthOfField(rows) : 0,
                Features = features,
                Game = data.GameName ?? string.Empty,
                Sequence = sequence,
                TakenUtcTicks = DateTime.UtcNow.Ticks,
                Diagnostics = new[]
                {
                    "source: SimHub opponent list (" + (data.GameName ?? "?") + ")",
                    "cars: " + rows.Count,
                    "features: " + features
                }
            };
        }

        private static int IdOf(Dictionary<string, int> ids, string key, int max)
        {
            int id;
            if (!ids.TryGetValue(key, out id))
            {
                id = ids.Count <= max ? ids.Count : ids.Count % (max + 1);
                ids[key] = id;
            }

            return id;
        }

        private static double Seconds(TimeSpan time)
        {
            return time.TotalSeconds > 0 ? time.TotalSeconds : -1;
        }

        private static string TyreName(string compound)
        {
            return string.IsNullOrWhiteSpace(compound) ? string.Empty : compound.Trim();
        }
    }
}
