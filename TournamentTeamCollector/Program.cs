using Microsoft.Extensions.Caching.StackExchangeRedis;
using Newtonsoft.Json;
using ShowdownReplayScouter.Core.Data;
using ShowdownReplayScouter.Core.Util;
//using TournamentParser.Data;
using TournamentTeamCollector;

var smogonParserCachePath = args.Length > 0 ?
    args[0] :
    "/root/TournamentParser/SmogonTournamentParser.db";

// To experiment with weird results
TournamentParser.Util.Common.ParallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 1 };

var parserCache = new RedisCache(new RedisCacheOptions()
    {
        Configuration = "localhost;connectTimeout=10000",
        InstanceName = "SmogonTournamentParser",
    }
);

var replayScouterCachePath = args.Length > 1 ?
    args[1] :
    "/home/apache/ShowdownReplayScouter.Cmd/ShowdownReplayScouter.db";

var replayScouterCache = new RedisCache(new RedisCacheOptions()
    {
        Configuration = "localhost",
        InstanceName = "ShowdownReplayScouter",
    }
);

var smogonTournament = new TournamentParser.Parser.SmogonParser(parserCache);

/** Full Scan */
var userRelationList = await smogonTournament.GetMatchesForUsers().ConfigureAwait(false);
/* */

/** Single Scan 
var tasks = new List<Task>
{
    smogonTournament.ThreadScanner.AnalyzeTopic("http://www.smogon.com/forums/threads/smogon-premier-league-xiii-finals-won-by-team-raiders.3699347/", new CancellationToken()),
    smogonTournament.ThreadScanner.AnalyzeTopic("https://www.smogon.com/forums/threads/spl-xiii-replays.3695657/", new CancellationToken()),
    smogonTournament.ThreadScanner.AnalyzeTopic("http://www.smogon.com/forums/threads/smogon-premier-league-xiii-semifinals.3698695/", new CancellationToken()),
    smogonTournament.ThreadScanner.AnalyzeTopic("http://www.smogon.com/forums/threads/smogon-premier-league-xiii-week-9.3698322/", new CancellationToken()),
    smogonTournament.ThreadScanner.AnalyzeTopic("http://www.smogon.com/forums/threads/smogon-premier-league-xiii-week-8.3698000/", new CancellationToken()),
    smogonTournament.ThreadScanner.AnalyzeTopic("http://www.smogon.com/forums/threads/smogon-premier-league-xiii-week-7.3697650/", new CancellationToken()),
    smogonTournament.ThreadScanner.AnalyzeTopic("http://www.smogon.com/forums/threads/smogon-premier-league-xiii-week-6.3697278/", new CancellationToken()),
    smogonTournament.ThreadScanner.AnalyzeTopic("http://www.smogon.com/forums/threads/smogon-premier-league-xiii-week-5.3696960/", new CancellationToken()),
    smogonTournament.ThreadScanner.AnalyzeTopic("http://www.smogon.com/forums/threads/smogon-premier-league-xiii-week-4.3696633/", new CancellationToken()),
    smogonTournament.ThreadScanner.AnalyzeTopic("http://www.smogon.com/forums/threads/smogon-premier-league-xiii-week-3.3696316/", new CancellationToken()),
    smogonTournament.ThreadScanner.AnalyzeTopic("http://www.smogon.com/forums/threads/smogon-premier-league-xiii-week-2.3695985/", new CancellationToken()),
    smogonTournament.ThreadScanner.AnalyzeTopic("http://www.smogon.com/forums/threads/smogon-premier-league-xiii-week-1.3695656/", new CancellationToken()),
};

Task.WaitAll(tasks.ToArray());

var userRelationList = smogonTournament.ThreadScanner.NameUserTranslation;
/* */

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
var count = 1;
var maxLength = tournamentsToMatches.Count;
foreach (var tournamentMatch in tournamentsToMatches)
{
    Console.WriteLine($"Scouting {tournamentMatch.Value.Replays.Count} replays for: {tournamentMatch.Value.Name} ({count}/{maxLength})");
    try
    {
        var scoutingResult = await replayScouter.ScoutReplaysAsync(new ScoutingRequest()
        {
            Links = tournamentMatch.Value.Replays.Select((replay) => new Uri(replay))
        }).ConfigureAwait(false);
        if (scoutingResult is not null)
        {
            tournamentMatch.Value.ScoutingResult = new ApiScoutingResult(scoutingResult);
            Console.WriteLine($"Scouted {tournamentMatch.Value.ScoutingResult.Teams.Count()} teams for: {tournamentMatch.Value.Name} ({count}/{maxLength})");
        }
    }
    catch (Exception)
    {
        Console.Error.WriteLine($"Error on thread: {tournamentMatch.Value.Name}");
    }
    count++;
}

// serialize JSON directly to a file
using (var file = File.CreateText("output.json"))
{
    var serializer = new JsonSerializer();
    serializer.Serialize(file, tournamentsToMatches.Select((kv)
        => new KeyValuePair<string, OldThreadWithReplay>(kv.Key, new OldThreadWithReplay(kv.Value))
    ).OrderBy((kv) => kv.Value.Name).ToDictionary(x => x.Key, x => x.Value));
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
    var teams = tournamentMatch.Value.ScoutingResult?.Teams;
    if (tournamentMatch.Value.Name is not null
        && tournamentMatch.Value.Id is not null
        && tournamentMatch.Value.ScoutingResult is not null
        && teams is not null
    )
    {
        tournamentMatch.Value.ScoutingResult.Outputs = OutputPrinter.PrintObject(
               new ScoutingRequest()
               {
                   Users = new List<string> { tournamentMatch.Value.Name },
                   Tiers = new List<string> { tournamentMatch.Value.Id }
               },
               teams
           );

        // serialize JSON directly to a file
        using (var file = File.CreateText($"Tournaments/{tournamentMatch.Value.Id}.json"))
        {
            var serializer = new JsonSerializer();
            serializer.Serialize(file, tournamentMatch.Value.ScoutingResult);
        }

        await File.WriteAllTextAsync($"Tournaments/{tournamentMatch.Value.Id}.txt",
            OutputPrinter.Print(
                new ScoutingRequest()
                {
                    Users = new List<string> { tournamentMatch.Value.Name },
                    Tiers = new List<string> { tournamentMatch.Value.Id }
                },
                teams
            )
        ).ConfigureAwait(false);
    }
}

// serialize JSON directly to a file
using (var file = File.CreateText("Tournaments/outputJustThreads.json"))
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

// serialize JSON directly to a file
using (var file = File.CreateText("newOutput.json"))
{
    var serializer = new JsonSerializer();
    serializer.Serialize(file, tournamentsToMatches);
}
