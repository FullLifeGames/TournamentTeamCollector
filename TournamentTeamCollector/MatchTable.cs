using TournamentParser.Data;

namespace TournamentTeamCollector
{
    /// <summary>
    /// The matches of the parser's output as the match files need them, kept for the whole run: per thread its name, how many
    /// matches it has and when they were posted; per match with replay links its thread, players, winner, day and links. The
    /// parser's output holds a million matches, so they are kept compactly: players are indices, links are positions in the
    /// thread's link list (<see cref="ThreadWithReplays.Replays"/>, which the run keeps anyway), and matches without replay
    /// links are only counted.
    /// </summary>
    public sealed class MatchTable
    {
        /// <summary>A Tournament Thread.</summary>
        public sealed class ThreadEntry
        {
            internal ThreadEntry(string id, string name, IReadOnlyList<string> links)
            {
                Id = id;
                Name = name;
                Links = links;
                NumericId = long.TryParse(id, out var numeric) ? numeric : long.MaxValue;
                IsCollection = TournamentNames.IsCollection(name);
            }

            public string Id { get; }

            /// <summary>The thread id as a number, for ordering and for the files; <see cref="long.MaxValue"/> if it is none.</summary>
            public long NumericId { get; }

            public string Name { get; }

            /// <summary>The distinct replay links of the thread, in the order the scouter gets them.</summary>
            public IReadOnlyList<string> Links { get; }

            /// <summary>Whether the thread collects the replays of other threads; see <see cref="TournamentNames.IsCollection"/>.</summary>
            public bool IsCollection { get; }

            /// <summary>Distinct matches of the thread, also those without replay links.</summary>
            public int Matches { get; internal set; }

            /// <summary>Day of the first match post, -1 if unknown.</summary>
            public int FirstDay { get; internal set; } = -1;

            /// <summary>Day of the last match post, -1 if unknown.</summary>
            public int LastDay { get; internal set; } = -1;
        }

        /// <summary>A match with at least one replay link.</summary>
        public readonly record struct MatchEntry(int Thread, int A, int B, byte Winner, int Day, int[] Links);

        private readonly IReadOnlyDictionary<string, string> _aliases;
        private readonly List<ThreadEntry> _threads = [];
        private readonly Dictionary<string, int> _threadIndex = new(StringComparer.Ordinal);
        private readonly List<MatchEntry> _matches = [];
        private readonly List<string> _playerKeys = [];
        private readonly List<string> _playerNames = [];
        private readonly Dictionary<string, int> _playerIndex = new(StringComparer.Ordinal);
        // Replay key hash -> last added entry of _candidates; each entry links to the one added before it for the same key.
        private readonly Dictionary<long, int> _firstCandidate = [];
        private readonly List<(int Match, int Next)> _candidates = [];
        private Dictionary<string, string>? _userNames = new(StringComparer.Ordinal);
        private HashSet<ulong>? _seen = [];

        /// <param name="playerAliases">Smogon user keys that are another Player's alts: alias key to the Player's key.</param>
        public MatchTable(IReadOnlyDictionary<string, string>? playerAliases = null)
        {
            _aliases = playerAliases ?? new Dictionary<string, string>();
        }

        public IReadOnlyList<ThreadEntry> Threads => _threads;

        public IReadOnlyList<MatchEntry> Matches => _matches;

        /// <summary>The user key of each Player, as the parser's output names them.</summary>
        public IReadOnlyList<string> PlayerKeys => _playerKeys;

        /// <summary>The name of each Player as written on Smogon; the key while the user is unknown.</summary>
        public IReadOnlyList<string> PlayerNames => _playerNames;

        public bool TryGetThread(string id, out int thread) => _threadIndex.TryGetValue(id, out thread);

        /// <summary>The Player of a user key after the aliases, or null if no match with replay links names it.</summary>
        public int? FindPlayer(string? key) =>
            !string.IsNullOrWhiteSpace(key) && _playerIndex.TryGetValue(Canonical(key), out var player) ? player : null;

        /// <summary>The Player of a user key after the aliases, added if new; -1 for no key.</summary>
        private int AddPlayer(string? key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return -1;
            }
            key = Canonical(key);
            if (!_playerIndex.TryGetValue(key, out var player))
            {
                player = _playerKeys.Count;
                _playerKeys.Add(key);
                _playerNames.Add(key);
                _playerIndex.Add(key, player);
            }
            return player;
        }

        /// <summary>The matches whose replay links name this replay (<see cref="ReplayLinks.Key"/>).</summary>
        public IEnumerable<int> Candidates(string replayKey)
        {
            if (!_firstCandidate.TryGetValue(Hash(replayKey), out var candidate))
            {
                yield break;
            }
            while (candidate >= 0)
            {
                yield return _candidates[candidate].Match;
                candidate = _candidates[candidate].Next;
            }
        }

