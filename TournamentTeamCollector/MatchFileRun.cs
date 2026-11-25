using ShowdownReplayScouter.Core.Data;

namespace TournamentTeamCollector
{
    /// <summary>
    /// What a run keeps for Matches/ besides today's outputs: the rules and overrides, the forum tree, the matches as read,
    /// the Tournaments derived from them and the Games of the scouted threads. The Tournaments are derived before the first
    /// replay is scouted, so that a wrong rule or override stops the run before hours of downloads.
    /// </summary>
    public sealed class MatchFileRun
    {
        private IReadOnlyList<Tournament>? _tournaments;
        private int[]? _tournamentOfThread;
        private ScoutedGames? _games;

        private MatchFileRun(CategoryRules rules, Overrides overrides, ForumTree? forums)
        {
            Rules = rules;
            Overrides = overrides;
            Forums = forums;
            Matches = new MatchTable(overrides.PlayerAliases());
        }

        public CategoryRules Rules { get; }

        public Overrides Overrides { get; }

        /// <summary>The forum tree, null without forums.json.</summary>
        public ForumTree? Forums { get; }

        /// <summary>Receives the matches while they are read (<see cref="TournamentMatches.GroupByThread"/>).</summary>
        public MatchTable Matches { get; }

        public IReadOnlyList<Tournament> Tournaments => _tournaments ?? throw new InvalidOperationException("Derive the Tournaments first");

        public ScoutedGames Games => _games ?? throw new InvalidOperationException("Derive the Tournaments first");

        /// <summary>
        /// Reads the Category rules, the overrides and, if there is one, the forum file. Without a forum file the run says
        /// so once: the Categories then come from the name rules and the fallback.
        /// </summary>
        public static MatchFileRun Load(RunOptions options)
        {
            var rules = CategoryRules.Load(options.CategoriesPath);
            var overrides = Overrides.Load(options.OverridesPath);
            ForumTree? forums = null;
            if (options.ForumsPath == null)
            {
                Console.WriteLine($"No forum file (--forums): the Categories of the Tournaments come from their names, else \"{rules.Categories[rules.Fallback]}\"");
            }
            else if (!File.Exists(options.ForumsPath))
            {
                Console.WriteLine($"{options.ForumsPath} does not exist: the Categories of the Tournaments come from their names, else \"{rules.Categories[rules.Fallback]}\"");
            }
            else
            {
                try
                {
                    forums = ForumTree.Load(options.ForumsPath);
                    Console.WriteLine($"Read {forums.ForumCount} forums and the forums of {forums.ThreadCount} threads from {options.ForumsPath}");
                }
                catch (InvalidDataException e)
                {
                    // The forums only decide Categories: a broken file must not cost the run.
                    Console.Error.WriteLine($"{e.Message}: the Categories of the Tournaments come from their names, else \"{rules.Categories[rules.Fallback]}\"");
                }
            }
            return new MatchFileRun(rules, overrides, forums);
        }

        /// <summary>Groups the threads of the read matches into Tournaments; call it once all matches are read.</summary>
        public void Derive()
        {
            (_tournaments, _tournamentOfThread) = TournamentGrouping.Derive(Matches, Forums, Rules, Overrides);
            _games = new ScoutedGames(Matches);
            var perCategory = _tournaments.GroupBy(tournament => tournament.Category).OrderBy(group => group.Key)
                .Select(group => $"{group.Count()} {Rules.Categories[group.Key]}");
            Console.WriteLine($"{Matches.Threads.Count} threads in {_tournaments.Count} Tournaments: {string.Join(", ", perCategory)}");
        }

        /// <summary>Takes the Games of a scouted thread; see <see cref="ScoutedGames.AddThread"/>.</summary>
        public void AddThread(string threadId, IEnumerable<Team>? teams) => Games.AddThread(threadId, teams);

        /// <summary>Writes the files of Matches/ into <paramref name="directory"/>.</summary>
        public void Write(string directory)
        {
            Games.Complete();
            MatchFiles.Write(directory, Games, Tournaments, _tournamentOfThread!, Rules, Overrides, DateTime.UtcNow);
            Console.WriteLine($"Wrote {Games.Games.Count} Games of {Games.Formats.Count} Formats with {Games.Teams.Count} Teams into {MatchFiles.Folder}/");
        }
    }
}
