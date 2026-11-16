using NUnit.Framework;

namespace TournamentTeamCollector.Tests
{
    public class TournamentMatchesTests
    {
        [Test]
        public void ReadsTheOutputOfTournamentParser()
        {
            using var dir = new TempDirectory();
            ParserOutput.Write(dir.File("output.json"), ParserOutput.Users(
                ParserOutput.Match("100", "UU Open - Round 1", "Alice", "Bob", "https://replay.pokemonshowdown.com/gen9uu-1", "https://replay.pokemonshowdown.com/gen9uu-2"),
                ParserOutput.Match("100", "UU Open - Round 1", "Carol", "Dave", "https://replay.pokemonshowdown.com/gen9uu-3"),
                ParserOutput.Match("200", "OU Cup - Finals", "Alice", "Carol", "https://replay.pokemonshowdown.com/gen9ou-1")
            ));

            var users = TournamentMatches.ReadParserOutput(dir.File("output.json")).ToDictionary(user => user.Name!);

            Assert.That(users.Keys, Is.EquivalentTo(new[] { "Alice", "Bob", "Carol", "Dave" }));
            Assert.That(users["Alice"].Matches, Has.Count.EqualTo(2));
            var match = users["Bob"].Matches.Single();
            Assert.That(match.Thread?.Id, Is.EqualTo("100"));
            Assert.That(match.Thread?.Name, Is.EqualTo("UU Open - Round 1"));
            Assert.That(match.Replays, Is.EqualTo(new[] { "https://replay.pokemonshowdown.com/gen9uu-1", "https://replay.pokemonshowdown.com/gen9uu-2" }));
        }

        [TestCase("")]
        [TestCase("[]")]
        [TestCase("{\"Alice\":{\"n\":\"Alice\",\"m\":[]}")]
        public void AnOutputThatIsNoCompleteObjectOfUsersIsAnError(string content)
        {
            using var dir = new TempDirectory();
            File.WriteAllText(dir.File("output.json"), content);

            Assert.That(() => TournamentMatches.ReadParserOutput(dir.File("output.json")).ToList(),
                Throws.InstanceOf<InvalidDataException>().Or.InstanceOf<Newtonsoft.Json.JsonException>());
        }

        [Test]
        public void GroupsTheDistinctReplaysOfEachTournament()
        {
            // Both players carry the same match: its replays count once.
            var tournaments = TournamentMatches.GroupByThread(ParserOutput.Users(
                ParserOutput.Match("100", "UU Open - Round 1", "Alice", "Bob", "https://replay.pokemonshowdown.com/gen9uu-1", "https://replay.pokemonshowdown.com/gen9uu-2"),
                ParserOutput.Match("100", "UU Open - Round 1", "Carol", "Dave", "https://replay.pokemonshowdown.com/gen9uu-2", "https://replay.pokemonshowdown.com/gen9uu-3"),
                ParserOutput.Match("200", "OU Cup - Finals", "Alice", "Carol")
            ).Values);

            Assert.That(tournaments.Keys, Is.EquivalentTo(new[] { "100", "200" }));
            Assert.That(tournaments["100"].Replays, Is.EquivalentTo(new[]
            {
                "https://replay.pokemonshowdown.com/gen9uu-1",
                "https://replay.pokemonshowdown.com/gen9uu-2",
                "https://replay.pokemonshowdown.com/gen9uu-3",
            }));
            Assert.That(tournaments["100"].Name, Is.EqualTo("UU Open - Round 1"));
            Assert.That(tournaments["100"].Locked, Is.True);
            Assert.That(tournaments["200"].Replays, Is.Empty);
        }

        [Test]
        public void SkipsMatchesWithoutThreadAndLinksThatAreNoUrl()
        {
            var withoutThread = ParserOutput.Match("300", "x", "Eve", "Frank", "https://replay.pokemonshowdown.com/gen9ou-9");
            withoutThread.Thread = null;

            var tournaments = TournamentMatches.GroupByThread(ParserOutput.Users(
                withoutThread,
                ParserOutput.Match("100", "UU Open - Round 1", "Alice", "Bob", "replay.pokemonshowdown.com/gen9uu-1", "", "https://replay.pokemonshowdown.com/gen9uu-2")
            ).Values);

            Assert.That(tournaments.Keys, Is.EqualTo(new[] { "100" }));
            Assert.That(tournaments["100"].Replays, Is.EqualTo(new[] { "https://replay.pokemonshowdown.com/gen9uu-2" }));
        }
    }
}
