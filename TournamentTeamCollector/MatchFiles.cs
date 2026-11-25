using Newtonsoft.Json;
using System.Globalization;

namespace TournamentTeamCollector
{
    /// <summary>
    /// Writes Matches/, the files of the Tournament Teams search view: index.json (the Formats), tournaments.json (every
    /// Tournament with its threads), players.json (every Player with Games), and one file per Format and per Tournament with
    /// its Matches, Games and Teams. The web app only displays them, so everything it shows is derived here. The shapes are
    /// version 1 of the contract with the app; each file carries <see cref="Version"/>.
    /// <para>
    /// The files are JSON without indentation and with tables: a file names each Player, species or move once and refers to
    /// it by its position. A Format file holds every Match with a Game in the Format with all its Games, also those in other
    /// Formats (a best-of-three across generations is one Match).
    /// </para>
    /// </summary>
    public static class MatchFiles
    {
        public const string Folder = "Matches";
        public const string FormatsFolder = "Formats";
        public const string TournamentsFolder = "Tournaments";
        public const int Version = 1;

        /// <summary>A Match with its Games, in the order the files list them.</summary>
        private readonly record struct MatchGames(int Match, int Start, int Count);

        /// <summary>Writes all files of the folder into <paramref name="directory"/>, which must be empty or not exist.</summary>
        public static void Write(
            string directory, ScoutedGames games, IReadOnlyList<Tournament> tournaments, int[] tournamentOfThread, CategoryRules rules,
            Overrides overrides, DateTime generated)
        {
            var table = games.Matches;
            var alts = AltPlayers.Assign(games, overrides);

            // Matches by day, their Games by upload time.
            var order = Enumerable.Range(0, games.Games.Count)
                .OrderBy(game => MatchOrder(table, games.Games[game].Match))
                .ThenBy(game => games.Games[game].UploadTime)
                .ThenBy(game => game)
                .ToArray();
            var groups = new List<MatchGames>();
            for (var start = 0; start < order.Length;)
            {
                var match = games.Games[order[start]].Match;
                var end = start;
                while (end < order.Length && games.Games[order[end]].Match == match)
                {
                    end++;
                }
                groups.Add(new MatchGames(match, start, end - start));
                start = end;
            }

            // What the index, tournaments.json and players.json count.
            var formatCount = games.Formats.Count;
            var formatGames = new int[formatCount];
            var formatMatches = new int[formatCount];
            var formatLastDay = Enumerable.Repeat(-1, formatCount).ToArray();
            var groupsOfFormat = Enumerable.Range(0, formatCount).Select(_ => new List<int>()).ToArray();
            var groupsOfTournament = tournaments.Select(_ => new List<int>()).ToArray();
            var matchesWithGames = new int[table.Threads.Count];
            var tournamentFormats = tournaments.Select(_ => new Dictionary<int, int>()).ToArray();
            var playerFormats = new Dictionary<int, Dictionary<int, int>>();
            for (var g = 0; g < groups.Count; g++)
            {
                var match = table.Matches[groups[g].Match];
                var tournament = tournamentOfThread[match.Thread];
                matchesWithGames[match.Thread]++;
                groupsOfTournament[tournament].Add(g);
                var seenFormats = new HashSet<int>();
                foreach (var game in Games(games, order, groups[g]))
                {
                    formatGames[game.Format]++;
                    formatLastDay[game.Format] = Math.Max(formatLastDay[game.Format], game.UploadTime / 86400);
                    if (seenFormats.Add(game.Format))
                    {
                        formatMatches[game.Format]++;
                        groupsOfFormat[game.Format].Add(g);
                    }
                    Count(tournamentFormats[tournament], game.Format);
                    foreach (var player in new[] { match.A, match.B }.Where(player => player >= 0).Distinct())
                    {
                        if (!playerFormats.TryGetValue(player, out var counts))
                        {
                            playerFormats.Add(player, counts = []);
                        }
                        Count(counts, game.Format);
                    }
                }
            }
            // Most Games first: the order of every formats table in index.json, tournaments.json and players.json.
            var formats = Enumerable.Range(0, formatCount).Where(format => formatGames[format] > 0)
                .OrderByDescending(format => formatGames[format]).ThenBy(format => games.Formats[format], StringComparer.Ordinal).ToArray();
            var formatPosition = new int[formatCount];
            for (var i = 0; i < formats.Length; i++)
            {
                formatPosition[formats[i]] = i;
            }

            Directory.CreateDirectory(Path.Combine(directory, FormatsFolder));
            Directory.CreateDirectory(Path.Combine(directory, TournamentsFolder));

            WriteJson(Path.Combine(directory, "index.json"), json =>
            {
                json.WriteStartObject();
                Header(json);
                json.WritePropertyName("generated");
                json.WriteValue(generated.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
                json.WritePropertyName("formats");
                json.WriteStartArray();
                foreach (var format in formats)
                {
                    json.WriteStartObject();
                    json.WritePropertyName("id");
                    json.WriteValue(games.Formats[format]);
                    json.WritePropertyName("name");
                    json.WriteValue(games.FormatNames[format]);
                    json.WritePropertyName("games");
                    json.WriteValue(formatGames[format]);
                    json.WritePropertyName("matches");
                    json.WriteValue(formatMatches[format]);
                    json.WritePropertyName("lastDay");
                    json.WriteValue(formatLastDay[format]);
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteEndObject();
            });

            WriteJson(Path.Combine(directory, "tournaments.json"), json =>
            {
                json.WriteStartObject();
                Header(json);
                json.WritePropertyName("categories");
                json.WriteStartArray();
                foreach (var category in rules.Categories)
                {
                    json.WriteValue(category);
                }
                json.WriteEndArray();
                WriteFormatIds(json, games, formats);
                json.WritePropertyName("tournaments");
                json.WriteStartArray();
                for (var t = 0; t < tournaments.Count; t++)
                {
                    var tournament = tournaments[t];
                    json.WriteStartObject();
                    json.WritePropertyName("id");
                    json.WriteValue(tournament.Id);
                    json.WritePropertyName("name");
                    json.WriteValue(tournament.Name);
                    json.WritePropertyName("category");
                    json.WriteValue(tournament.Category);
                    json.WritePropertyName("year");
                    json.WriteValue(tournament.Year);
                    json.WritePropertyName("forum");
                    json.WriteValue(tournament.Forum);
                    json.WritePropertyName("games");
                    json.WriteValue(tournamentFormats[t].Values.Sum());
                    json.WritePropertyName("formats");
                    WriteCounts(json, tournamentFormats[t], formatPosition);
                    json.WritePropertyName("threads");
                    json.WriteStartArray();
                    foreach (var (thread, round) in tournament.Threads)
                    {
                        var entry = table.Threads[thread];
                        json.WriteStartArray();
                        WriteThreadId(json, entry);
                        json.WriteValue(round);
                        json.WriteValue(entry.FirstDay);
                        json.WriteValue(entry.Matches);
                        json.WriteValue(matchesWithGames[thread]);
                        json.WriteEndArray();
                    }
                    json.WriteEndArray();
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteEndObject();
            });

            var altsOfPlayers = alts.AltsOfPlayers();
            WriteJson(Path.Combine(directory, "players.json"), json =>
            {
                json.WriteStartObject();
                Header(json);
                WriteFormatIds(json, games, formats);
                json.WritePropertyName("players");
                json.WriteStartArray();
                foreach (var (player, counts) in playerFormats.OrderByDescending(entry => entry.Value.Values.Sum()).ThenBy(entry => table.PlayerKeys[entry.Key], StringComparer.Ordinal))
                {
                    json.WriteStartArray();
                    json.WriteValue(table.PlayerKeys[player]);
                    json.WriteValue(table.PlayerNames[player]);
                    json.WriteStartArray();
                    foreach (var alt in altsOfPlayers.GetValueOrDefault(player) ?? [])
                    {
                        json.WriteValue(alt);
                    }
                    json.WriteEndArray();
                    WriteCounts(json, counts, formatPosition);
                    json.WriteEndArray();
                }
                json.WriteEndArray();
                json.WriteEndObject();
            });

            foreach (var format in formats)
            {
                WriteMatches(Path.Combine(directory, FormatsFolder, games.Formats[format] + ".json"),
                    games, alts, order, groupsOfFormat[format].Select(g => groups[g]), format);
            }
            for (var t = 0; t < tournaments.Count; t++)
            {
                WriteMatches(Path.Combine(directory, TournamentsFolder, tournaments[t].Id + ".json"),
                    games, alts, order, groupsOfTournament[t].Select(g => groups[g]), -1);
            }
        }

        /// <summary>
        /// A Format or Tournament file: its Matches with all their Games and the Teams, Players and names these use, each
        /// table holding only what the file needs.
        /// </summary>
        /// <param name="firstFormat">The Format of a Format file, listed first in its formats table; -1 for none.</param>
        private static void WriteMatches(
            string path, ScoutedGames games, AltPlayers.Assignment alts, int[] order, IEnumerable<MatchGames> matchGames, int firstFormat)
        {
            var table = games.Matches;
            var groups = matchGames.ToList();
            var formats = new LocalTable();
            var players = new LocalTable();
            var teams = new LocalTable();
            var species = new LocalTable();
            var items = new LocalTable();
            var abilities = new LocalTable();
            var teras = new LocalTable();
            var moves = new LocalTable();
            if (firstFormat >= 0)
            {
                formats.Index(firstFormat);
            }
            foreach (var group in groups)
            {
                var match = table.Matches[group.Match];
                players.Index(match.A);
                players.Index(match.B);
                foreach (var game in Games(games, order, group))
                {
                    formats.Index(game.Format);
                    teams.Index(game.Team1);
                    teams.Index(game.Team2);
                }
            }
            var decoded = teams.Values.Select(team => TeamCodec.Decode(games.Teams[team]).ToList()).ToList();
            foreach (var member in decoded.SelectMany(team => team))
            {
                species.Index(member.Species);
                items.Index(member.Item);
                abilities.Index(member.Ability);
                teras.Index(member.Tera);
                foreach (var move in member.Moves)
                {
                    moves.Index(move);
                }
                foreach (var name in member.OtherNames)
                {
                    species.Index(name);
                }
            }

            WriteJson(path, json =>
            {
                json.WriteStartObject();
                Header(json);
                WriteStrings(json, "formats", formats, format => games.Formats[format]);
                json.WritePropertyName("players");
                json.WriteStartArray();
                foreach (var player in players.Values)
                {
                    json.WriteStartArray();
                    json.WriteValue(table.PlayerKeys[player]);
                    json.WriteValue(table.PlayerNames[player]);
                    json.WriteEndArray();
                }
                json.WriteEndArray();
                WriteStrings(json, "species", species, index => games.Species[index]);
                WriteStrings(json, "items", items, index => games.Items[index]);
                WriteStrings(json, "abilities", abilities, index => games.Abilities[index]);
                WriteStrings(json, "teras", teras, index => games.Teras[index]);
                WriteStrings(json, "moves", moves, index => games.Moves[index]);

                json.WritePropertyName("teams");
                json.WriteStartArray();
                foreach (var team in decoded)
                {
                    json.WriteStartArray();
                    foreach (var member in team)
                    {
                        json.WriteStartArray();
                        json.WriteValue(species.Find(member.Species));
                        json.WriteValue(items.Find(member.Item));
                        json.WriteValue(abilities.Find(member.Ability));
                        json.WriteValue(teras.Find(member.Tera));
                        json.WriteStartArray();
                        foreach (var move in member.Moves)
                        {
                            json.WriteValue(moves.Find(move));
                        }
                        json.WriteEndArray();
                        json.WriteValue(member.Lead ? 1 : 0);
                        if (member.OtherNames.Length > 0)
                        {
                            json.WriteStartArray();
                            foreach (var name in member.OtherNames)
                            {
                                json.WriteValue(species.Find(name));
                            }
                            json.WriteEndArray();
                        }
                        json.WriteEndArray();
                    }
                    json.WriteEndArray();
                }
                json.WriteEndArray();

                json.WritePropertyName("games");
                json.WriteStartArray();
                foreach (var group in groups)
                {
                    var match = table.Matches[group.Match];
                    var links = table.Threads[match.Thread].Links;
                    foreach (var game in Games(games, order, group))
                    {
                        json.WriteStartArray();
                        json.WriteValue(ReplayLinks.Id(links[game.Link]) ?? links[game.Link]);
                        json.WriteValue(formats.Find(game.Format));
                        json.WriteValue(game.UploadTime);
                        json.WriteValue(game.Alt1 >= 0 ? games.Alts[game.Alt1] : "");
                        json.WriteValue(game.Alt2 >= 0 ? games.Alts[game.Alt2] : "");
                        json.WriteValue(game.Winner);
                        json.WriteValue(teams.Find(game.Team1));
                        json.WriteValue(teams.Find(game.Team2));
                        json.WriteValue(alts.SideOfA(game, match));
                        json.WriteValue(game.Views);
                        json.WriteValue(game.Rating);
                        json.WriteEndArray();
                    }
                }
                json.WriteEndArray();

                json.WritePropertyName("matches");
                json.WriteStartArray();
                var gameIndex = 0;
                foreach (var group in groups)
                {
                    var match = table.Matches[group.Match];
                    json.WriteStartArray();
                    WriteThreadId(json, table.Threads[match.Thread]);
                    json.WriteValue(players.Find(match.A));
                    json.WriteValue(players.Find(match.B));
                    json.WriteValue(match.Winner);
                    json.WriteValue(match.Day);
                    json.WriteStartArray();
                    for (var i = 0; i < group.Count; i++)
                    {
                        json.WriteValue(gameIndex++);
                    }
                    json.WriteEndArray();
                    json.WriteEndArray();
                }
                json.WriteEndArray();
                json.WriteEndObject();
            });
        }

        /// <summary>Global positions of one file's table entries, numbered in the order the file first uses them.</summary>
        private sealed class LocalTable
        {
            private readonly Dictionary<int, int> _local = [];

            public List<int> Values { get; } = [];

            public void Index(int global)
            {
                if (global >= 0 && _local.TryAdd(global, Values.Count))
                {
                    Values.Add(global);
                }
            }

            /// <summary>The position in the file, -1 for none.</summary>
            public int Find(int global) => global >= 0 && _local.TryGetValue(global, out var local) ? local : -1;
        }

        private static (int Day, long Thread, int Match) MatchOrder(MatchTable table, int match)
        {
            var entry = table.Matches[match];
            return (entry.Day < 0 ? int.MaxValue : entry.Day, table.Threads[entry.Thread].NumericId, match);
        }

        private static IEnumerable<ScoutedGames.GameEntry> Games(ScoutedGames games, int[] order, MatchGames group)
        {
            for (var i = group.Start; i < group.Start + group.Count; i++)
            {
                yield return games.Games[order[i]];
            }
        }

        private static void Count(Dictionary<int, int> counts, int key) => counts[key] = counts.GetValueOrDefault(key) + 1;

        private static void Header(JsonWriter json)
        {
            json.WritePropertyName("v");
            json.WriteValue(Version);
        }

        private static void WriteFormatIds(JsonWriter json, ScoutedGames games, int[] formats)
        {
            json.WritePropertyName("formats");
            json.WriteStartArray();
            foreach (var format in formats)
            {
                json.WriteValue(games.Formats[format]);
            }
            json.WriteEndArray();
        }

        /// <summary>[[position of the Format, Games]...], most Games first.</summary>
        private static void WriteCounts(JsonWriter json, Dictionary<int, int> counts, int[] formatPosition)
        {
            json.WriteStartArray();
            foreach (var (format, count) in counts.OrderByDescending(entry => entry.Value).ThenBy(entry => formatPosition[entry.Key]))
            {
                json.WriteStartArray();
                json.WriteValue(formatPosition[format]);
                json.WriteValue(count);
                json.WriteEndArray();
            }
            json.WriteEndArray();
        }

        private static void WriteStrings(JsonWriter json, string name, LocalTable table, Func<int, string> value)
        {
            json.WritePropertyName(name);
            json.WriteStartArray();
            foreach (var index in table.Values)
            {
                json.WriteValue(value(index));
            }
            json.WriteEndArray();
        }

        /// <summary>Thread ids are numbers on Smogon; one that is not stays a string.</summary>
        private static void WriteThreadId(JsonWriter json, MatchTable.ThreadEntry thread)
        {
            if (thread.NumericId != long.MaxValue)
            {
                json.WriteValue(thread.NumericId);
            }
            else
            {
                json.WriteValue(thread.Id);
            }
        }

        private static void WriteJson(string path, Action<JsonWriter> write)
        {
            using var file = File.CreateText(path);
            using var json = new JsonTextWriter(file) { Formatting = Formatting.None };
            write(json);
        }
    }
}
