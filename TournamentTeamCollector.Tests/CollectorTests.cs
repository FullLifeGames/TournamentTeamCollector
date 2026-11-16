using Newtonsoft.Json.Linq;
using NUnit.Framework;
using System.Net;

namespace TournamentTeamCollector.Tests
{
    /// <summary>Whole runs against a replay server in memory; no request leaves the process.</summary>
    public class CollectorTests
    {
        private const string Replay = "https://replay.pokemonshowdown.com/smogtours-gen9ou-848010";
        private const string GoneReplay = "https://replay.pokemonshowdown.com/gen8ou-1369777628";
        private const string OtherGoneReplay = "https://replay.pokemonshowdown.com/gen8challengecup1v1-1686010368";

        private TempDirectory _dir = null!;
        private FakeReplayServer _server = null!;
        private RunOptions _options = null!;

        private string Out(string path) => Path.Combine(_options.OutputDirectory, path);

        [SetUp]
        public void SetUp()
        {
            _dir = new TempDirectory();
            Directory.CreateDirectory(_dir.File("parser"));
            Directory.CreateDirectory(_dir.File("out"));
            var fixture = File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "smogtours-gen9ou-848010.json"));
            _server = new FakeReplayServer().Serve(Replay + ".json", fixture);
            _options = new RunOptions()
            {
                MatchesPath = _dir.File("parser/output.json"),
                ReplayDatabasePath = _dir.File("ShowdownReplayScouter.db"),
                OutputDirectory = _dir.File("out"),
                RequestDelay = TimeSpan.Zero,
                // One replay at a time, so that a stop always falls on the same request.
                MaxConcurrentReplayAnalysis = 1,
            };
            ParserOutput.Write(_options.MatchesPath, ParserOutput.Users(
                ParserOutput.Match("3700001", "Test Open - Round 1", "pitfire", "chansey and lulu", Replay, GoneReplay),
                ParserOutput.Match("3700002", "Another Cup - Finals", "Alice", "Bob", OtherGoneReplay)
            ));
        }

        [TearDown]
        public void TearDown() => _dir.Dispose();

        private void WritePreviousOutputs()
        {
            File.WriteAllText(Out("output.json"), "previous");
            Directory.CreateDirectory(Out("Tournaments"));
            File.WriteAllText(Out("Tournaments/3600000.json"), "previous");
        }

        private void AssertPreviousOutputsUntouched()
        {
            Assert.That(File.ReadAllText(Out("output.json")), Is.EqualTo("previous"));
            Assert.That(File.Exists(Out("Tournaments/3600000.json")), Is.True);
            Assert.That(File.Exists(Out("newOutput.json")), Is.False);
        }

        [Test]
        public async Task WritesTheOutputsOfTheTournaments()
        {
            Assert.That(await Collector.RunAsync(_options, _server), Is.EqualTo(ExitCodes.Done));

            var output = JObject.Parse(File.ReadAllText(Out("output.json")));
            Assert.That(output.Properties().Select(p => p.Name), Is.EqualTo(new[] { "3700002", "3700001" }), "ordered by name");
            var tournament = output["3700001"]!;
            Assert.That((string?)tournament["n"], Is.EqualTo("Test Open - Round 1"));
            Assert.That((bool?)tournament["l"], Is.True);
            Assert.That(tournament["Replays"]!.Values<string>(), Is.EquivalentTo(new[] { Replay, GoneReplay }));
            var teams = (JArray)tournament["Teams"]!;
            Assert.That(teams, Has.Count.EqualTo(2), "both players of the replay that still exists");
            Assert.That(teams.SelectMany(team => team["Pokemon"]!), Is.Not.Empty);
            Assert.That(output["3700002"]!["Teams"], Is.Empty);

            var justThreads = JObject.Parse(File.ReadAllText(Out("outputJustThreads.json")));
            Assert.That((string?)justThreads["3700001"]!["n"], Is.EqualTo("Test Open - Round 1"));
            Assert.That(File.ReadAllText(Out("Tournaments/outputJustThreads.json")), Is.EqualTo(File.ReadAllText(Out("outputJustThreads.json"))));

            var perTournament = JObject.Parse(File.ReadAllText(Out("Tournaments/3700001.json")));
            Assert.That((JArray)perTournament["Teams"]!, Has.Count.EqualTo(2));
            Assert.That((string?)perTournament["Outputs"]!["Header"], Does.Contain("Test Open - Round 1"));
            Assert.That(File.ReadAllText(Out("Tournaments/3700001.txt")), Does.Contain("Test Open - Round 1"));
            Assert.That(File.Exists(Out("Tournaments/3700002.json")), Is.True);

            var newOutput = JObject.Parse(File.ReadAllText(Out("newOutput.json")));
            Assert.That(newOutput["3700001"]!["ScoutingResult"]!["Outputs"], Is.Not.Null.And.Not.Empty);

            Assert.That(Directory.GetFileSystemEntries(_options.OutputDirectory).Select(Path.GetFileName),
                Is.EquivalentTo(new[] { "output.json", "outputJustThreads.json", "newOutput.json", "Tournaments" }));
        }

        [Test]
        public async Task ASecondRunSendsNoRequestForAnalysedOrGoneReplays()
        {
            Assert.That(await Collector.RunAsync(_options, _server), Is.EqualTo(ExitCodes.Done));
            Assert.That(_server.Requests, Has.Count.EqualTo(3));
            var first = File.ReadAllText(Out("Tournaments/3700001.json"));

            Assert.That(await Collector.RunAsync(_options, _server), Is.EqualTo(ExitCodes.Done));

            Assert.That(_server.Requests, Has.Count.EqualTo(3));
            Assert.That(JObject.Parse(File.ReadAllText(Out("Tournaments/3700001.json")))["Teams"], Has.Count.EqualTo(2));
            Assert.That(first, Does.Contain("pitfire"));
        }

        [Test]
        public async Task TheRequestLimitStopsTheRunAndTheNextOneContinues()
        {
            WritePreviousOutputs();
            _options.MaxRequests = 1;

            Assert.That(await Collector.RunAsync(_options, _server), Is.EqualTo(ExitCodes.RequestLimit));
            AssertPreviousOutputsUntouched();
            Assert.That(_server.Requests, Is.EqualTo(new[] { Replay + ".json" }));

            _options.MaxRequests = 0;
            Assert.That(await Collector.RunAsync(_options, _server), Is.EqualTo(ExitCodes.Done));

            // The stop fell into the chunk of the analysed replay, so it is requested again (see
            // ATournamentLargerThanTheRequestLimitIsDoneOverSeveralRuns for kept chunks); the old output's tournaments are gone.
            Assert.That(_server.Requests, Has.Count.EqualTo(4));
            Assert.That(JObject.Parse(File.ReadAllText(Out("output.json")))["3700001"]!["Teams"], Has.Count.EqualTo(2));
            Assert.That(File.Exists(Out("Tournaments/3600000.json")), Is.False);
        }

        [Test]
        public async Task ATournamentLargerThanTheRequestLimitIsDoneOverSeveralRuns()
        {
            var fixture = File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "smogtours-gen9ou-848010.json"));
            var replays = Enumerable.Range(1, Collector.ReplaysPerScoutingCall * 2 + 5).Select(i => $"{Replay}{i}").ToArray();
            foreach (var replay in replays)
            {
                _server.Serve(replay + ".json", fixture);
            }
            ParserOutput.Write(_options.MatchesPath!, ParserOutput.Users(ParserOutput.Match("3700001", "Test Open - Replays", "Alice", "Bob", replays)));
            _options.MaxRequests = Collector.ReplaysPerScoutingCall + 5;

            Assert.That(await Collector.RunAsync(_options, _server), Is.EqualTo(ExitCodes.RequestLimit));
            Assert.That(await Collector.RunAsync(_options, _server), Is.EqualTo(ExitCodes.Done));

            // Each run keeps the chunks it completed: the second run requests only the stopped chunk again and the rest.
            Assert.That(_server.Requests, Has.Count.EqualTo(replays.Length + 5));
            // All replays show the same two teams: merged over the whole tournament, not per chunk.
            var teams = (JArray)JObject.Parse(File.ReadAllText(Out("output.json")))["3700001"]!["Teams"]!;
            Assert.That(teams, Has.Count.EqualTo(2));
            Assert.That(teams.Select(team => team["Replays"]!.Count()), Is.All.EqualTo(replays.Length));
        }

        [Test]
        public async Task ARefusingServerStopsTheRun()
        {
            WritePreviousOutputs();
            ParserOutput.Write(_options.MatchesPath!, ParserOutput.Users(
                ParserOutput.Match("3700001", "Test Open - Round 1", "Alice", "Bob", Replay, GoneReplay, OtherGoneReplay, Replay + "2")
            ));
            foreach (var replay in new[] { Replay, GoneReplay, OtherGoneReplay, Replay + "2" })
            {
                _server.Answer(replay + ".json", HttpStatusCode.Forbidden);
            }

            Assert.That(await Collector.RunAsync(_options, _server), Is.EqualTo(ExitCodes.Stopped));

            AssertPreviousOutputsUntouched();
            Assert.That(_server.Requests, Has.Count.EqualTo(ReplayRequestGuard.RefusalsToStop));
        }

        [Test]
        public async Task MatchesWithoutTournamentsLeaveTheOutputsAlone()
        {
            WritePreviousOutputs();
            File.WriteAllText(_options.MatchesPath!, "{}");

            Assert.That(await Collector.RunAsync(_options, _server), Is.EqualTo(ExitCodes.Error));

            AssertPreviousOutputsUntouched();
            Assert.That(_server.Requests, Is.Empty);
        }

        [Test]
        public async Task AMissingMatchesFileWithoutFallbackIsAnError()
        {
            WritePreviousOutputs();
            File.Delete(_options.MatchesPath!);

            Assert.That(await Collector.RunAsync(_options, _server), Is.EqualTo(ExitCodes.Error));

            AssertPreviousOutputsUntouched();
        }

        [Test]
        public async Task DoesNotWriteOverTheMatchesItReads()
        {
            _options.OutputDirectory = _dir.File("parser");

            Assert.That(await Collector.RunAsync(_options, _server), Is.EqualTo(ExitCodes.Usage));

            Assert.That(_server.Requests, Is.Empty);
        }
    }
}
