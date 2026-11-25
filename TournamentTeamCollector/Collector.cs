using NeoSmart.Caching.Sqlite;
using ShowdownReplayScouter.Core.Data;
using TournamentParser.Data;
using TournamentParser.Parser;
using TournamentParser.ThreadCollector;
using TournamentParser.Util;
using Scouter = ShowdownReplayScouter.Core.ReplayScouter.ShowdownReplayScouter;

namespace TournamentTeamCollector
{
    public static class ExitCodes
    {
        public const int Done = 0;
        public const int Error = 1;
        /// <summary>Smogon's bot check or an unexpected page (crawl fallback), or the replay server refusing requests.</summary>
        public const int Stopped = 2;
        public const int RequestLimit = 3;
        public const int Usage = 64;
    }

    public static class Collector
    {
        public const string UserAgent = "TournamentTeamCollector (+https://github.com/FullLifeGames/TournamentTeamCollector)";

        /// <summary>
        /// Reads the matches, scouts the replays of every tournament and writes the outputs. Outputs are written only after a
        /// complete run; a run that stops leaves them as they were, and the next run continues from the analysed replays.
        /// </summary>
        /// <param name="replayServer">Sends the replay requests; plain HTTP by default. Tests pass a fake server.</param>
        /// <returns>One of <see cref="ExitCodes"/>.</returns>
        public static async Task<int> RunAsync(RunOptions options, HttpMessageHandler? replayServer = null)
        {
            if (options.Validate() is string invalid)
            {
                Console.Error.WriteLine(invalid);
                return ExitCodes.Usage;
            }

            SQLitePCL.Batteries_V2.Init();

            Dictionary<string, ThreadWithReplays> tournaments;
            MatchFileRun? matchFiles = null;
            try
            {
                // Before the matches: a broken rule or override file stops the run before it reads 440 MB.
                matchFiles = options.MatchFiles ? MatchFileRun.Load(options) : null;
                tournaments = await LoadTournamentsAsync(options, matchFiles?.Matches).ConfigureAwait(false);
                if (tournaments.Count > 0)
                {
                    matchFiles?.Derive();
                }
            }
            catch (Exception e) when (e is SmogonAccessBlockedException or UnexpectedPageException)
            {
                Console.Error.WriteLine(e.Message);
                return ExitCodes.Stopped;
            }
            catch (RequestBudgetExhaustedException e)
            {
                Console.Error.WriteLine(e.Message);
                return ExitCodes.RequestLimit;
            }
            catch (Exception e) when (e is FileNotFoundException or InvalidDataException or Newtonsoft.Json.JsonException)
            {
                Console.Error.WriteLine($"{e.Message}; the outputs are left as they were.");
                return ExitCodes.Error;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e);
                return ExitCodes.Error;
            }
            Console.WriteLine($"{tournaments.Count} tournaments with {tournaments.Values.Sum(t => t.Replays.Count)} replays");
            if (tournaments.Count == 0)
            {
                // An empty output would replace every tournament on the site.
                Console.Error.WriteLine("The matches name no tournament; the outputs are left as they were.");
                return ExitCodes.Error;
            }

            using var replayCache = new SqliteCache(new SqliteCacheOptions() { MemoryOnly = false, CachePath = options.ReplayDatabasePath });
            var polite = new PoliteHttpMessageHandler(
                new PoliteHttpOptions() { MinDelay = options.RequestDelay },
                replayServer ?? PoliteHttpMessageHandler.CreatePlainHttpHandler()
            );
            using var guard = new ReplayRequestGuard(replayCache, options.MaxRequests, options.MissingReplayRecheck, polite);
            using var client = new HttpClient(guard) { Timeout = TimeSpan.FromMinutes(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            ShowdownReplayScouter.Core.Util.Common.HttpClient = client;
            try
            {
                // Removes what it wrote unless committed: a stop or an error leaves the previous outputs as they were.
                using var writer = new OutputWriter(options.OutputDirectory, matchFiles);
                await ScoutAsync(tournaments, new Scouter(replayCache), options, writer, guard.StopToken).ConfigureAwait(false);
                await writer.CommitAsync().ConfigureAwait(false);
                Console.WriteLine($"Wrote {tournaments.Count} tournaments with {writer.Teams} teams");
                return ExitCodes.Done;
            }
            catch (Exception) when (guard.StopReason != StopReason.None)
            {
                // Usually an OperationCanceledException, but HttpClient and the scouter's dataflow block may wrap it.
                Console.Error.WriteLine($"{guard.StopMessage} The outputs are left as they were; analysed replays are kept, so the next run continues from there.");
                return guard.StopReason == StopReason.RequestLimit ? ExitCodes.RequestLimit : ExitCodes.Stopped;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e);
                return ExitCodes.Error;
            }
            finally
            {
                Console.WriteLine(guard);
                Console.WriteLine(polite.Statistics);
                ShowdownReplayScouter.Core.Util.Common.HttpClient = null!;
            }
        }

