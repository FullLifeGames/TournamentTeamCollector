using System.Text.RegularExpressions;

namespace TournamentTeamCollector
{
    /// <summary>
    /// Replay ids from the replay links in Smogon's posts. The same replay is linked with and without "?p2", with and without
    /// the password of a private replay, and in several threads (a round thread and a replay collection), so the match files
    /// know a replay by its id, not by its link.
    /// </summary>
    public static class ReplayLinks
    {
        // "/smogtours-gen9ou-848010", "/gen9ou-2301234567", "/gen8lc-1136072818-gkyvoxcnfnp1zqe8ds6g6wew0e62zldpw"
        private static readonly Regex Path = new(
            @"^/+(?:replay/)?(?<id>[a-z0-9]+(?:-[a-z0-9]+)*?-(?<number>\d+))(?<password>-[a-z0-9]+pw)?(?:\.(?:json|log))?/*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
        );

        /// <summary>
        /// The replay as the link names it, with the password of a private replay (its page needs it), or null if the link
        /// names no replay.
        /// </summary>
        public static string? Id(string link) =>
            Match(link) is Match match ? match.Groups["id"].Value + match.Groups["password"].Value : null;

        /// <summary>The same for every link of one replay: lower case, without password. Null if the link names no replay.</summary>
        public static string? Key(string link) =>
            Match(link) is Match match ? match.Groups["id"].Value.ToLowerInvariant() : null;

        /// <summary>
        /// The format a replay id names, for replays whose analysis carries none: the part before the number, after the server
        /// ("smogtours-gen9ou-848010" is gen9ou).
        /// </summary>
        public static string? FormatOf(string replayId)
        {
            var match = Path.Match("/" + replayId);
            if (!match.Success)
            {
                return null;
            }
            var parts = match.Groups["id"].Value.ToLowerInvariant().Split('-');
            return parts.Length >= 2 ? parts[^2] : null;
        }

        private static Match? Match(string link)
        {
            if (!Uri.TryCreate(link, UriKind.Absolute, out var uri))
            {
                return null;
            }
            var match = Path.Match(uri.AbsolutePath);
            return match.Success ? match : null;
        }
    }
}
