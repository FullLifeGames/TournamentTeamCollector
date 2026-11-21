using Newtonsoft.Json;
using TournamentParser.Data;

namespace TournamentTeamCollector
{
    public static class TournamentMatches
    {
        /// <summary>
        /// Reads output.json as TournamentParser.Cmd writes it after a complete run: user name to user with matches,
        /// the same dictionary <c>SmogonParser.GetMatchesForUsers()</c> returns. The users are read one at a time: the file
        /// holds every match once per player (440 MB in November 2026), and only the threads and replays are kept (for
        /// Matches/ also the compact match table of <see cref="GroupByThread"/>).
        /// </summary>
        public static IEnumerable<User> ReadParserOutput(string path)
        {
            using var reader = new JsonTextReader(File.OpenText(path));
            var serializer = new JsonSerializer();
            if (!reader.Read() || reader.TokenType != JsonToken.StartObject)
            {
                throw new InvalidDataException($"{path} is not an object of users");
            }
            while (reader.Read() && reader.TokenType == JsonToken.PropertyName)
            {
                reader.Read();
                if (serializer.Deserialize<User>(reader) is User user)
                {
                    yield return user;
                }
            }
            if (reader.TokenType != JsonToken.EndObject)
            {
                throw new InvalidDataException($"{path} ends before its last user");
            }
        }

        /// <summary>
        /// The tournaments (threads) of the matches with the distinct replays of each, in the order the matches name them.
        /// Matches without thread are skipped, and so are replay links that are not absolute http(s) URLs: one bad link must
        /// not cost the tournament its other replays.
        /// </summary>
        /// <param name="matches">If given, also receives the users and matches, for the match files; completed at the end.</param>
        public static Dictionary<string, ThreadWithReplays> GroupByThread(IEnumerable<User> users, MatchTable? matches = null)
        {
            var tournaments = new Dictionary<string, ThreadWithReplays>();
            var known = new Dictionary<string, Dictionary<string, int>>();
            foreach (var user in users)
            {
                matches?.AddUser(user);
                foreach (var match in user.Matches)
                {
                    if (match.Thread?.Id is not string id)
                    {
                        continue;
                    }
                    if (!tournaments.TryGetValue(id, out var tournament))
                    {
                        tournament = new ThreadWithReplays(match.Thread);
                        tournaments.Add(id, tournament);
                        known.Add(id, new Dictionary<string, int>(StringComparer.Ordinal));
                    }
                    var links = matches != null ? new List<int>(match.Replays.Count) : null;
                    foreach (var replay in match.Replays)
                    {
                        if (!IsWebLink(replay))
                        {
                            continue;
                        }
                        if (!known[id].TryGetValue(replay, out var position))
                        {
                            position = tournament.Replays.Count;
                            known[id].Add(replay, position);
                            tournament.Replays.Add(replay);
                        }
                        if (links != null && !links.Contains(position))
                        {
                            links.Add(position);
                        }
                    }
                    if (links != null)
                    {
                        matches!.AddMatch(match, tournament, [.. links]);
                    }
                }
            }
            matches?.Complete();
            return tournaments;
        }

        private static bool IsWebLink(string? link) =>
            Uri.TryCreate(link, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }
}