        private static async Task<Dictionary<string, ThreadWithReplays>> LoadTournamentsAsync(RunOptions options, MatchTable? matches)
        {
            if (options.MatchesPath is string matchesPath && File.Exists(matchesPath))
            {
                Console.WriteLine($"Reading the matches from {matchesPath}");
                return TournamentMatches.GroupByThread(TournamentMatches.ReadParserOutput(matchesPath), matches);
            }
            if (options.ParserDatabasePath is string parserDatabasePath)
            {
                if (options.MatchesPath != null)
                {
                    Console.WriteLine($"{options.MatchesPath} does not exist; crawling Smogon instead");
                }
                return TournamentMatches.GroupByThread((await CrawlSmogonAsync(parserDatabasePath, options.MaxRequests).ConfigureAwait(false)).Values, matches);
            }
            throw new FileNotFoundException($"{options.MatchesPath} does not exist", options.MatchesPath);
        }

        /// <summary>
        /// The fallback without a TournamentParser run: crawls Smogon with TournamentParser 3.0 as TournamentParser.Cmd does
        /// (plain HTTP, a headless Chromium for the bot check, polite requests), keeping the crawl state in the given database.
        /// After the first run only changes are read. Never point it at a database that a TournamentParser job writes at the
        /// same time.
        /// </summary>
        private static async Task<IDictionary<string, User>> CrawlSmogonAsync(string parserDatabasePath, int maxRequests)
        {
            Console.WriteLine($"Crawling Smogon with TournamentParser, crawl state in {parserDatabasePath}");
            var smogon = new PoliteHttpMessageHandler();
            TournamentParser.Util.Common.HttpClient = PoliteHttpMessageHandler.CreateClient(smogon);
            try
            {
                using var parserCache = new SqliteCache(new SqliteCacheOptions() { MemoryOnly = false, CachePath = parserDatabasePath });
                var parser = new SmogonParser(parserCache, new CollectorOptions() { MaxRequests = maxRequests });
                return await parser.GetMatchesForUsers().ConfigureAwait(false);
            }
            finally
            {
                Console.WriteLine("Smogon " + smogon.Statistics);
                // Closes the browser
                TournamentParser.Util.Common.HttpClient.Dispose();
            }
        }

        /// <summary>
        /// Replays per call of the scouter. The scouter keeps the replays it analyses in memory and writes them to the cache only
        /// when the call completes: a run that stops in the middle of a large tournament keeps the chunks before the stop, so the
        /// next run continues there instead of downloading the whole tournament again (and never getting past it when it holds
        /// more replays than the request limit allows).
        /// </summary>
        public const int ReplaysPerScoutingCall = 20;

        /// <summary>
        /// Scouts the tournaments in the order of output.json and hands each to the writer right away, then lets go of its
        /// teams: a run holds the teams of one tournament at a time.
        /// </summary>
        private static async Task ScoutAsync(Dictionary<string, ThreadWithReplays> tournaments, Scouter scouter, RunOptions options, OutputWriter writer, CancellationToken stop)
        {
            var count = 1;
            foreach (var (id, tournament) in tournaments.OrderBy(kv => kv.Value.Name, OutputWriter.NameOrder))
            {
                Console.WriteLine($"Scouting {tournament.Replays.Count} replays for: {tournament.Name} ({count}/{tournaments.Count})");
                try
                {
                    var teams = new List<Team>();
                    foreach (var chunk in tournament.Replays.Chunk(ReplaysPerScoutingCall))
                    {
                        var scoutingResult = await scouter.ScoutReplaysAsync(new ScoutingRequest()
                        {
                            Links = chunk.Select(replay => new Uri(replay)).ToList(),
                            MaxConcurrentReplayAnalysis = options.MaxConcurrentReplayAnalysis,
                            // Merged below over the whole tournament, as one grouped request would.
                            Grouped = false,
                        }, stop).ConfigureAwait(false);
                        // A replay that is gone yields a team without replay and Pokémon.
                        teams.AddRange(scoutingResult?.Teams.Where(team => team.IsValid()) ?? []);
                    }
                    // The same team in several replays of the tournament becomes one team with all of them.
                    var merged = scouter.TeamMerger.MergeTeams(teams).ToList();
                    tournament.ScoutingResult = new ApiScoutingResult() { Teams = merged };
                    Console.WriteLine($"Scouted {merged.Count} teams for: {tournament.Name} ({count}/{tournaments.Count})");
                }
                catch (Exception e) when (!stop.IsCancellationRequested)
                {
                    Console.Error.WriteLine($"Error on thread: {tournament.Name}: {e.Message}");
                }
                await writer.AddAsync(id, tournament).ConfigureAwait(false);
                tournament.ScoutingResult = null;
                count++;
            }
        }
    }
}
