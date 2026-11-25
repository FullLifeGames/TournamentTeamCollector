namespace TournamentTeamCollector
{
    /// <summary>Where a run of the collector reads its matches and how it asks the replay server.</summary>
    public class RunOptions
    {
        public const string OutputFileName = "output.json";

        /// <summary>output.json of TournamentParser: the tournament matches whose replays are scouted.</summary>
        public string? MatchesPath { get; set; }

        /// <summary>
        /// Crawl state of TournamentParser 3.0 (SmogonTournamentParser.db). Only used when <see cref="MatchesPath"/> is not set or
        /// its file does not exist: then the collector crawls Smogon itself.
        /// </summary>
        public string? ParserDatabasePath { get; set; }

        /// <summary>Analysed replays, and replays the server no longer has.</summary>
        public string ReplayDatabasePath { get; set; } = "ShowdownReplayScouter.db";

        /// <summary>Folder that receives output.json, outputJustThreads.json, newOutput.json, Tournaments/ and Matches/.</summary>
        public string OutputDirectory { get; set; } = ".";

        /// <summary>
        /// Whether a complete run also writes Matches/, the files of the Tournament Teams search view. The program turns it on;
        /// it is off here so that a caller of <see cref="Collector.RunAsync"/> that does not ask for it gets exactly the
        /// outputs of version 2.
        /// </summary>
        public bool MatchFiles { get; set; }

        /// <summary>
        /// forums.json of TournamentParser: the forum of each thread, for the Category of a Tournament. Without it, or if
        /// the file does not exist, the Category comes from the name rules alone.
        /// </summary>
        public string? ForumsPath { get; set; }

        /// <summary>The Category rules, a data file of the repository copied next to the program.</summary>
        public string CategoriesPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "Data", "categories.json");

        /// <summary>Hand-kept corrections of the derived Tournaments, Categories and Players, copied next to the program.</summary>
        public string OverridesPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "Data", "overrides.json");

        /// <summary>Pause between two requests to the replay server.</summary>
        public TimeSpan RequestDelay { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>Requests per run after which the run stops (the next one continues from there); 0 means no limit.</summary>
        public int MaxRequests { get; set; }

        /// <summary>How long a replay that the server answered with 404 is not requested again.</summary>
        public TimeSpan MissingReplayRecheck { get; set; } = TimeSpan.FromDays(30);

        /// <summary>
        /// Replays of one tournament analysed at the same time. Requests to the replay server still go out one at a time
        /// (<see cref="TournamentParser.Util.PoliteHttpMessageHandler"/>); this only overlaps cache reads and analysis.
        /// </summary>
        public int MaxConcurrentReplayAnalysis { get; set; } = 4;

        /// <summary>Why these options cannot run, or null.</summary>
        public string? Validate()
        {
            if (MatchesPath == null && ParserDatabasePath == null)
            {
                return "Give the matches to scout: --matches <output.json of TournamentParser>, or --parser-db <SmogonTournamentParser.db> to crawl Smogon.";
            }
            if (MatchesPath != null
                && string.Equals(Path.GetFullPath(MatchesPath), Path.GetFullPath(Path.Combine(OutputDirectory, OutputFileName)), StringComparison.Ordinal))
            {
                return $"--matches {MatchesPath} is the {OutputFileName} this run writes: run the collector in a folder of its own.";
            }
            return null;
        }
    }
}
