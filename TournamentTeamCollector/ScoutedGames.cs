using ShowdownReplayScouter.Core.Data;

namespace TournamentTeamCollector
{
    /// <summary>
    /// The Games and Teams of the scouted threads, kept for the whole run: the match files group them by Format and by
    /// Tournament, across threads, while the run lets go of each thread's teams once it is written. Strings are interned
    /// into tables and a Team is a small array of indices (<see cref="TeamCodec"/>), so all of Smogon's tournaments (about
    /// 270,000 Games) fit in some tens of megabytes.
    /// <para>
    /// A replay is often linked in several matches: in its round thread and again in a thread that collects replays, or
    /// twice in one thread. A Game belongs to one Match (<see cref="ChooseOwner"/>), and it takes the Teams of that Match's
    /// thread, so a replay seen first in another thread waits for the thread of its Match. If that thread has no Teams for
    /// it (its link there names another replay page, or the thread is not scouted), the Game goes to the thread it was
    /// seen in after all.
    /// </para>
    /// </summary>
    public sealed class ScoutedGames
    {
        /// <summary>A Game: one replay that still exists, of one Match.</summary>
        public readonly record struct GameEntry(
            int Match, int Link, int Format, int UploadTime, int Alt1, int Alt2, byte Winner, int Team1, int Team2, int Views, int Rating);

        private const int NoOwner = -1;

        /// <summary>A Team of a fallback Game, stored once when the first Game that uses it is recorded.</summary>
        private sealed class PendingTeam(int[] encoded)
        {
            public int[] Encoded { get; } = encoded;

            public int Index { get; set; } = -1;
        }

        private readonly MatchTable _matches;
        private readonly bool[] _scouted;
        // Replay key hashes of the recorded Games.
        private readonly HashSet<long> _recorded = [];
        // Replay key hash -> the Match that gets the Game, while the Game waits for the thread of that Match.
        private readonly Dictionary<long, int> _owners = [];
        // Replay key hash -> the Game as the thread it was first seen in has it, in case the Match's thread lacks it.
        private readonly Dictionary<long, (GameEntry Game, PendingTeam? Team1, PendingTeam? Team2)> _fallbacks = [];
        // Thread -> the replay key hashes waiting for it.
        private readonly Dictionary<int, List<long>> _waiting = [];
        private readonly List<GameEntry> _games = [];
        private readonly List<int[]> _teams = [];

        public ScoutedGames(MatchTable matches)
        {
            _matches = matches;
            _scouted = new bool[matches.Threads.Count];
        }

        public MatchTable Matches => _matches;

        public IReadOnlyList<GameEntry> Games => _games;

        /// <summary>The Teams, encoded by <see cref="TeamCodec"/> with indices into the tables below.</summary>
        public IReadOnlyList<int[]> Teams => _teams;

        /// <summary>Format ids (gen9ou), and the name of each as the replay shows it ([Gen 9] OU).</summary>
        public StringTable Formats { get; } = new();

        public List<string> FormatNames { get; } = [];

        /// <summary>The Showdown names the players used, as written in the replays.</summary>
        public StringTable Alts { get; } = new();

        public StringTable Species { get; } = new();
        public StringTable Items { get; } = new();
        public StringTable Abilities { get; } = new();
        public StringTable Teras { get; } = new();
        public StringTable Moves { get; } = new();

        /// <summary>
        /// Takes the Games of a scouted thread: the merged teams with their replays, as written to output.json. Call it once
        /// per thread, also when the thread has no teams, so that Games waiting for it are dropped.
        /// </summary>
        public void AddThread(string threadId, IEnumerable<Team>? teams)
        {
            if (!_matches.TryGetThread(threadId, out var thread))
            {
                return;
            }
            try
            {
                if (teams != null)
                {
                    AddGames(thread, teams as IList<Team> ?? [.. teams]);
                }
            }
            finally
            {
                _scouted[thread] = true;
                if (_waiting.Remove(thread, out var waiting))
                {
                    foreach (var hash in waiting)
                    {
                        RecordFallback(hash);
                    }
                }
            }
        }

