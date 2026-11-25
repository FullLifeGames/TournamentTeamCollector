using Newtonsoft.Json;
using NUnit.Framework;
using ShowdownReplayScouter.Core.Data;
using System.Diagnostics;
using System.Text;

namespace TournamentTeamCollector.Tests
{
    /// <summary>
    /// The derivation of Matches/ on a real parser output, without a single replay download: how the threads group into
    /// Tournaments and Categories, and what the run keeps in memory. Run by hand, with the paths in environment variables:
    /// <list type="bullet">
    /// <item>TTC_DERIVATION_MATCHES: output.json of TournamentParser (required).</item>
    /// <item>TTC_DERIVATION_FORUMS: forums.json of TournamentParser (optional).</item>
    /// <item>TTC_DERIVATION_OUTPUT: folder for the report, the derived Tournaments and the match files of the simulated
    /// Games (default: the test's work folder).</item>
    /// <item>TTC_DERIVATION_GAMES: how many Games to simulate from the real replay links for the memory estimate (default
    /// 270000; 0 for none). The Games get made-up Teams: they measure size, not content.</item>
    /// </list>
    /// <c>dotnet test --filter FullyQualifiedName~DerivationTests</c>
    /// </summary>
    [Explicit("Reads a real parser output; see the class comment")]
    public class DerivationTests
    {
        [Test]
        public void DerivesTheTournamentsOfARealParserOutput()
        {
            var matchesPath = Environment.GetEnvironmentVariable("TTC_DERIVATION_MATCHES");
            if (string.IsNullOrEmpty(matchesPath))
            {
                Assert.Ignore("TTC_DERIVATION_MATCHES is not set");
            }
            var forumsPath = Environment.GetEnvironmentVariable("TTC_DERIVATION_FORUMS");
            var outputDirectory = Environment.GetEnvironmentVariable("TTC_DERIVATION_OUTPUT") is { Length: > 0 } output ? output : TestContext.CurrentContext.WorkDirectory;
            var gameTarget = int.TryParse(Environment.GetEnvironmentVariable("TTC_DERIVATION_GAMES"), out var target) ? target : 270_000;
            Directory.CreateDirectory(outputDirectory);
            var report = new StringBuilder();
            void Line(string text = "")
            {
                report.AppendLine(text);
                TestContext.Out.WriteLine(text);
            }

            // What today's run keeps of the matches, then the same with the match table.
            var (plainBytes, plainTime) = ReadWithoutTable(matchesPath);
            var run = MatchFileRun.Load(new RunOptions() { ForumsPath = forumsPath });
            var baseline = Heap();
            var clock = Stopwatch.StartNew();
            var threads = TournamentMatches.GroupByThread(TournamentMatches.ReadParserOutput(matchesPath), run.Matches);
            var readTime = clock.Elapsed;
            var withTableBytes = Heap() - baseline;
            clock.Restart();
            run.Derive();
            var deriveTime = clock.Elapsed;

            var table = run.Matches;
            var tournaments = run.Tournaments;
            Line($"Derivation of Matches/ from {matchesPath}");
            Line($"Forums: {(run.Forums == null ? "none" : $"{forumsPath}, {run.Forums.ForumCount} forums, {run.Forums.ThreadCount} threads")}");
            Line();
            Line($"Threads: {table.Threads.Count} ({threads.Count} with a match the run scouts), matches with replay links: {table.Matches.Count}, Players: {table.PlayerKeys.Count}");
            Line($"Distinct matches: {table.Threads.Sum(thread => thread.Matches)}");
            Line($"Tournaments: {tournaments.Count}");
            Line();
            Line("Tournaments per Category:");
            foreach (var group in tournaments.GroupBy(t => t.Category).OrderBy(group => group.Key))
            {
                Line($"  {run.Rules.Categories[group.Key],-16} {group.Count(),5} Tournaments, {group.Sum(t => t.Threads.Count),6} threads");
            }
            Line($"  by name rule: {tournaments.Count(t => run.Rules.ByName(t.Name) != null)}, by forum rule: {tournaments.Count(t => run.Rules.ByName(t.Name) == null && run.Rules.ByForum(t.Forum, run.Forums) != null)}, fallback: {tournaments.Count(t => run.Rules.ByName(t.Name) == null && run.Rules.ByForum(t.Forum, run.Forums) == null)}");
            Line($"  without known forum: {tournaments.Count(t => t.Forum == null)}");
            Line();
            Line("Memory (managed heap after a full collection):");
            Line($"  today's run, threads with their replay links: {Mb(plainBytes)} (read in {plainTime.TotalSeconds:0} s)");
            Line($"  with the match table:                         {Mb(withTableBytes)} (read in {readTime.TotalSeconds:0} s), so the table adds {Mb(withTableBytes - plainBytes)}");
            Line($"  Tournaments derived in {deriveTime.TotalSeconds:0.0} s");

            void Describe(Tournament tournament)
            {
                Line($"{tournament.Name}  [{tournament.Id}]  {run.Rules.Categories[tournament.Category]}, {tournament.Year?.ToString() ?? "no year"}, forum {tournament.Forum ?? "unknown"}, {tournament.Threads.Count} threads");
                foreach (var (thread, round) in tournament.Threads)
                {
                    var entry = table.Threads[thread];
                    Line($"    {entry.Id,8}  {(round.Length == 0 ? "(whole Tournament)" : round),-40}  {entry.Matches,4} matches  \"{entry.Name}\"");
                }
            }
            Line();
            Line("The 40 Tournaments with the most threads:");
            foreach (var tournament in tournaments.OrderByDescending(t => t.Threads.Count).ThenBy(t => t.Id, StringComparer.Ordinal).Take(40))
            {
                Describe(tournament);
            }
            Line();
            Line("40 Tournaments at random (seed 20261121):");
            var random = new Random(20261121);
            foreach (var tournament in tournaments.OrderBy(t => t.Id, StringComparer.Ordinal).OrderBy(_ => random.Next()).Take(40))
            {
                Describe(tournament);
            }

            using (var file = File.CreateText(Path.Combine(outputDirectory, "derived-tournaments.json")))
            {
                new JsonSerializer().Serialize(file, tournaments.Select(t => new
                {
                    id = t.Id,
                    n = t.Name,
                    y = t.Year,
                    c = run.Rules.Categories[t.Category],
                    f = t.Forum,
                    th = t.Threads.Select(entry => new object[] { table.Threads[entry.Thread].Id, entry.Round, table.Threads[entry.Thread].FirstDay, table.Threads[entry.Thread].Matches, table.Threads[entry.Thread].Name }),
                }));
            }

            if (gameTarget > 0)
            {
                Line();
                SimulateGames(run, threads, gameTarget, Path.Combine(outputDirectory, "simulated-matches"), Line);
            }
            File.WriteAllText(Path.Combine(outputDirectory, "collector-derivation.txt"), report.ToString());
        }

