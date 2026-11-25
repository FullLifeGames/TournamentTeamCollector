using NUnit.Framework;

namespace TournamentTeamCollector.Tests
{
    public class TournamentGroupingTests
    {
        private const string Replay = "https://replay.pokemonshowdown.com/gen9ou-";

        private TempDirectory _dir = null!;

        [SetUp]
        public void SetUp() => _dir = new TempDirectory();

        [TearDown]
        public void TearDown() => _dir.Dispose();

        /// <summary>Categories A to D, D the fallback; two name rules, the first that matches wins, and two forum rules.</summary>
        private CategoryRules Rules()
        {
            File.WriteAllText(_dir.File("categories.json"), """
                {
                  "categories": ["A", "B", "C", "D"],
                  "fallback": "D",
                  "names": [
                    { "category": "B", "pattern": "premier", "except": "tours plaza premier" },
                    { "category": "C", "pattern": "premier|ladder" }
                  ],
                  "forums": [
                    { "category": "A", "forums": ["tournaments.34"] },
                    { "category": "C", "forums": ["smogon-premier-league.130"] }
                  ]
                }
                """);
            return CategoryRules.Load(_dir.File("categories.json"));
        }

        /// <summary>tournaments.34 holds smogon-premier-league.130, which holds spl-replays.999 (without a rule).</summary>
        private static ForumTree Forums(params (string Thread, string Forum)[] threads) => ForumTree.Create(
            new Dictionary<string, string?>()
            {
                ["tournaments.34"] = null,
                ["smogon-premier-league.130"] = "tournaments.34",
                ["spl-replays.999"] = "smogon-premier-league.130",
                ["off-topic.1"] = null,
            },
            threads.ToDictionary(entry => entry.Thread, entry => entry.Forum));

        private static (IReadOnlyList<Tournament> Tournaments, int[] TournamentOfThread) Derive(
            MatchTable table, ForumTree? forums, CategoryRules rules, Overrides? overrides = null) =>
            TournamentGrouping.Derive(table, forums, rules, overrides ?? new Overrides());

        [Test]
        public void ThreadsWithTheSameTournamentNameAreOneTournament()
        {
            var table = ParserOutput.Table(
                ParserOutput.Match("3576857", "UU Open V - Round 1", "a", "b", Replay + "1").Posted(2016, 7, 11),
                ParserOutput.Match("3578000", "The UU Open V - Round 3", "a", "c", Replay + "3").Posted(2016, 8, 1),
                ParserOutput.Match("3577500", "UU open V: Round 2 [Read post #2]", "a", "d", Replay + "2").Posted(2016, 7, 20),
                ParserOutput.Match("3577500", "UU open V: Round 2 [Read post #2]", "e", "f").Posted(2016, 7, 21)
            );

            var (tournaments, tournamentOfThread) = Derive(table, null, Rules());

            // "The UU Open V" is spelt differently: the prototype keeps it apart too.
            Assert.That(tournaments.Select(t => t.Name), Is.EqualTo(new[] { "The UU Open V", "UU Open V" }));
            var open = tournaments.Single(t => t.Name == "UU Open V");
            Assert.That(open.Id, Is.EqualTo("uu-open-v"));
            Assert.That(open.Year, Is.EqualTo(2016));
            Assert.That(open.Threads.Select(entry => (table.Threads[entry.Thread].Id, entry.Round)),
                Is.EqualTo(new[] { ("3576857", "Round 1"), ("3577500", "Round 2") }), "oldest first");
            Assert.That(table.Threads[open.Threads[1].Thread].Matches, Is.EqualTo(2), "also the match without replay");
            Assert.That(tournamentOfThread[open.Threads[1].Thread], Is.EqualTo(tournaments.ToList().IndexOf(open)));
        }

