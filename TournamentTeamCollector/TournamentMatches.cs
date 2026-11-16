using Newtonsoft.Json;
using TournamentParser.Data;

namespace TournamentTeamCollector
{
    public static class TournamentMatches
    {
        /// <summary>
        /// Reads output.json as TournamentParser.Cmd writes it after a complete run: user name to user with matches,
        /// the same dictionary <c>SmogonParser.GetMatchesForUsers()</c> returns.
        /// </summary>
        public static IDictionary<string, User> ReadParserOutput(string path)
        {
            using var reader = new JsonTextReader(File.OpenText(path));
            return new JsonSerializer().Deserialize<Dictionary<string, User>>(reader)
                ?? throw new InvalidDataException($"{path} holds no users");
        }

        /// <summary>
        /// The tournaments (threads) of the matches with the distinct replays of each, in the order the matches name them.
        /// Matches without thread are skipped, and so are replay links that are not absolute http(s) URLs: one bad link must
        /// not cost the tournament its other replays.
        /// </summary>
        public static Dictionary<string, ThreadWithReplays> GroupByThread(IEnumerable<User> users)
        {
            var tournaments = new Dictionary<string, ThreadWithReplays>();
            var known = new Dictionary<string, HashSet<string>>();
            foreach (var match in users.SelectMany(user => user.Matches))
            {
                if (match.Thread?.Id is not string id)
                {
                    continue;
                }
                if (!tournaments.TryGetValue(id, out var tournament))
                {
                    tournament = new ThreadWithReplays(match.Thread);
                    tournaments.Add(id, tournament);
                    known.Add(id, new HashSet<string>(StringComparer.Ordinal));
                }
                foreach (var replay in match.Replays)
                {
                    if (IsWebLink(replay) && known[id].Add(replay))
                    {
                        tournament.Replays.Add(replay);
                    }
                }
            }
            return tournaments;
        }

        private static bool IsWebLink(string? link) =>
            Uri.TryCreate(link, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }
}