        private static (long Bytes, TimeSpan Time) ReadWithoutTable(string path)
        {
            var baseline = Heap();
            var clock = Stopwatch.StartNew();
            var threads = TournamentMatches.GroupByThread(TournamentMatches.ReadParserOutput(path));
            var time = clock.Elapsed;
            var bytes = Heap() - baseline;
            GC.KeepAlive(threads);
            return (bytes, time);
        }

        /// <summary>
        /// Feeds made-up Teams for the real replay links of the threads, in the order a run scouts them, until there are
        /// <paramref name="target"/> Games; measures what the run keeps and what writing Matches/ takes.
        /// </summary>
        private static void SimulateGames(MatchFileRun run, Dictionary<string, ThreadWithReplays> threads, int target, string directory, Action<string> line)
        {
            var table = run.Matches;
            var matchesOfThread = new Dictionary<int, List<int>>();
            for (var match = 0; match < table.Matches.Count; match++)
            {
                if (!matchesOfThread.TryGetValue(table.Matches[match].Thread, out var list))
                {
                    matchesOfThread.Add(table.Matches[match].Thread, list = []);
                }
                list.Add(match);
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var baseline = Heap();
            var clock = Stopwatch.StartNew();
            foreach (var (id, _) in threads.OrderBy(entry => entry.Value.Name, StringComparer.Ordinal))
            {
                if (run.Games.Games.Count >= target)
                {
                    break;
                }
                if (!table.TryGetThread(id, out var thread) || !matchesOfThread.TryGetValue(thread, out var matches))
                {
                    run.AddThread(id, []);
                    continue;
                }
                var teams = new List<Team>();
                foreach (var match in matches)
                {
                    var entry = table.Matches[match];
                    var (alt1, alt2) = (entry.A >= 0 ? table.PlayerKeys[entry.A] : "", entry.B >= 0 ? table.PlayerKeys[entry.B] : "");
                    var replays = entry.Links.Select(link => table.Threads[thread].Links[link])
                        .Where(link => ReplayLinks.Key(link) is string key && seen.Add(key)).ToList();
                    if (replays.Count == 0)
                    {
                        continue;
                    }
                    foreach (var (side, seed) in new[] { ("p1", entry.A), ("p2", entry.B) })
                    {
                        teams.Add(new Team()
                        {
                            Pokemon = Enumerable.Range(0, 6).Select(slot => MadeUpPokemon(seed * 31 + thread * 7 + slot, slot == 0)).ToList(),
                            Replays = replays.Select(link => new Replay()
                            {
                                Id = ReplayLinks.Id(link)!,
                                Link = new Uri(link),
                                FormatId = ReplayLinks.FormatOf(ReplayLinks.Id(link)!) ?? "",
                                Format = ReplayLinks.FormatOf(ReplayLinks.Id(link)!) ?? "",
                                Players = [alt1, alt2],
                                P1 = alt1,
                                P2 = alt2,
                                Winner = alt1,
                                UploadTime = Math.Max(0, entry.Day) * 86400,
                                Views = 100,
                                PlayerInfo = new PlayerInfo() { PlayerValue = side },
                            }).ToList(),
                        });
                    }
                }
                run.AddThread(id, teams);
            }
            var feedTime = clock.Elapsed;
            // Only the simulation needs it.
            seen.Clear();
            seen.TrimExcess();
            var gamesBytes = Heap() - baseline;
            var games = run.Games;
            line($"Simulated Games: {games.Games.Count} Games with {games.Teams.Count} Teams in {games.Formats.Count} Formats, {games.Alts.Count} Alts, fed in {feedTime.TotalSeconds:0} s");
            line($"  kept for Matches/: {Mb(gamesBytes)} (Teams {Mb(games.Teams.Sum(team => 24L + 2 * team.Length))} of it)");

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            var peakBefore = process.PeakWorkingSet64;
            var allocated = GC.GetTotalAllocatedBytes(true);
            clock.Restart();
            run.Write(directory);
            var writeTime = clock.Elapsed;
            process.Refresh();
            var files = Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories).Select(path => new FileInfo(path)).ToList();
            line($"  Matches/ written in {writeTime.TotalSeconds:0} s: {files.Count} files, {Mb(files.Sum(file => file.Length))}, largest {files.MaxBy(file => file.Length)?.Name} with {Mb(files.Max(file => file.Length))}");
            line($"  allocated while writing: {Mb(GC.GetTotalAllocatedBytes(true) - allocated)}; peak working set of the test process {Mb(peakBefore)} before, {Mb(process.PeakWorkingSet64)} after");
        }

        private static readonly string[] Items = [.. Enumerable.Range(0, 120).Select(i => $"Item {i}")];
        private static readonly string[] Moves = [.. Enumerable.Range(0, 700).Select(i => $"Move {i}")];

        private static Pokemon MadeUpPokemon(int seed, bool lead)
        {
            var random = new Random(seed);
            return new Pokemon()
            {
                Name = $"Species {random.Next(450)}",
                Item = Items[random.Next(Items.Length)],
                Ability = $"Ability {random.Next(250)}",
                TeraType = random.Next(3) == 0 ? $"Tera {random.Next(19)}" : null!,
                Lead = lead,
                Moves = [.. Enumerable.Range(0, random.Next(2, 5)).Select(_ => Moves[random.Next(Moves.Length)])],
            };
        }

        private static long Heap()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            return GC.GetTotalMemory(true);
        }

        private static string Mb(long bytes) => $"{bytes / 1024.0 / 1024.0:0.0} MB";
    }
}