        [Test]
        public void TheSameNameMonthsApartIsAnotherTournamentAndGetsTheYearItStarted()
        {
            var first = new[]
            {
                ParserOutput.Match("1", "PU Open - Round 1", "a", "b", Replay + "1").Posted(2016, 12, 20),
                ParserOutput.Match("2", "PU Open - Finals", "a", "b", Replay + "2").Posted(2017, 2, 1),
                ParserOutput.Match("3", "PU Open - Round 1", "a", "b", Replay + "3").Posted(2017, 9, 1),
            };
            // Named like the first with its year: a newer Tournament does not take the id of an older one.
            var later = ParserOutput.Match("4", "PU Open 2016 - Round 1", "a", "b", Replay + "4").Posted(2018, 1, 1);

            var before = Derive(ParserOutput.Table(first), null, Rules()).Tournaments;
            var (tournaments, _) = Derive(ParserOutput.Table([.. first, later]), null, Rules());

            Assert.That(tournaments.Select(t => (t.Id, t.Name, t.Year)), Is.EqualTo(new[]
            {
                ("pu-open-2016-4", "PU Open 2016", (int?)2018),
                ("pu-open-2017", "PU Open (2017)", 2017),
                ("pu-open-2016", "PU Open (2016)", 2017),
            }), "newest first; the year in the name is the year the Tournament started");
            Assert.That(tournaments[2].Threads.Select(entry => entry.Round), Is.EqualTo(new[] { "Round 1", "Finals" }));
            Assert.That(before.Select(t => t.Id), Is.EqualTo(new[] { "pu-open-2017", "pu-open-2016" }));
        }

        [Test]
        public void TheCrawlTimeOfAPostWithoutDateIsNoPostDate()
        {
            // The parser's output of 2026-11-17 dates a post it could not read with the time of the crawl.
            var late = ParserOutput.Match("3619250", "PU Fall Seasonal: Round 1 (POST / PM REPLAYS)", "a", "c", Replay + "2");
            late.PostDate = Newtonsoft.Json.JsonConvert.DeserializeObject<DateTime>("\"2026-11-17T01:39:06.2760292+01:00\"");
            var table = ParserOutput.Table(
                ParserOutput.Match("3619250", "PU Fall Seasonal: Round 1 (POST / PM REPLAYS)", "a", "b", Replay + "1").Posted(2017, 10, 29),
                late,
                ParserOutput.Match("3626529", "PU Fall Seasonal Finals (won by amber lamps)", "a", "b", Replay + "3").Posted(2018, 1, 21),
                ParserOutput.Match("3642902", "PU Fall Seasonal: Round 1", "a", "b", Replay + "4").Posted(2018, 10, 21)
            );

            var (tournaments, _) = Derive(table, null, Rules());

            Assert.That(tournaments.Select(t => (t.Name, t.Year, t.Threads.Count)),
                Is.EqualTo(new[] { ("PU Fall Seasonal (2018)", (int?)2018, 1), ("PU Fall Seasonal (2017)", 2018, 2) }));
            Assert.That(table.Matches.Select(match => match.Day), Has.Exactly(1).EqualTo(-1));
        }

        [Test]
        public void AccentsAndPunctuationMakeNoOtherTournament()
        {
            var table = ParserOutput.Table(
                ParserOutput.Match("10", "Pokémon Cup - Round 1", "a", "b", Replay + "1").Posted(2020, 1, 1),
                ParserOutput.Match("20", "Pokemon-Cup Finals", "a", "b", Replay + "2").Posted(2020, 3, 1),
                ParserOutput.Match("30", "Pokemon Cup! - Round 2", "a", "b", Replay + "3").Posted(2020, 2, 1),
                ParserOutput.Match("40", "Pokemon Cups - Round 1", "a", "b", Replay + "4").Posted(2020, 1, 1)
            );

            var (tournaments, _) = Derive(table, null, Rules());

            Assert.That(tournaments.Select(t => (t.Id, t.Name, t.Threads.Count)),
                Is.EquivalentTo(new[] { ("pokemon-cup", "Pokémon Cup", 3), ("pokemon-cups", "Pokemon Cups", 1) }), "named after the oldest thread");
        }

        [Test]
        public void IdsAreUniqueAndSafeAsFileNames()
        {
            var table = ParserOutput.Table(
                ParserOutput.Match("1", "宝可梦大赛", "a", "b", Replay + "1").Posted(2020, 1, 1),
                ParserOutput.Match("2", "ポケモン大会", "a", "b", Replay + "2").Posted(2020, 1, 2),
                ParserOutput.Match("3", "Con - Round 1", "a", "b", Replay + "3").Posted(2020, 1, 3),
                ParserOutput.Match("4", "SPL X - Week 1", "a", "b", Replay + "4").Posted(2020, 1, 4),
                ParserOutput.Match("5", "S.P.L. X - Week 2", "a", "b", Replay + "5").Posted(2020, 1, 11)
            );

            var (tournaments, _) = Derive(table, null, Rules());

            Assert.That(tournaments.Select(t => t.Id), Is.EquivalentTo(new[] { "tournament", "tournament-2", "con-3", "spl-x" }));
            Assert.That(tournaments.Select(t => t.Id), Has.All.Matches("^[a-z0-9]+(?:-[a-z0-9]+)*$"));
        }

