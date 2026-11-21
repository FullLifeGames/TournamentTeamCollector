using Newtonsoft.Json.Linq;
using NUnit.Framework;
using ShowdownReplayScouter.Core.Data;

namespace TournamentTeamCollector.Tests
{
    /// <summary>Games, Alts and the match files, from scouting results as the scouter returns them.</summary>
    public class MatchGamesTests
    {
        private const string Server = "https://replay.pokemonshowdown.com/";

        /// <summary>The replay as the scouter returns it in the Team of one side.</summary>
        private static Replay Replay(string id, string side, string alt1, string alt2, int uploadTime, string? winner = null) => new()
        {
            Id = id,
            Link = new Uri(Server + id),
            FormatId = ReplayLinks.FormatOf(id)!,
            Format = ReplayLinks.FormatOf(id) == "gen8ou" ? "[Gen 8] OU" : "[Gen 9] OU",
            P1 = alt1,
            P2 = alt2,
            Players = [alt1, alt2],
            UploadTime = uploadTime,
            Views = 12,
            Winner = winner!,
            PlayerInfo = new PlayerInfo() { PlayerValue = side, PlayerName = side == "p1" ? alt1 : alt2 },
        };

        private static Team Team(string lead, params Replay[] replays) => new()
        {
            Pokemon =
            [
                new Pokemon() { Name = lead, Lead = true, Item = "Leftovers", Ability = "Pressure", TeraType = "Fairy", Moves = ["Protect", "Toxic"] },
                new Pokemon() { Name = "Urshifu-Rapid-Strike", FormName = "Urshifu", Moves = ["Surging Strikes"] },
            ],
            Replays = [.. replays],
        };

        /// <summary>The Teams of both sides of each replay: (id, alt of side 1, alt of side 2, upload time, winner).</summary>
        private static List<Team> Teams(params (string Id, string Alt1, string Alt2, int UploadTime, string? Winner)[] replays) =>
            replays.SelectMany(replay => new[]
            {
                Team("Kingambit", Replay(replay.Id, "p1", replay.Alt1, replay.Alt2, replay.UploadTime, replay.Winner)),
                Team("Great Tusk", Replay(replay.Id, "p2", replay.Alt1, replay.Alt2, replay.UploadTime, replay.Winner)),
            }).ToList();

        private static int MatchOf(MatchTable table, string threadId, string first)
        {
            table.TryGetThread(threadId, out var thread);
            return Enumerable.Range(0, table.Matches.Count)
                .Single(match => table.Matches[match].Thread == thread && table.PlayerKeys[table.Matches[match].A] == first);
        }

