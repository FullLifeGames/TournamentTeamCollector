using NeoSmart.Caching.Sqlite;
using Newtonsoft.Json;
using ShowdownReplayScouter.Core.Data;
using ShowdownReplayScouter.Core.Util;
//using TournamentParser.Data;
using TournamentTeamCollector;

var smogonParserCachePath = args.Length > 0 ?
    args[0] :
    "/root/TournamentParser/SmogonTournamentParser.db";

var parserCache = new SqliteCache(
    new SqliteCacheOptions()
    {
        MemoryOnly = false,
        CachePath = smogonParserCachePath,
    }
);

var replayScouterCachePath = args.Length > 1 ?
    args[1] :
    "/home/apache/ShowdownReplayScouter.Cmd/ShowdownReplayScouter.db";

var replayScouterCache = new SqliteCache(
    new SqliteCacheOptions()
    {
        MemoryOnly = false,
        CachePath = replayScouterCachePath,
    }
);

var smogonTournament = new TournamentParser.Parser.SmogonParser(parserCache);

/** Full Scan */
var userRelationList = await smogonTournament.GetMatchesForUsers().ConfigureAwait(false);
/* */

/** Single Scan
await smogonTournament.ThreadScanner.AnalyzeTopic("https://www.smogon.com/forums/threads/official-smogon-tournament-xvii-finals-won-by-empo.3680402/", new CancellationToken()).ConfigureAwait(false);
var userRelationList = smogonTournament.ThreadScanner.NameUserTranslation;
*/

//var userRelationList = JsonConvert.DeserializeObject<IDictionary<string, User>>(await File.ReadAllTextAsync("userRelationOutput.json"));

var tournamentsToMatches = new Dictionary<string, ThreadWithReplays>();

foreach (var userRelation in userRelationList)
{
    foreach (var match in userRelation.Value.Matches)
    {
        if (match.Thread?.Id is not null)
        {
            if (!tournamentsToMatches.ContainsKey(match.Thread.Id))
            {
                tournamentsToMatches.Add(match.Thread.Id, new ThreadWithReplays(match.Thread));
            }
            tournamentsToMatches[match.Thread.Id].Replays.AddRange(match.Replays);
        }
    }
}

foreach (var tournamentMatch in tournamentsToMatches)
{
    tournamentMatch.Value.Replays = tournamentMatch.Value.Replays.Distinct().ToList();
}

var replayScouter = new ShowdownReplayScouter.Core.ReplayScouter.ShowdownReplayScouter(replayScouterCache);
foreach (var tournamentMatch in tournamentsToMatches)
{
    Console.WriteLine($"Scouting replays for: {tournamentMatch.Value.Name}");
    try
    {
        var result = await replayScouter.ScoutReplaysAsync(new ScoutingRequest()
        {
            Links = tournamentMatch.Value.Replays.Select((replay) => new Uri(replay))
        }).ConfigureAwait(false);
        if (result != null)
        {
            tournamentMatch.Value.Teams = result.Teams;
        }
    }
    catch (Exception)
    {
        Console.Error.WriteLine($"Error on thread: {tournamentMatch.Value.Name}");
    }
}

// serialize JSON directly to a file
using (var file = File.CreateText("output.json"))
{
    var serializer = new JsonSerializer();
    serializer.Serialize(file, tournamentsToMatches);
}

// serialize JSON directly to a file
using (var file = File.CreateText("outputJustThreads.json"))
{
    var serializer = new JsonSerializer();
    serializer.Serialize(file, tournamentsToMatches.Select((kv)
        => new KeyValuePair<string, TournamentParser.Data.Thread>(kv.Key, new TournamentParser.Data.Thread()
        {
            Name = kv.Value.Name,
            Id = kv.Value.Id,
            Locked = kv.Value.Locked
        })
    ).OrderBy((kv) => kv.Value.Name).ToDictionary(x => x.Key, x => x.Value));
}

if (!Directory.Exists("Tournaments"))
{
    Directory.CreateDirectory("Tournaments");
}

foreach (var tournamentMatch in tournamentsToMatches)
{
    if (tournamentMatch.Value.Name != null && tournamentMatch.Value.Id != null)
    {
        await File.WriteAllTextAsync($"Tournaments/{tournamentMatch.Value.Id}.txt",
            OutputPrinter.Print(
                new ScoutingRequest()
                {
                    Users = new List<string> { tournamentMatch.Value.Name },
                    Tiers = new List<string> { tournamentMatch.Value.Id }
                },
                tournamentMatch.Value.Teams
            )
        ).ConfigureAwait(false);
    }
}
