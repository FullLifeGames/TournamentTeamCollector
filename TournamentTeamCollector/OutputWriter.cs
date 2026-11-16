using Newtonsoft.Json;
using ShowdownReplayScouter.Core.Data;
using ShowdownReplayScouter.Core.Util;

namespace TournamentTeamCollector
{
    /// <summary>
    /// Writes the outputs of a complete run: output.json, outputJustThreads.json, newOutput.json and Tournaments/ with
    /// &lt;thread id&gt;.json, &lt;thread id&gt;.txt and outputJustThreads.json. Everything is written into a staging folder first and
    /// then moved into place, so a run that fails while writing leaves the previous outputs as they were, and Tournaments/
    /// holds only the tournaments of this run.
    /// </summary>
    public static class OutputWriter
    {
        public const string TournamentsFolder = "Tournaments";
        private const string StagingFolder = ".output-new";
        private const string PreviousTournamentsFolder = ".Tournaments-previous";
        private static readonly string[] TopLevelFiles = [RunOptions.OutputFileName, "outputJustThreads.json", "newOutput.json"];

        public static async Task WriteAsync(string directory, IReadOnlyDictionary<string, ThreadWithReplays> tournaments)
        {
            var staging = Path.Combine(directory, StagingFolder);
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, true);
            }
            var stagingTournaments = Directory.CreateDirectory(Path.Combine(staging, TournamentsFolder)).FullName;

            var byName = tournaments.OrderBy(kv => kv.Value.Name, StringComparer.InvariantCulture).ToList();
            var justThreads = byName.ToDictionary(
                kv => kv.Key,
                kv => new TournamentParser.Data.Thread() { Name = kv.Value.Name, Id = kv.Value.Id, Locked = kv.Value.Locked }
            );

            Serialize(Path.Combine(staging, RunOptions.OutputFileName), byName.ToDictionary(kv => kv.Key, kv => new OldThreadWithReplay(kv.Value)));
            Serialize(Path.Combine(staging, "outputJustThreads.json"), justThreads);

            foreach (var tournament in tournaments.Values)
            {
                if (tournament.Name is null || tournament.Id is null || tournament.ScoutingResult?.Teams is not IEnumerable<Team> teams)
                {
                    continue;
                }
                var request = new ScoutingRequest()
                {
                    Users = [tournament.Name],
                    Tiers = [tournament.Id],
                };
                tournament.ScoutingResult.Outputs = OutputPrinter.PrintObject(request, teams);
                Serialize(Path.Combine(stagingTournaments, $"{tournament.Id}.json"), tournament.ScoutingResult);
                await File.WriteAllTextAsync(Path.Combine(stagingTournaments, $"{tournament.Id}.txt"), OutputPrinter.Print(request, teams)).ConfigureAwait(false);
            }
            Serialize(Path.Combine(stagingTournaments, "outputJustThreads.json"), justThreads);

            // Written last: it carries the Outputs set above.
            Serialize(Path.Combine(staging, "newOutput.json"), tournaments);

            foreach (var file in TopLevelFiles)
            {
                File.Move(Path.Combine(staging, file), Path.Combine(directory, file), overwrite: true);
            }
            var live = Path.Combine(directory, TournamentsFolder);
            var previous = Path.Combine(directory, PreviousTournamentsFolder);
            if (Directory.Exists(previous))
            {
                Directory.Delete(previous, true);
            }
            if (Directory.Exists(live))
            {
                Directory.Move(live, previous);
            }
            Directory.Move(stagingTournaments, live);
            Directory.Delete(staging, true);
            if (Directory.Exists(previous))
            {
                Directory.Delete(previous, true);
            }
        }

        private static void Serialize(string path, object value)
        {
            using var file = File.CreateText(path);
            new JsonSerializer().Serialize(file, value);
        }
    }
}
