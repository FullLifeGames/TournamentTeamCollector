using System.Globalization;
using TournamentTeamCollector;

const string Usage = """
    Usage: TournamentTeamCollector --matches <output.json> [--parser-db <SmogonTournamentParser.db>]
                                   [--forums <forums.json>] [--replay-db <ShowdownReplayScouter.db>]
                                   [--delay <seconds>] [--max-requests <count>]
           TournamentTeamCollector <SmogonTournamentParser.db> [<ShowdownReplayScouter.db>]

      --matches <file>        output.json of TournamentParser, written after its last complete run: the
                              tournament matches whose replays are scouted. Sends no request to Smogon.
      --parser-db <file>      Fallback when --matches is not given or its file does not exist: crawl Smogon
                              with TournamentParser 3.0 (headless Chromium for the bot check), crawl state
                              in this database. The second form does the same.
      --forums <file>         forums.json of TournamentParser: the forum of each thread, which decides the
                              Category of a Tournament in Matches/. Without it, or if the file does not exist,
                              the Categories come from the Tournament names alone.
      --replay-db <file>      Analysed replays, and replays the server no longer has
                              (default ShowdownReplayScouter.db).
      --delay <seconds>       Pause between two requests to the replay server (default 1).
      --max-requests <count>  Stop after this many requests to the replay server (and to Smogon when
                              crawling); the next run continues from there (default 0, no limit).

    Writes output.json, outputJustThreads.json, newOutput.json, Tournaments/ and Matches/ into the working
    folder, only after a complete run. Replays the server answers with 404 are asked for again after 30 days.
    Exit codes: 0 done, 1 error, 2 bot check, unexpected page or replay server refusing requests,
    3 request limit reached, 64 usage.
    """;

var options = new RunOptions() { MatchFiles = true };
var positional = new List<string>();
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--matches" when i + 1 < args.Length:
            options.MatchesPath = args[++i];
            break;
        case "--parser-db" when i + 1 < args.Length:
            options.ParserDatabasePath = args[++i];
            break;
        case "--forums" when i + 1 < args.Length:
            options.ForumsPath = args[++i];
            break;
        case "--replay-db" when i + 1 < args.Length:
            options.ReplayDatabasePath = args[++i];
            break;
        case "--delay" when i + 1 < args.Length && double.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0:
            options.RequestDelay = TimeSpan.FromSeconds(seconds);
            i++;
            break;
        case "--max-requests" when i + 1 < args.Length && int.TryParse(args[i + 1], out var maxRequests) && maxRequests >= 0:
            options.MaxRequests = maxRequests;
            i++;
            break;
        case "-h" or "--help":
            Console.WriteLine(Usage);
            return ExitCodes.Done;
        case var positionalArgument when !positionalArgument.StartsWith('-') && positional.Count < 2:
            positional.Add(positionalArgument);
            break;
        default:
            Console.Error.WriteLine($"Unknown or incomplete argument: {args[i]}");
            Console.Error.WriteLine(Usage);
            return ExitCodes.Usage;
    }
}
// The form of version 1: TournamentTeamCollector <SmogonTournamentParser.db> [<ShowdownReplayScouter.db>]
if (positional.Count > 0)
{
    options.ParserDatabasePath ??= positional[0];
}
if (positional.Count > 1)
{
    options.ReplayDatabasePath = positional[1];
}
if (options.MatchesPath == null && options.ParserDatabasePath == null)
{
    Console.Error.WriteLine(Usage);
    return ExitCodes.Usage;
}

return await Collector.RunAsync(options).ConfigureAwait(false);
