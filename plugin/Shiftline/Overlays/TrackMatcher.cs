using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Shiftline.Plugin
{
    /// <summary>What the running game says about its track.</summary>
    internal sealed class TrackQuery
    {
        public string Game = string.Empty;          // SimHub's game name, for example "IRacing", "AssettoCorsa"
        public string IRacingName = string.Empty;   // iRacing WeekendInfo.TrackName
        public string TrackCode = string.Empty;     // SimHub GameData.TrackCode (track and layout)
        public string TrackId = string.Empty;       // SimHub GameData.TrackId
        public string Config = string.Empty;        // layout name of the game
        public string DisplayName = string.Empty;   // the track's name as the game shows it
        public double LengthM = double.NaN;

        public bool IsIRacing
        {
            get { return string.Equals(Game, "IRacing", StringComparison.OrdinalIgnoreCase); }
        }

        /// <summary>The key a track is found by: "iracing/&lt;name&gt;" or "&lt;game&gt;/&lt;trackcode&gt;" (lower case).</summary>
        public string Key
        {
            get
            {
                if (IsIRacing)
                {
                    string name = TrackMatcher.NormKey(IRacingName);
                    return name.Length == 0 ? string.Empty : "iracing/" + name;
                }

                string code = TrackCode.Trim().Length > 0 ? TrackCode : TrackId;
                return code.Trim().Length == 0 ? string.Empty : (Game.Trim() + "/" + code.Trim()).ToLowerInvariant();
            }
        }

        public string Signature
        {
            get { return Key + "|" + DisplayName + "|" + Config + "|" + LengthM.ToString("0", CultureInfo.InvariantCulture); }
        }

        public static TrackQuery From(StandingsSnapshot snapshot)
        {
            TrackQuery query = new TrackQuery { Game = snapshot.Game ?? string.Empty, LengthM = snapshot.TrackLengthM };
            if (query.IsIRacing)
            {
                query.IRacingName = snapshot.TrackName ?? string.Empty;
                query.DisplayName = snapshot.Track ?? string.Empty;
                query.Config = snapshot.TrackConfig ?? string.Empty;
            }
            else
            {
                query.TrackId = snapshot.TrackName ?? string.Empty;
                query.TrackCode = snapshot.TrackCode ?? string.Empty;
                query.Config = snapshot.TrackConfig ?? string.Empty;
                query.DisplayName = snapshot.TrackShort ?? string.Empty;
            }

            return query;
        }
    }

    internal sealed class TrackMatch
    {
        public static readonly TrackMatch None = new TrackMatch { How = "none" };

        public TrackLayout Layout;
        public string How = "none";      // correction, code, name and length, closest length, preview, none
        public string Key = string.Empty;
        public double LengthDifference = double.NaN;    // our length against the game's, as a fraction
    }

    /// <summary>
    /// Picks the track shape for the running track, never asking: (0) the user's correction, (1) a shipped code, (2) name and
    /// length, (3) of several candidates the closest length, (4) none (the map shows its line or ring).
    /// </summary>
    internal static class TrackMatcher
    {
        private const double LengthTolerance = 0.03;

        // words that name a layout, not a place: they say nothing about which track it is
        private static readonly HashSet<string> LayoutWords = new HashSet<string>(StringComparer.Ordinal)
        {
            "gp", "grand", "prix", "club", "sprint", "national", "international", "full", "short", "long", "layout", "circuit",
            "course", "road", "race", "raceway", "track", "the", "and", "del", "combined", "config", "default", "reverse",
            "oval", "east", "west", "north", "south", "inner", "outer", "moto", "speedway", "motor", "motorsport", "park",
            "historic", "chicane", "pits", "pit", "version"
        };

        /// <summary>Lower case; every run of characters other than a-z and 0-9 becomes one "_"; no "_" at either end.</summary>
        public static string NormKey(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            StringBuilder key = new StringBuilder(text.Length);
            bool gap = false;
            foreach (char raw in text.ToLowerInvariant())
            {
                bool plain = (raw >= 'a' && raw <= 'z') || (raw >= '0' && raw <= '9');
                if (plain)
                {
                    if (gap && key.Length > 0)
                    {
                        key.Append('_');
                    }

                    key.Append(raw);
                    gap = false;
                }
                else
                {
                    gap = true;
                }
            }

            return key.ToString();
        }

        public static TrackMatch Find(TrackQuery query, IList<TrackLayout> layouts, IDictionary<string, string> corrections)
        {
            string key = query.Key;
            TrackMatch found = FindLayout(query, key, layouts, corrections);
            found.Key = key;
            if (found.Layout != null && query.LengthM > 0 && found.Layout.LengthM > 0)
            {
                found.LengthDifference = (found.Layout.LengthM - query.LengthM) / query.LengthM;
            }

            return found;
        }

        private static TrackMatch FindLayout(TrackQuery query, string key, IList<TrackLayout> layouts, IDictionary<string, string> corrections)
        {
            // (0) the user's own correction
            string id;
            if (key.Length > 0 && corrections != null && corrections.TryGetValue(key, out id))
            {
                TrackLayout corrected = layouts.FirstOrDefault(l => string.Equals(l.Id, id, StringComparison.OrdinalIgnoreCase));
                if (corrected != null)
                {
                    return new TrackMatch { Layout = corrected, How = "correction" };
                }
            }

            // (1) a shipped code
            List<TrackLayout> coded;
            if (query.IsIRacing)
            {
                string name = NormKey(query.IRacingName);
                coded = name.Length == 0 ? new List<TrackLayout>() : layouts.Where(l => l.IRacingNames.Any(n => NormKey(n) == name)).ToList();
            }
            else
            {
                HashSet<string> keys = CodeKeys(query);
                coded = layouts.Where(l => l.TrackCodes.Any(keys.Contains)).ToList();
            }

            HashSet<string> layoutWords = LayoutWordsOf(query.DisplayName + " " + query.Config + " " + query.TrackCode + " " + query.IRacingName);
            if (coded.Count > 0)
            {
                return new TrackMatch { Layout = Best(coded, query.LengthM, layoutWords), How = "code" };
            }

            // (2) name and length, (3) the closest length of several
            HashSet<string> words = Words(query.DisplayName + " " + query.Config + " " + query.TrackId + " " + query.IRacingName);
            if (words.Count == 0)
            {
                return new TrackMatch();
            }

            List<TrackLayout> named = layouts.Where(l => Words(l.Id.Replace('_', ' ') + " " + l.Name + " " + l.Config).Overlaps(words)).ToList();
            if (query.LengthM > 0)
            {
                named = named.Where(l => l.LengthM > 0 && Math.Abs(l.LengthM - query.LengthM) / query.LengthM <= LengthTolerance).ToList();
            }
            else if (named.Count > 1)
            {
                return new TrackMatch();        // the name alone is not enough to choose between layouts
            }

            if (named.Count == 0)
            {
                return new TrackMatch();
            }

            return new TrackMatch { Layout = Best(named, query.LengthM, layoutWords), How = named.Count == 1 ? "name and length" : "closest length" };
        }

        /// <summary>The keys a shipped code may use: &lt;game&gt;/&lt;trackcode&gt;, and the older &lt;game&gt;/&lt;trackid&gt;[/&lt;config&gt;].</summary>
        private static HashSet<string> CodeKeys(TrackQuery query)
        {
            string game = query.Game.Trim().ToLowerInvariant();
            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            if (query.TrackCode.Trim().Length > 0)
            {
                keys.Add(game + "/" + query.TrackCode.Trim().ToLowerInvariant());
            }

            if (query.TrackId.Trim().Length > 0)
            {
                string id = game + "/" + query.TrackId.Trim().ToLowerInvariant();
                keys.Add(query.Config.Trim().Length > 0 ? id + "/" + query.Config.Trim().ToLowerInvariant() : id);
                if (query.TrackCode.Trim().Length == 0)
                {
                    keys.Add(id);
                }
            }

            return keys;
        }

        /// <summary>
        /// Of several candidates: first the one whose layout words fit the game's (for example "GP" against "moto": a layout word
        /// the game names counts for it, one the game does not name counts against it), then the closest length.
        /// </summary>
        private static TrackLayout Best(List<TrackLayout> layouts, double length, HashSet<string> gameLayoutWords)
        {
            return layouts
                .OrderByDescending(l => LayoutFit(l, gameLayoutWords))
                .ThenBy(l => length > 0 && l.LengthM > 0 ? Math.Abs(l.LengthM - length) : double.MaxValue)
                .First();
        }

        private static int LayoutFit(TrackLayout layout, HashSet<string> gameLayoutWords)
        {
            HashSet<string> own = LayoutWordsOf(layout.Id.Replace('_', ' ') + " " + layout.Config);
            return own.Count(gameLayoutWords.Contains) - own.Count(w => !gameLayoutWords.Contains(w));
        }

        private static HashSet<string> LayoutWordsOf(string text)
        {
            HashSet<string> words = new HashSet<string>(StringComparer.Ordinal);
            foreach (string word in NormKey(text).Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (LayoutWords.Contains(word))
                {
                    words.Add(word);
                }
            }

            return words;
        }

        private static HashSet<string> Words(string text)
        {
            HashSet<string> words = new HashSet<string>(StringComparer.Ordinal);
            foreach (string word in NormKey(text).Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (word.Length >= 3 && !LayoutWords.Contains(word) && !word.All(char.IsDigit))
                {
                    words.Add(word);
                }
            }

            return words;
        }
    }

    /// <summary>The track shape for the running session (cached per track) or, for the samples, a fixed example track.</summary>
    internal static class TrackMapTracks
    {
        private static readonly string[] PreviewIds = { "spa_2024_combined", "spa_combined", "spa_2024_up", "spa_up" };
        private static string _signature;
        private static TrackMatch _match = TrackMatch.None;

        /// <summary>The match for a snapshot; for samples (preview, edit mode without a session) the session's track or Spa.</summary>
        public static TrackMatch For(StandingsSnapshot snapshot)
        {
            if (!TrackLibrary.IsReady)
            {
                TrackLibrary.EnsureLoading();
                return TrackMatch.None;
            }

            if (snapshot == null || snapshot.IsSample || !snapshot.HasData)
            {
                return Sample();
            }

            return Live(snapshot);
        }

        public static TrackMatch Live(StandingsSnapshot snapshot)
        {
            if (!TrackLibrary.IsReady || snapshot == null || !snapshot.HasData)
            {
                return TrackMatch.None;
            }

            TrackQuery query = TrackQuery.From(snapshot);
            string signature = query.Game + "|" + query.Signature;
            if (signature != _signature)
            {
                _match = TrackMatcher.Find(query, TrackLibrary.Layouts, TrackLibrary.Mappings);
                _signature = signature;
            }

            return _match;
        }

        private static TrackMatch Sample()
        {
            TrackMatch live = Live(OverlayData.Current);
            if (live.Layout != null)
            {
                return live;
            }

            foreach (string id in PreviewIds)
            {
                TrackLayout layout = TrackLibrary.ById(id);
                if (layout != null)
                {
                    return new TrackMatch { Layout = layout, How = "preview" };
                }
            }

            TrackLayout any = TrackLibrary.Layouts.FirstOrDefault();
            return any == null ? TrackMatch.None : new TrackMatch { Layout = any, How = "preview" };
        }
    }
}