        /// <summary>Notes the name a user has on Smogon; the match files show it.</summary>
        internal void AddUser(User user)
        {
            if (_userNames != null && user.Name is string key && !_userNames.ContainsKey(key))
            {
                _userNames.Add(key, string.IsNullOrWhiteSpace(user.NormalName) ? key : user.NormalName);
            }
        }

        /// <summary>
        /// Adds a match of the parser's output, unless the same match was added before (under its other user).
        /// </summary>
        /// <param name="thread">The thread of the match as the run scouts it.</param>
        /// <param name="links">The positions of the match's replay links in <paramref name="thread"/>'s links.</param>
        internal void AddMatch(TournamentMatch match, ThreadWithReplays thread, int[] links)
        {
            if (match.Thread?.Id is not string id || _seen is not HashSet<ulong> seen || !seen.Add(ContentHash(match)))
            {
                return;
            }
            if (!_threadIndex.TryGetValue(id, out var threadIndex))
            {
                threadIndex = _threads.Count;
                _threads.Add(new ThreadEntry(id, thread.Name ?? "", thread.Replays));
                _threadIndex.Add(id, threadIndex);
            }
            var entry = _threads[threadIndex];
            entry.Matches++;
            var day = Day(match.PostDate);
            if (day >= 0)
            {
                entry.FirstDay = entry.FirstDay < 0 ? day : Math.Min(entry.FirstDay, day);
                entry.LastDay = Math.Max(entry.LastDay, day);
            }
            if (links.Length == 0)
            {
                return;
            }

            var a = AddPlayer(match.FirstUser);
            var b = AddPlayer(match.SecondUser);
            var winnerKey = string.IsNullOrWhiteSpace(match.Winner) ? null : Canonical(match.Winner);
            var winner = winnerKey == null ? 0 : a >= 0 && winnerKey == _playerKeys[a] ? 1 : b >= 0 && winnerKey == _playerKeys[b] ? 2 : 0;
            var matchIndex = _matches.Count;
            _matches.Add(new MatchEntry(threadIndex, a, b, (byte)winner, day, links));
            foreach (var link in links)
            {
                if (ReplayLinks.Key(thread.Replays[link]) is string replayKey)
                {
                    var hash = Hash(replayKey);
                    _candidates.Add((matchIndex, _firstCandidate.TryGetValue(hash, out var next) ? next : -1));
                    _firstCandidate[hash] = _candidates.Count - 1;
                }
            }
        }

        /// <summary>Called when all matches are added: names the Players and lets go of what only reading needed.</summary>
        internal void Complete()
        {
            if (_userNames is Dictionary<string, string> names)
            {
                for (var player = 0; player < _playerKeys.Count; player++)
                {
                    if (names.TryGetValue(_playerKeys[player], out var name))
                    {
                        _playerNames[player] = name;
                    }
                }
            }
            _userNames = null;
            _seen = null;
            _matches.TrimExcess();
            _candidates.TrimExcess();
        }

        private string Canonical(string key) => _aliases.TryGetValue(key, out var canonical) ? canonical : key;

        /// <summary>
        /// Whole days since 1970-01-01 UTC; -1 for no date. The parser writes post dates as Smogon shows them, to the minute
        /// and without offset (taken as UTC). A post whose date it could not read gets the time of the crawl instead, with
        /// offset and fractions of a second: that is no post date, and it would chain a thread to every later Tournament of
        /// the same name (2,522 such dates in the output of 2026-11-17).
        /// </summary>
        public static int Day(DateTime time)
        {
            if (time.Year < 1990 || time.Kind == DateTimeKind.Local || time.Ticks % TimeSpan.TicksPerSecond != 0)
            {
                return -1;
            }
            return (int)((time - DateTime.UnixEpoch).Ticks / TimeSpan.TicksPerDay);
        }

        /// <summary>
        /// The match's content: the same match listed under both its users (or twice under one) has the same hash. 64 bits
        /// make a collision among a million matches unlikely (about 1 in 30 million).
        /// </summary>
        private static ulong ContentHash(TournamentMatch match)
        {
            var hash = Fnv.Start;
            hash = Fnv.Add(hash, match.Thread?.Id);
            hash = Fnv.Add(hash, match.FirstUser);
            hash = Fnv.Add(hash, match.SecondUser);
            hash = Fnv.Add(hash, match.Winner);
            hash = Fnv.Add(hash, match.Finished ? "1" : "0");
            hash = Fnv.Add(hash, match.PostDate.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (var replay in match.Replays)
            {
                hash = Fnv.Add(hash, replay);
            }
            return hash;
        }

        internal static long Hash(string text) => (long)Fnv.Add(Fnv.Start, text);

        /// <summary>FNV-1a over the UTF-16 code units, with a separator after each part.</summary>
        private static class Fnv
        {
            public const ulong Start = 14695981039346656037;
            private const ulong Prime = 1099511628211;

            public static ulong Add(ulong hash, string? text)
            {
                foreach (var c in text ?? "\0null")
                {
                    hash = (hash ^ c) * Prime;
                }
                return (hash ^ 0xFFFF) * Prime;
            }
        }
    }
}
