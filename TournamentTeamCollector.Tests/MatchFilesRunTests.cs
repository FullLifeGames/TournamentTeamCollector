using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace TournamentTeamCollector.Tests
{
    /// <summary>Whole runs that write Matches/, against a replay server in memory; checks the files against the contract.</summary>
    public class MatchFilesRunTests
    {
        private const string Replay = "https://replay.pokemonshowdown.com/smogtours-gen9ou-848010";
        // The same battle as a Gen 8 replay an hour later: one Match across two Formats.
        private const string Gen8Replay = "https://replay.pokemonshowdown.com/smogtours-gen8ou-848011";
        private const string GoneReplay = "https://replay.pokemonshowdown.com/gen8ou-1369777628";
        private const string OtherGoneReplay = "https://replay.pokemonshowdown.com/gen8challengecup1v1-1686010368";
        private const int UploadTime = 1749304382;

        private TempDirectory _dir = null!;
        private FakeReplayServer _server = null!;
        private RunOptions _options = null!;

        private string Out(string path) => Path.Combine(_options.OutputDirectory, path);

        private JObject Read(string path) => JsonFile.Read(Out(Path.Combine(MatchFiles.Folder, path)));

        private static int Day(int year, int month, int day) => (int)(new DateTime(year, month, day) - DateTime.UnixEpoch).TotalDays;

        [SetUp]
        public void SetUp()
        {
            _dir = new TempDirectory();
            Directory.CreateDirectory(_dir.File("parser"));
            Directory.CreateDirectory(_dir.File("out"));
            var fixture = File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "smogtours-gen9ou-848010.json"));
            var gen8 = JObject.Parse(fixture);
            gen8["id"] = "smogtours-gen8ou-848011";
            gen8["formatid"] = "gen8ou";
            gen8["format"] = "[Gen 8] OU";
            gen8["uploadtime"] = UploadTime + 3600;
            gen8["log"] = ((string)gen8["log"]!).Replace("|tier|[Gen 9] OU", "|tier|[Gen 8] OU");
            _server = new FakeReplayServer().Serve(Replay + ".json", fixture).Serve(Gen8Replay + ".json", gen8.ToString());

            File.WriteAllText(_dir.File("categories.json"), """
                {
                  "categories": ["Official", "Unofficial", "Other"],
                  "fallback": "Other",
                  "forums": [
                    { "category": "Official", "forums": ["tournaments.34"] },
                    { "category": "Unofficial", "forums": ["unofficial-tournaments.666"] }
                  ]
                }
                """);
            File.WriteAllText(_dir.File("parser/forums.json"), """
                {
                  "forums": {
                    "tournaments.34": { "u": "https://www.smogon.com/forums/forums/tournaments.34/", "p": null, "n": "Tournaments" },
                    "unofficial-tournaments.666": { "u": "https://www.smogon.com/forums/forums/unofficial-tournaments.666/", "p": "tournaments.34", "n": null }
                  },
                  "threads": { "3700001": "unofficial-tournaments.666", "3700002": "unofficial-tournaments.666", "3700003": "unofficial-tournaments.666" }
                }
                """);
            _options = new RunOptions()
            {
                MatchesPath = _dir.File("parser/output.json"),
                ReplayDatabasePath = _dir.File("ShowdownReplayScouter.db"),
                OutputDirectory = _dir.File("out"),
                RequestDelay = TimeSpan.Zero,
                MaxConcurrentReplayAnalysis = 1,
                MatchFiles = true,
                ForumsPath = _dir.File("parser/forums.json"),
                CategoriesPath = _dir.File("categories.json"),
            };
            ParserOutput.Write(_options.MatchesPath, ParserOutput.SmogonUsers(
                new Dictionary<string, string>() { ["pitfire"] = "Pitfire", ["cl main"] = "CL Main" },
                ParserOutput.Match("3700001", "Test Open - Round 1", "pitfire", "cl main", Replay, Gen8Replay, GoneReplay).Posted(2025, 6, 7).WonBy("cl main"),
                ParserOutput.Match("3700002", "Test Open - Finals", "pitfire", "alice").Posted(2025, 6, 14),
                // Scouted before the round thread (by name): its link is a Game of the round thread's Match.
                ParserOutput.Match("3700003", "Test Open - Replays", "pitfire", "cl main", Replay).Posted(2025, 6, 20),
                ParserOutput.Match("3700004", "Another Cup - Finals", "alice", "bob", OtherGoneReplay).Posted(2024, 1, 5).WonBy(null)
            ));
        }

        [TearDown]
        public void TearDown() => _dir.Dispose();

        [Test]
        public async Task WritesTheMatchFilesNextToTheOutputs()
        {
            Assert.That(await Collector.RunAsync(_options, _server), Is.EqualTo(ExitCodes.Done));

            Assert.That(Directory.GetFileSystemEntries(_options.OutputDirectory).Select(Path.GetFileName),
                Is.EquivalentTo(new[] { "output.json", "outputJustThreads.json", "newOutput.json", "Tournaments", "Matches" }));
            Assert.That(Directory.GetFileSystemEntries(Out("Matches"), "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(Out("Matches"), path).Replace('\\', '/')),
                Is.EquivalentTo(new[]
                {
                    "index.json", "tournaments.json", "players.json", "Formats", "Formats/gen9ou.json", "Formats/gen8ou.json",
                    "Tournaments", "Tournaments/test-open.json", "Tournaments/another-cup.json",
                }));
            foreach (var file in Directory.GetFiles(Out("Matches"), "*.json", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file);
                Assert.That((int?)JsonFile.Read(file)["v"], Is.EqualTo(1), file);
                Assert.That(text, Does.Not.Contain("\n"), "without indentation");
            }

            // index.json: the Formats with Games, most Games first.
            var index = Read("index.json");
            Assert.That((string?)index["generated"], Does.Match(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ$"));
            Assert.That(index["formats"]!.Select(f => ((string?)f["id"], (string?)f["name"], (int)f["games"]!, (int)f["matches"]!, (int)f["lastDay"]!)),
                Is.EqualTo(new[] { ("gen8ou", "[Gen 8] OU", 1, 1, UploadTime / 86400), ("gen9ou", "[Gen 9] OU", 1, 1, UploadTime / 86400) }));

            // tournaments.json: every Tournament, newest first, its threads oldest first.
            var tournaments = Read("tournaments.json");
            Assert.That(tournaments["categories"]!.Values<string>(), Is.EqualTo(new[] { "Official", "Unofficial", "Other" }));
            var formats = tournaments["formats"]!.Values<string>().ToList();
            Assert.That(formats, Is.EqualTo(new[] { "gen8ou", "gen9ou" }));
            var (open, cup) = (tournaments["tournaments"]![0]!, tournaments["tournaments"]![1]!);
            Assert.That(((string?)open["id"], (string?)open["name"], (int)open["category"]!, (int?)open["year"], (string?)open["forum"], (int)open["games"]!),
                Is.EqualTo(("test-open", "Test Open", 1, (int?)2025, "unofficial-tournaments.666", 2)), "the nearest forum with a rule");
            Assert.That(open["formats"]!.ToString(Newtonsoft.Json.Formatting.None), Is.EqualTo("[[0,1],[1,1]]"));
            Assert.That(open["threads"]!.ToString(Newtonsoft.Json.Formatting.None), Is.EqualTo(
                $"[[3700001,\"Round 1\",{Day(2025, 6, 7)},1,1],[3700002,\"Finals\",{Day(2025, 6, 14)},1,0],[3700003,\"Replays\",{Day(2025, 6, 20)},1,0]]"));
            Assert.That(((string?)cup["id"], (int)cup["category"]!, (string?)cup["forum"], (int)cup["games"]!),
                Is.EqualTo(("another-cup", 2, (string?)null, 0)), "listed without Games; the forum file does not know its thread");
            Assert.That(cup["threads"]!.ToString(Newtonsoft.Json.Formatting.None), Is.EqualTo($"[[3700004,\"Finals\",{Day(2024, 1, 5)},1,0]]"));

            // players.json: the Players with Games, with their Alts.
            var players = Read("players.json");
            Assert.That(players["formats"]!.Values<string>(), Is.EqualTo(formats));
            Assert.That(players["players"]!.ToString(Newtonsoft.Json.Formatting.None), Is.EqualTo(
                "[[\"cl main\",\"CL Main\",[\"chansey and lulu\"],[[0,1],[1,1]]],[\"pitfire\",\"Pitfire\",[\"pitfire\"],[[0,1],[1,1]]]]"));

            // A Format file holds the Match with both its Games, the other Format's too.
            foreach (var format in formats)
            {
                var file = Read($"Formats/{format}.json");
                Assert.That((string?)file["formats"]![0], Is.EqualTo(format));
                AssertMatchOfRoundOne(file);
            }
            var openFile = Read("Tournaments/test-open.json");
            AssertMatchOfRoundOne(openFile);
            Assert.That((string?)openFile["formats"]![0], Is.EqualTo("gen9ou"), "in the order the file uses them");
            var cupFile = Read("Tournaments/another-cup.json");
            foreach (var table in new[] { "players", "species", "teams", "games", "matches" })
            {
                Assert.That(cupFile[table], Is.Empty, table);
            }
        }

        private void AssertMatchOfRoundOne(JObject file)
        {
            var formats = file["formats"]!.Values<string>().ToList();
            Assert.That(file["players"]!.ToString(Newtonsoft.Json.Formatting.None), Is.EqualTo("[[\"pitfire\",\"Pitfire\"],[\"cl main\",\"CL Main\"]]"));
            var match = file["matches"]!.Single();
            Assert.That(match.ToString(Newtonsoft.Json.Formatting.None), Is.EqualTo($"[3700001,0,1,2,{Day(2025, 6, 7)},[0,1]]"), "CL Main won");

            var games = (JArray)file["games"]!;
            Assert.That(games.Select(game => (string?)game[0]), Is.EqualTo(new[] { "smogtours-gen9ou-848010", "smogtours-gen8ou-848011" }), "in the order they were played");
            Assert.That(games.Select(game => formats[(int)game[1]!]), Is.EqualTo(new[] { "gen9ou", "gen8ou" }));
            var teams = (JArray)file["teams"]!;
            foreach (var game in games)
            {
                Assert.That(game, Has.Count.EqualTo(11));
                Assert.That(((int)game[2]! - UploadTime) % 3600, Is.Zero);
                Assert.That(((string?)game[3], (string?)game[4], (int)game[5]!), Is.EqualTo(("pitfire", "chansey and lulu", 2)), "the replay's winner");
                Assert.That((int)game[8]!, Is.EqualTo(1), "Pitfire, Player a, played side 1 as pitfire");
                Assert.That(((int)game[9]!, (int)game[10]!), Is.EqualTo((488, 0)));
                Assert.That(new[] { (int)game[6]!, (int)game[7]! }, Has.All.InRange(0, teams.Count - 1));
                Assert.That((int)game[6]!, Is.Not.EqualTo((int)game[7]!));
            }

            var species = file["species"]!.Values<string>().ToList();
            var moves = file["moves"]!.Values<string>().ToList();
            Assert.That(games.SelectMany(game => new[] { (int)game[6]!, (int)game[7]! }).Distinct().Count(), Is.EqualTo(teams.Count), "only the Teams of its Games");
            foreach (var team in teams)
            {
                Assert.That(team, Has.Count.InRange(1, 6));
                foreach (var mon in team)
                {
                    Assert.That(mon, Has.Count.InRange(6, 7));
                    Assert.That((int)mon[5]!, Is.AnyOf(0, 1));
                    Assert.That((int)mon[0]!, Is.InRange(0, species.Count - 1));
                    Assert.That(mon[4]!.Select(move => (int)move), Has.All.InRange(0, moves.Count - 1));
                    foreach (var (table, position) in new[] { ("items", 1), ("abilities", 2), ("teras", 3) })
                    {
                        Assert.That((int)mon[position]!, Is.InRange(-1, file[table]!.Count() - 1));
                    }
                }
            }

            // The Teams are those of output.json, with their Sets.
            string? Name(string table, JToken index) => (int)index! is var i and >= 0 ? (string?)file[table]![i] : null;
            var written = teams.Select(team => string.Join(" / ", team.Select(mon => Describe(
                Name("species", mon[0]!)!, Name("items", mon[1]!), Name("abilities", mon[2]!), Name("teras", mon[3]!),
                mon[4]!.Select(move => Name("moves", move)!), (int)mon[5]! == 1, mon.Count() > 6 ? mon[6]!.Select(other => Name("species", other)!) : []))));
            Assert.That(written.ToHashSet(), Is.EquivalentTo(TeamsOfOutput("3700001")));
            Assert.That(string.Join(" ", written), Does.Contain("Deoxys-Speed|").And.Contain("Weezing-Galar|").And.Contain("|Deoxys"), "with the other names of a form");
        }

        /// <summary>The Teams of a thread as output.json has them, in the words of <see cref="Describe"/>.</summary>
        private HashSet<string> TeamsOfOutput(string threadId) => JsonFile.Read(Out("output.json"))[threadId]!["Teams"]!
            .Select(team => string.Join(" / ", team["Pokemon"]!.Select(mon => Describe(
                (string)mon["Name"]!, (string?)mon["Item"], (string?)mon["Ability"], (string?)mon["TeraType"], mon["Moves"]!.Values<string>()!, (bool)mon["Lead"]!,
                mon["AltNames"]!.Values<string>().Append((string?)mon["FormName"]).OfType<string>().Select(name => name.Split(", ")[0]).Where(name => name != (string)mon["Name"]!).Distinct()!))))
            .ToHashSet();

        private static string Describe(string species, string? item, string? ability, string? tera, IEnumerable<string> moves, bool lead, IEnumerable<string> others) =>
            $"{species}|{item}|{ability}|{tera}|{string.Join(",", moves)}|{lead}|{string.Join(",", others)}";

        [Test]
        public async Task AStoppedRunLeavesThePreviousMatchFiles()
        {
            Directory.CreateDirectory(Out("Matches"));
            File.WriteAllText(Out("Matches/index.json"), "previous");
            _options.MaxRequests = 1;

            Assert.That(await Collector.RunAsync(_options, _server), Is.EqualTo(ExitCodes.RequestLimit));

            Assert.That(File.ReadAllText(Out("Matches/index.json")), Is.EqualTo("previous"));
            Assert.That(Directory.GetFileSystemEntries(_options.OutputDirectory).Select(Path.GetFileName), Is.EquivalentTo(new[] { "Matches" }));

            _options.MaxRequests = 0;
            Assert.That(await Collector.RunAsync(_options, _server), Is.EqualTo(ExitCodes.Done));

            Assert.That((int?)Read("index.json")["v"], Is.EqualTo(1));
            Assert.That(Directory.GetFileSystemEntries(_options.OutputDirectory).Select(Path.GetFileName),
                Is.EquivalentTo(new[] { "output.json", "outputJustThreads.json", "newOutput.json", "Tournaments", "Matches" }));
        }

        [TestCase(null)]
        [TestCase("missing.json")]
        public async Task WithoutForumFileTheCategoriesComeFromTheNames(string? forums)
        {
            _options.ForumsPath = forums == null ? null : _dir.File(forums);
            var console = Console.Out;
            using var output = new StringWriter();
            Console.SetOut(output);
            try
            {
                Assert.That(await Collector.RunAsync(_options, _server), Is.EqualTo(ExitCodes.Done));
            }
            finally
            {
                Console.SetOut(console);
            }

            Assert.That(output.ToString().Split('\n').Count(line => line.Contains("the Categories of the Tournaments come from their names")), Is.EqualTo(1));
            var tournaments = Read("tournaments.json")["tournaments"]!;
            Assert.That(tournaments.Select(t => ((int)t["category"]!, (string?)t["forum"])), Is.All.EqualTo((2, (string?)null)));
        }
    }
}
