using System;
using System.Collections.Generic;

namespace Shiftline.Plugin
{
    /// <summary>
    /// Remembers what iRacing does not report directly, from one snapshot to the next: the start position of each car, its pit
    /// stops (lap of the last stop, number of stops), the laps of its current stint, and whether it was towed. Runs on the data
    /// thread only. Everything is reset when the session changes.
    /// </summary>
    internal sealed class SessionTracker
    {
        private const int Cars = 64;

        // iRacing track surface values (irsdk_TrkLoc).
        private const int NotInWorld = -1;
        private const int OffTrack = 0;
        private const int InPitStall = 1;
        private const int ApproachingPits = 2;
        private const int OnTrack = 3;

        private readonly int[] _startPosition = new int[Cars];
        private readonly int[] _pitLap = new int[Cars];
        private readonly int[] _pitCount = new int[Cars];
        private readonly int[] _stintStartLap = new int[Cars];
        private readonly bool[] _wasOnPitRoad = new bool[Cars];
        private readonly bool[] _seen = new bool[Cars];
        private readonly int[] _lastSurface = new int[Cars];
        private readonly bool[] _passedPitRoad = new bool[Cars];
        private readonly bool[] _towing = new bool[Cars];
        private readonly double[] _pitEntryTime = new double[Cars];
        private readonly int[] _lastLapCount = new int[Cars];
        private readonly List<double>[] _recentLaps = new List<double>[Cars];
        private string _sessionKey;
        private const int KeptLaps = 5;

        public void Apply(List<StandingsRow> rows, string sessionKey, bool race, double sessionTime)
        {
            if (!string.Equals(sessionKey, _sessionKey, StringComparison.Ordinal))
            {
                Reset();
                _sessionKey = sessionKey;
            }

            foreach (StandingsRow row in rows)
            {
                int i = row.CarIdx;
                if (i < 0 || i >= Cars)
                {
                    continue;
                }

                int laps = Math.Max(0, row.LapsCompleted);
                if (!_seen[i])
                {
                    _seen[i] = true;
                    _stintStartLap[i] = laps;
                    _wasOnPitRoad[i] = row.OnPitRoad;
                    _lastSurface[i] = row.TrackSurface;
                    _lastLapCount[i] = row.LapsCompleted;
                    _recentLaps[i] = new List<double>(KeptLaps);
                }

                // A new completed lap: keep its time (newest first).
                if (row.LapsCompleted > _lastLapCount[i])
                {
                    if (row.LastLap > 0)
                    {
                        _recentLaps[i].Insert(0, row.LastLap);
                        if (_recentLaps[i].Count > KeptLaps)
                        {
                            _recentLaps[i].RemoveAt(KeptLaps);
                        }
                    }

                    _lastLapCount[i] = row.LapsCompleted;
                }

                // The first position a car gets in a race is its start position (iRacing gives positions from the green flag).
                if (race && _startPosition[i] == 0 && row.Position > 0)
                {
                    _startPosition[i] = row.Position;
                }

                if (row.OnPitRoad && !_wasOnPitRoad[i])
                {
                    _pitCount[i]++;
                    _pitLap[i] = laps + 1;
                    _pitEntryTime[i] = sessionTime;
                }
                else if (!row.OnPitRoad && _wasOnPitRoad[i])
                {
                    _stintStartLap[i] = laps;           // leaving the pit lane starts a new stint
                }

                _wasOnPitRoad[i] = row.OnPitRoad;
                TrackTow(i, row);

                row.StartPosition = _startPosition[i];
                row.PitLap = _pitCount[i] > 0 ? _pitLap[i] : 0;
                row.PitCount = _pitCount[i];
                row.StintLaps = Math.Max(0, laps - _stintStartLap[i]);
                row.Towing = _towing[i];
                row.PitLaneSeconds = row.OnPitRoad && _pitCount[i] > 0 ? Math.Max(0, sessionTime - _pitEntryTime[i]) : -1;
                row.RecentLaps = _recentLaps[i].ToArray();
            }
        }

        /// <summary>
        /// A car that reaches its pit stall without driving through the pit lane was towed. This is a guess from the track surface
        /// values and has not been checked in a session yet.
        /// </summary>
        private void TrackTow(int i, StandingsRow row)
        {
            int surface = row.TrackSurface;
            int last = _lastSurface[i];
            if (surface == ApproachingPits || row.OnPitRoad)
            {
                _passedPitRoad[i] = true;
            }

            if (surface == OnTrack || surface == OffTrack)
            {
                _passedPitRoad[i] = false;
                _towing[i] = false;
            }

            if (surface == InPitStall && last != InPitStall && !_passedPitRoad[i] && (last == OnTrack || last == OffTrack || last == NotInWorld))
            {
                _towing[i] = true;
            }

            _lastSurface[i] = surface;
        }

        private void Reset()
        {
            Array.Clear(_startPosition, 0, Cars);
            Array.Clear(_pitLap, 0, Cars);
            Array.Clear(_pitCount, 0, Cars);
            Array.Clear(_stintStartLap, 0, Cars);
            Array.Clear(_wasOnPitRoad, 0, Cars);
            Array.Clear(_seen, 0, Cars);
            Array.Clear(_passedPitRoad, 0, Cars);
            Array.Clear(_towing, 0, Cars);
            Array.Clear(_pitEntryTime, 0, Cars);
            Array.Clear(_lastLapCount, 0, Cars);
            Array.Clear(_recentLaps, 0, Cars);
            for (int i = 0; i < Cars; i++)
            {
                _lastSurface[i] = NotInWorld;
            }
        }

        /// <summary>
        /// Strength of field from the iRatings, with the formula the iRacing community uses (an average on iRacing's logarithmic
        /// rating scale). Not an official iRacing number. 0 when no car has an iRating.
        /// </summary>
        public static int StrengthOfField(IEnumerable<StandingsRow> rows)
        {
            const double Scale = 1600 / 0.69314718055994530942;     // 1600 / ln 2
            double sum = 0;
            int count = 0;
            foreach (StandingsRow row in rows)
            {
                if (row.IRating > 0)
                {
                    sum += Math.Exp(-row.IRating / Scale);
                    count++;
                }
            }

            return count == 0 ? 0 : (int)Math.Round(Scale * Math.Log(count / sum));
        }
    }
}