        [Test]
        public void AReplayInARoundThreadAndInAReplayCollectionIsAGameOfTheRoundThread()
        {
            var table = ParserOutput.Table(
                ParserOutput.Match("10", "SPL XVI - Replays", "mada", "setsu", Server + "gen9ou-101?p2").Posted(2025, 2, 1),
                ParserOutput.Match("11", "SPL XVI - Week 1", "mada", "setsu", Server + "gen9ou-101").Posted(2025, 1, 25)
            );
            var games = new ScoutedGames(table);

            // The collection is scouted first: the Game waits for its round thread.
            games.AddThread("10", Teams(("gen9ou-101", "Mada", "setsu", 1000, "Mada")));
            Assert.That(games.Games, Is.Empty);
            games.AddThread("11", Teams(("gen9ou-101", "Mada", "setsu", 1000, "Mada")));

            var game = games.Games.Single();
            Assert.That(game.Match, Is.EqualTo(MatchOf(table, "11", "mada")));
            Assert.That((game.Winner, game.Team1, game.Team2), Is.EqualTo(((byte)1, 0, 1)));
            Assert.That(games.Formats[game.Format], Is.EqualTo("gen9ou"));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void AGameWaitingForARoundThreadThatLacksItStaysWithTheCollection(bool roundThreadScouted)
        {
            var table = ParserOutput.Table(
                ParserOutput.Match("10", "SPL XVI - Replays", "mada", "setsu", Server + "gen9ou-101").Posted(2025, 2, 1),
                ParserOutput.Match("11", "SPL XVI - Week 1", "mada", "setsu", Server + "gen9ou-101?p2").Posted(2025, 1, 25)
            );
            var games = new ScoutedGames(table);

            games.AddThread("10", Teams(("gen9ou-101", "Mada", "setsu", 1000, "Mada")));
            if (roundThreadScouted)
            {
                // Its link there named a page the server does not have.
                games.AddThread("11", []);
            }
            games.Complete();

            var game = games.Games.Single();
            Assert.That(game.Match, Is.EqualTo(MatchOf(table, "10", "mada")));
            Assert.That((game.Team1, game.Team2, games.Teams.Count), Is.EqualTo((0, 1, 2)));
        }

        [Test]
        public void AReplayLinkedByTwoMatchesIsAGameOfTheMatchWhosePlayersPlayedIt()
        {
            var table = ParserOutput.Table(
                ParserOutput.Match("20", "SPL XVI - Week 2", "alpha", "beta", Server + "gen9ou-201").Posted(2025, 2, 1),
                ParserOutput.Match("20", "SPL XVI - Week 2", "gamma", "delta", Server + "gen9ou-201", Server + "gen9ou-202").Posted(2025, 2, 2)
            );
            var games = new ScoutedGames(table);

            games.AddThread("20", Teams(("gen9ou-201", "Gamma", "delta", 1000, null), ("gen9ou-202", "Gamma", "delta", 2000, null)));

            Assert.That(games.Games.Select(game => game.Match), Is.All.EqualTo(MatchOf(table, "20", "gamma")));
            Assert.That(games.Games, Has.Count.EqualTo(2));
        }

        [Test]
        public void AnAltIsThePlayerItPlaysForAcrossTheGames()
        {
            var table = ParserOutput.Table(
                ParserOutput.Match("1", "SPL XVI - Week 1", "mada", "setsu", Server + "gen9ou-101").Posted(2025, 1, 1),
                ParserOutput.Match("2", "SPL XVI - Week 2", "mada", "kor", Server + "gen9ou-102").Posted(2025, 1, 8),
                ParserOutput.Match("2", "SPL XVI - Week 2", "alpha", "beta", Server + "gen9ou-103").Posted(2025, 1, 8),
                ParserOutput.Match("3", "SPL XVI - Week 3", "setsu", "mada", Server + "gen9ou-104").Posted(2025, 1, 15)
            );
            var games = new ScoutedGames(table);
            games.AddThread("1", Teams(("gen9ou-101", "Mada", "setsu123", 1000, "Mada")));
            games.AddThread("2", Teams(("gen9ou-102", "mada (smog)", "Kor", 2000, "Kor"), ("gen9ou-103", "x1", "x2", 2100, "x2")));
            games.AddThread("3", Teams(("gen9ou-104", "Mada", "setsu123", 3000, "setsu123")));

            var alts = AltPlayers.Assign(games, new Overrides());

            string? PlayerOf(string alt) => alts.PlayerOf(games.Alts.Find(alt)) is var player and >= 0 ? table.PlayerKeys[player] : null;
            // Its own name, the other side of a Game whose other Alt is a user's name, and a name voted for over the Games.
            Assert.That(PlayerOf("Mada"), Is.EqualTo("mada"));
            Assert.That(PlayerOf("mada (smog)"), Is.EqualTo("mada"));
            Assert.That(PlayerOf("setsu123"), Is.EqualTo("setsu"));
            Assert.That(PlayerOf("Kor"), Is.EqualTo("kor"));
            // Nothing tells which of alpha and beta x1 is.
            Assert.That(PlayerOf("x1"), Is.Null);
            Assert.That(alts.AltsOfPlayers()[table.FindPlayer("mada")!.Value], Is.EqualTo(new[] { "Mada", "mada (smog)" }));

            byte SideOfA(string replay) => games.Games
                .Where(game => table.Threads[table.Matches[game.Match].Thread].Links[game.Link].EndsWith(replay))
                .Select(game => alts.SideOfA(game, table.Matches[game.Match])).Single();
            Assert.That(SideOfA("gen9ou-101"), Is.EqualTo(1));
            Assert.That(SideOfA("gen9ou-102"), Is.EqualTo(1));
            Assert.That(SideOfA("gen9ou-103"), Is.EqualTo(0));
            Assert.That(SideOfA("gen9ou-104"), Is.EqualTo(2), "setsu is Player a and played side 2");

            var corrected = AltPlayers.Assign(games, new Overrides() { Alts = { ["X1"] = "Alpha" } });
            Assert.That(corrected.PlayerOf(games.Alts.Find("x1")), Is.EqualTo(table.FindPlayer("alpha")));
        }

        [Test]
        public void TwoSmogonUsersOfOnePlayerAreOnePlayer()
        {
            var table = ParserOutput.Table(new MatchTable(new Overrides() { Players = { ["Mada Alt"] = "mada" } }.PlayerAliases()),
                ParserOutput.Match("1", "SPL XVI - Week 1", "mada", "setsu", Server + "gen9ou-101").Posted(2025, 1, 1),
                ParserOutput.Match("2", "SPL XVI - Week 2", "mada alt", "kor", Server + "gen9ou-102").Posted(2025, 1, 8).WonBy("mada alt")
            );

            Assert.That(table.PlayerKeys, Is.EquivalentTo(new[] { "mada", "setsu", "kor" }));
            var match = table.Matches[MatchOf(table, "2", "mada")];
            Assert.That(match.Winner, Is.EqualTo(1));
        }

        [Test]
        public void AFormatFileHoldsAMatchWithAllItsGamesAlsoThoseInOtherFormats()
        {
            using var dir = new TempDirectory();
            var table = ParserOutput.Table(
                ParserOutput.Match("1", "Gen Cup - Finals", "mada", "setsu", Server + "gen9ou-102", Server + "gen8ou-101", Server + "gen9ou-103").Posted(2025, 1, 1).WonBy("setsu"),
                ParserOutput.Match("1", "Gen Cup - Finals", "kor", "alpha", Server + "gen9ou-104").Posted(2025, 1, 2)
            );
            File.WriteAllText(dir.File("categories.json"), """{ "categories": ["Other"], "fallback": "Other" }""");
            var rules = CategoryRules.Load(dir.File("categories.json"));
            var (tournaments, tournamentOfThread) = TournamentGrouping.Derive(table, null, rules, new Overrides());
            var games = new ScoutedGames(table);
            games.AddThread("1", Teams(
                ("gen9ou-102", "Mada", "Setsu", 2000, "Setsu"), ("gen8ou-101", "Mada", "Setsu", 1000, "Mada"),
                ("gen9ou-103", "Mada", "Setsu", 3000, "Setsu"), ("gen9ou-104", "Kor", "Alpha", 4000, "Kor")));

            MatchFiles.Write(dir.File("Matches"), games, tournaments, tournamentOfThread, rules, new Overrides(), new DateTime(2026, 11, 22, 3, 12, 45, DateTimeKind.Utc));

            JObject Read(string path) => JsonFile.Read(dir.File(Path.Combine("Matches", path)));
            var index = Read("index.json");
            Assert.That((string?)index["generated"], Is.EqualTo("2026-11-22T03:12:45Z"));
            Assert.That(index["formats"]!.Select(f => ((string?)f["id"], (string?)f["name"], (int)f["games"]!, (int)f["matches"]!, (int)f["lastDay"]!)),
                Is.EqualTo(new[] { ("gen9ou", "[Gen 9] OU", 3, 2, 0), ("gen8ou", "[Gen 8] OU", 1, 1, 0) }));
            foreach (var format in new[] { "gen9ou", "gen8ou" })
            {
                var file = Read($"Formats/{format}.json");
                Assert.That((string?)file["formats"]![0], Is.EqualTo(format), "the file's Format first");
                var finals = file["matches"]!.Single(match => (string?)file["players"]![(int)match[1]!]![0] == "mada");
                var replayIds = finals[5]!.Select(game => (string?)file["games"]![(int)game]![0]);
                Assert.That(replayIds, Is.EqualTo(new[] { "gen8ou-101", "gen9ou-102", "gen9ou-103" }), "in the order they were played");
                Assert.That((int)finals[3]!, Is.EqualTo(2), "setsu won the Match");
            }
            Assert.That(Read("Formats/gen8ou.json")["matches"], Has.Count.EqualTo(1));
            Assert.That(Read("Formats/gen9ou.json")["matches"], Has.Count.EqualTo(2));
        }
    }
}