        [Test]
        public void AnOverrideWinsOverANameRuleOverTheNearestForumRuleOverTheFallback()
        {
            var table = ParserOutput.Table(
                // Both name rules match: the first wins, also over the forum.
                ParserOutput.Match("1", "Smogon Premier League XVI - Week 1", "a", "b").Posted(2025, 1, 1),
                // No name rule: the forum's own rule, not the one of tournaments.34 above it.
                ParserOutput.Match("2", "Secret Cup - Round 1", "a", "b").Posted(2025, 1, 1),
                // spl-replays.999 has no rule: the nearest forum above it that has one.
                ParserOutput.Match("3", "Mystery Tour - Round 1", "a", "b").Posted(2025, 1, 1),
                // The second name rule; the first does not match because of its exception.
                ParserOutput.Match("4", "Tours Plaza Premiere IV : Semi Finals", "a", "b").Posted(2025, 1, 1),
                // A forum without rule, a thread the forum file does not know.
                ParserOutput.Match("5", "Off Cup - Round 1", "a", "b").Posted(2025, 1, 1),
                ParserOutput.Match("6", "Lost Cup - Round 1", "a", "b").Posted(2025, 1, 1),
                ParserOutput.Match("7", "Fixed Cup - Round 1", "a", "b").Posted(2025, 1, 1),
                ParserOutput.Match("8", "Plain Cup - Round 1", "a", "b").Posted(2025, 1, 1)
            );
            var forums = Forums(("1", "tournaments.34"), ("2", "smogon-premier-league.130"), ("3", "spl-replays.999"),
                ("4", "tournaments.34"), ("5", "off-topic.1"), ("7", "tournaments.34"), ("8", "tournaments.34"));
            var overrides = new Overrides() { Tournaments = { ["fixed-cup"] = new() { Category = "b" } } };
            var rules = Rules();

            var (tournaments, _) = Derive(table, forums, rules, overrides);

            string CategoryOf(string name) => rules.Categories[tournaments.Single(t => t.Name == name).Category];
            Assert.That(CategoryOf("Smogon Premier League XVI"), Is.EqualTo("B"));
            Assert.That(CategoryOf("Secret Cup"), Is.EqualTo("C"));
            Assert.That(CategoryOf("Mystery Tour"), Is.EqualTo("C"));
            Assert.That(CategoryOf("Tours Plaza Premiere IV"), Is.EqualTo("C"));
            Assert.That(CategoryOf("Plain Cup"), Is.EqualTo("A"));
            Assert.That(CategoryOf("Off Cup"), Is.EqualTo("D"));
            Assert.That(CategoryOf("Lost Cup"), Is.EqualTo("D"));
            Assert.That(CategoryOf("Fixed Cup"), Is.EqualTo("B"));
            Assert.That(tournaments.Single(t => t.Name == "Mystery Tour").Forum, Is.EqualTo("spl-replays.999"));
            Assert.That(tournaments.Single(t => t.Name == "Lost Cup").Forum, Is.Null);
        }

        [Test]
        public void WithoutForumsOnlyTheNameRulesAndTheFallbackDecide()
        {
            var table = ParserOutput.Table(
                ParserOutput.Match("1", "Smogon Premier League XVI - Week 1", "a", "b").Posted(2025, 1, 1),
                ParserOutput.Match("2", "Mystery Tour - Round 1", "a", "b").Posted(2025, 1, 1)
            );
            var rules = Rules();

            var (tournaments, _) = Derive(table, null, rules);

            Assert.That(tournaments.OrderBy(t => t.Name).Select(t => (rules.Categories[t.Category], t.Forum)),
                Is.EqualTo(new[] { ("D", (string?)null), ("B", null) }));
        }

        [Test]
        public void TheMainForumIsWhereMostThreadsSit()
        {
            var table = ParserOutput.Table(
                ParserOutput.Match("1", "Mystery Tour - Round 1", "a", "b").Posted(2025, 1, 1),
                ParserOutput.Match("2", "Mystery Tour - Round 2", "a", "b").Posted(2025, 1, 8),
                ParserOutput.Match("3", "Mystery Tour - Finals", "a", "b").Posted(2025, 1, 15)
            );
            var forums = Forums(("1", "tournaments.34"), ("2", "spl-replays.999"), ("3", "spl-replays.999"));

            var tournament = Derive(table, forums, Rules()).Tournaments.Single();

            Assert.That(tournament.Forum, Is.EqualTo("spl-replays.999"));
        }