        /// <summary>
        /// Records the Games still waiting for a thread that was not scouted, as their first thread has them; call it once
        /// all threads are added.
        /// </summary>
        public void Complete()
        {
            foreach (var hash in _fallbacks.Keys.ToList())
            {
                RecordFallback(hash);
            }
            _owners.Clear();
            _waiting.Clear();
        }

        private void AddGames(int thread, IList<Team> teams)
        {
            var links = _matches.Threads[thread].Links;
            var positions = new Dictionary<string, int>(links.Count, StringComparer.Ordinal);
            var keyPositions = new Dictionary<string, int>(links.Count, StringComparer.Ordinal);
            for (var i = 0; i < links.Count; i++)
            {
                positions.TryAdd(links[i], i);
                if (ReplayLinks.Key(links[i]) is string key)
                {
                    keyPositions.TryAdd(key, i);
                }
            }

            // One draft per replay: its data and the team of each side.
            var drafts = new SortedDictionary<int, (Replay Replay, int Team1, int Team2)>();
            for (var team = 0; team < teams.Count; team++)
            {
                foreach (var replay in teams[team].Replays ?? [])
                {
                    int position;
                    if (replay.Link?.OriginalString is string link && positions.TryGetValue(link, out var exact))
                    {
                        position = exact;
                    }
                    else if (replay.Link?.OriginalString is string other && ReplayLinks.Key(other) is string key && keyPositions.TryGetValue(key, out var byKey))
                    {
                        position = byKey;
                    }
                    else
                    {
                        continue;
                    }
                    var draft = drafts.TryGetValue(position, out var known) ? known : (Replay: replay, Team1: -1, Team2: -1);
                    switch (replay.PlayerInfo?.PlayerValue)
                    {
                        case "p1":
                            draft.Team1 = team;
                            break;
                        case "p2":
                            draft.Team2 = team;
                            break;
                    }
                    drafts[position] = draft;
                }
            }

            var stored = new Dictionary<int, int>();
            var pending = new Dictionary<int, PendingTeam>();
            foreach (var (position, draft) in drafts)
            {
                var key = ReplayLinks.Key(links[position]) ?? links[position].ToLowerInvariant();
                var hash = MatchTable.Hash(key);
                if (_recorded.Contains(hash))
                {
                    continue;
                }
                if (!_owners.TryGetValue(hash, out var owner))
                {
                    owner = ChooseOwner(key, thread, draft.Replay, onlyThisThread: false);
                }
                if (owner == NoOwner)
                {
                    continue;
                }
                if (_matches.Matches[owner].Thread == thread)
                {
                    Record(hash, Game(owner, position, draft.Replay, Store(teams, draft.Team1, stored), Store(teams, draft.Team2, stored)));
                    continue;
                }
                if (_owners.TryAdd(hash, owner) && ChooseOwner(key, thread, draft.Replay, onlyThisThread: true) is var fallback and not NoOwner)
                {
                    // Waits for the thread of its Match, which is not scouted yet.
                    _fallbacks.Add(hash, (Game(fallback, position, draft.Replay, -1, -1), Pending(teams, draft.Team1, pending), Pending(teams, draft.Team2, pending)));
                    var waitFor = _matches.Matches[owner].Thread;
                    if (!_waiting.TryGetValue(waitFor, out var waiting))
                    {
                        _waiting.Add(waitFor, waiting = []);
                    }
                    waiting.Add(hash);
                }
            }
        }

        private void Record(long hash, GameEntry game)
        {
            _games.Add(game);
            _recorded.Add(hash);
            _owners.Remove(hash);
            _fallbacks.Remove(hash);
        }

        private void RecordFallback(long hash)
        {
            if (_fallbacks.Remove(hash, out var fallback) && !_recorded.Contains(hash))
            {
                Record(hash, fallback.Game with { Team1 = Store(fallback.Team1), Team2 = Store(fallback.Team2) });
            }
        }

