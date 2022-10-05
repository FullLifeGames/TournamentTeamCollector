using Newtonsoft.Json;
using ShowdownReplayScouter.Core.Util;
//using TournamentParser.Data;
using TournamentTeamCollector;

var smogonTournament = new TournamentParser.Tournament.SmogonTournament();

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

var replayScouter = new ShowdownReplayScouter.Core.ReplayScouter.ShowdownReplayScouter();
foreach (var tournamentMatch in tournamentsToMatches)
{
    Console.WriteLine($"Scouting replays for: {tournamentMatch.Value.Name}");
    try
    {
        var result = await replayScouter.ScoutReplaysAsync(new ShowdownReplayScouter.Core.Data.ScoutingRequest()
        {
            Links = tournamentMatch.Value.Replays.Select((replay) => new Uri(replay))
        }).ConfigureAwait(false);
        tournamentMatch.Value.Teams = result.Teams;
    }
    catch(Exception)
    {
        Console.Error.WriteLine($"Error on thread: {tournamentMatch.Value.Name}");
    }
}

var json = JsonConvert.SerializeObject(tournamentsToMatches);

await File.WriteAllTextAsync("output.json", json).ConfigureAwait(false);

var jsonJustThreads = JsonConvert.SerializeObject(tournamentsToMatches.Select((kv)
    => new KeyValuePair<string, TournamentParser.Data.Thread>(kv.Key, new TournamentParser.Data.Thread()
    {
        Name = kv.Value.Name,
        Id = kv.Value.Id,
        Locked = kv.Value.Locked
    })
).OrderBy((kv) => kv.Value.Name).ToDictionary(x => x.Key, x => x.Value));

await File.WriteAllTextAsync("outputJustThreads.json", jsonJustThreads).ConfigureAwait(false);

if (!Directory.Exists("Tournaments"))
{
    Directory.CreateDirectory("Tournaments");
}

foreach (var tournamentMatch in tournamentsToMatches)
{
    await File.WriteAllTextAsync($"Tournaments/{tournamentMatch.Value.Id}.txt",
        OutputPrinter.Print(
            new ShowdownReplayScouter.Core.Data.ScoutingRequest()
            {
                Users = new List<string> { tournamentMatch.Value.Name },
                Tiers = new List<string> { tournamentMatch.Value.Id }
            },
            tournamentMatch.Value.Teams
        )
    ).ConfigureAwait(false);
}