        [Test]
        public void OverridesMoveAThreadAndRenameATournament()
        {
            var table = ParserOutput.Table(
                ParserOutput.Match("1", "Mystery Tour - Round 1", "a", "b").Posted(2025, 1, 1),
                ParserOutput.Match("2", "The Mystery Tour, second week", "a", "b").Posted(2025, 1, 8),
                ParserOutput.Match("3", "Mystery Tour - Finals", "a", "b").Posted(2025, 1, 15)
            );
            var overrides = new Overrides()
            {
                Threads = { ["2"] = new() { Tournament = "Mystery Tour", Round = "Round 2" } },
                Tournaments = { ["mystery-tour"] = new() { Name = "Mystery Tour I" } },
            };

            var tournament = Derive(table, null, Rules(), overrides).Tournaments.Single();

            Assert.That((tournament.Id, tournament.Name), Is.EqualTo(("mystery-tour-i", "Mystery Tour I")));
            Assert.That(tournament.Threads.Select(entry => entry.Round), Is.EqualTo(new[] { "Round 1", "Round 2", "Finals" }));
        }

        [Test]
        public void AnOverrideThatNamesAnUnknownCategoryIsAnError()
        {
            var table = ParserOutput.Table(ParserOutput.Match("1", "Mystery Tour - Round 1", "a", "b").Posted(2025, 1, 1));
            var overrides = new Overrides() { Tournaments = { ["mystery-tour"] = new() { Category = "E" } } };

            Assert.That(() => Derive(table, null, Rules(), overrides), Throws.InstanceOf<InvalidDataException>());
        }

        [TestCase("""{ "categories": ["A"], "fallback": "B" }""")]
        [TestCase("""{ "categories": ["A", "a"], "fallback": "A" }""")]
        [TestCase("""{ "categories": ["A"], "fallback": "A", "names": [{ "category": "A", "pattern": "(" }] }""")]
        [TestCase("""{ "categories": ["A"], "fallback": "A", "forums": [{ "category": "A", "forums": ["x.1"] }, { "category": "A", "forums": ["x.1"] }] }""")]
        [TestCase("""{ "categories": ["A"], "fallback": "A", "colours": [] }""")]
        public void ACategoryRulesFileWithAnErrorIsRejected(string content)
        {
            File.WriteAllText(_dir.File("categories.json"), content);

            Assert.That(() => CategoryRules.Load(_dir.File("categories.json")), Throws.InstanceOf<InvalidDataException>());
        }

        [Test]
        public void TheShippedRulesAndOverridesAreValid()
        {
            var options = new RunOptions();

            var rules = CategoryRules.Load(options.CategoriesPath);
            var overrides = Overrides.Load(options.OverridesPath);

            Assert.That(rules.Categories, Has.Count.GreaterThan(1));
            Assert.That(rules.Categories, Has.Member(rules.Categories[rules.Fallback]));
            Assert.That(overrides.Threads, Is.Empty);
            Assert.That(overrides.Tournaments, Is.Empty);
            Assert.That(overrides.Players, Is.Empty);
            Assert.That(overrides.Alts, Is.Empty);
        }

        [Test]
        public void ReadsTheForumFileOfTheParser()
        {
            File.WriteAllText(_dir.File("forums.json"), """
                {
                  "forums": {
                    "tournaments.34": { "u": "https://www.smogon.com/forums/forums/tournaments.34/", "p": null, "n": "Tournaments" },
                    "smogon-premier-league.130": { "u": "https://www.smogon.com/forums/forums/smogon-premier-league.130/", "p": "tournaments.34", "n": null }
                  },
                  "threads": { "3593744": "smogon-premier-league.130" }
                }
                """);

            var forums = ForumTree.Load(_dir.File("forums.json"));

            Assert.That(forums.ForumOf("3593744"), Is.EqualTo("smogon-premier-league.130"));
            Assert.That(forums.ForumOf("1"), Is.Null);
            Assert.That(forums.SelfAndAncestors("smogon-premier-league.130"), Is.EqualTo(new[] { "smogon-premier-league.130", "tournaments.34" }));
        }
    }
}