        /// <summary>
        /// The Match a Game belongs to, among the matches that link its replay in this thread or in threads not yet scouted
        /// (a thread scouted before would have taken it): first the match whose users have the Showdown names of the replay's
        /// players, then a match of a round thread over one of a replay collection, then the earliest posted.
        /// </summary>
        private int ChooseOwner(string replayKey, int thread, Replay replay, bool onlyThisThread)
        {
            var alts = (replay.Players ?? []).Where(name => !string.IsNullOrWhiteSpace(name)).Select(TournamentNames.ShowdownId).ToHashSet();
            var best = NoOwner;
            (int Fit, int Collection, int Day, long ThreadId, int Index) bestRank = default;
            foreach (var candidate in _matches.Candidates(replayKey))
            {
                var match = _matches.Matches[candidate];
                if (match.Thread != thread && (onlyThisThread || _scouted[match.Thread]))
                {
                    continue;
                }
                var fit = 0;
                foreach (var player in new[] { match.A, match.B })
                {
                    if (player >= 0 && alts.Contains(TournamentNames.ShowdownId(_matches.PlayerKeys[player])))
                    {
                        fit++;
                    }
                }
                var threadEntry = _matches.Threads[match.Thread];
                var rank = (-fit, threadEntry.IsCollection ? 1 : 0, match.Day < 0 ? int.MaxValue : match.Day, threadEntry.NumericId, candidate);
                if (best == NoOwner || rank.CompareTo(bestRank) < 0)
                {
                    best = candidate;
                    bestRank = rank;
                }
            }
            return best;
        }

        private GameEntry Game(int match, int position, Replay replay, int team1, int team2)
        {
            var formatId = Format(replay, _matches.Threads[_matches.Matches[match].Thread].Links[position]);
            var format = Formats.Intern(formatId);
            if (format == FormatNames.Count)
            {
                FormatNames.Add(string.IsNullOrWhiteSpace(replay.Format) ? formatId : replay.Format);
            }
            var players = (replay.Players ?? []).ToList();
            var alt1 = players.Count > 0 ? players[0] : replay.P1;
            var alt2 = players.Count > 1 ? players[1] : replay.P2;
            var winner = string.IsNullOrWhiteSpace(replay.Winner) ? 0
                : SameShowdownName(replay.Winner, alt1) ? 1
                : SameShowdownName(replay.Winner, alt2) ? 2
                : 0;
            return new GameEntry(
                match, position, format, replay.UploadTime, Alts.Intern(alt1 ?? ""), Alts.Intern(alt2 ?? ""), (byte)winner,
                team1, team2, Math.Max(0, replay.Views), Math.Max(0, replay.Rating ?? 0));
        }

        private static bool SameShowdownName(string a, string? b) =>
            b != null && TournamentNames.ShowdownId(a) is { Length: > 0 } id && id == TournamentNames.ShowdownId(b);

        /// <summary>The format id of the replay: Showdown's, else the one its id names; letters and digits only.</summary>
        private static string Format(Replay replay, string link)
        {
            var id = TournamentNames.Key(replay.FormatId ?? "");
            if (id.Length == 0 && ReplayLinks.Id(link) is string replayId)
            {
                id = TournamentNames.Key(ReplayLinks.FormatOf(replayId) ?? "");
            }
            return id.Length > 0 ? id : "unknown";
        }

        /// <summary>The index of a merged team of this thread among the stored Teams, stored on first use; -1 for none.</summary>
        private int Store(IList<Team> teams, int team, Dictionary<int, int> stored)
        {
            if (team < 0)
            {
                return -1;
            }
            if (!stored.TryGetValue(team, out var index))
            {
                index = _teams.Count;
                _teams.Add(TeamCodec.Encode(teams[team], this));
                stored.Add(team, index);
            }
            return index;
        }

        /// <summary>A merged team of this thread for a fallback Game, encoded once per thread; null for none.</summary>
        private PendingTeam? Pending(IList<Team> teams, int team, Dictionary<int, PendingTeam> pending)
        {
            if (team < 0)
            {
                return null;
            }
            if (!pending.TryGetValue(team, out var encoded))
            {
                pending.Add(team, encoded = new PendingTeam(TeamCodec.Encode(teams[team], this)));
            }
            return encoded;
        }

        private int Store(PendingTeam? team)
        {
            if (team == null)
            {
                return -1;
            }
            if (team.Index < 0)
            {
                team.Index = _teams.Count;
                _teams.Add(team.Encoded);
            }
            return team.Index;
        }
    }
}
