using Newtonsoft.Json;
using ShowdownReplayScouter.Core.Data;
using ShowdownReplayScouter.Core.Util;

namespace TournamentTeamCollector
{
    /// <summary>
    /// Writes the outputs of a run: output.json, outputJustThreads.json, newOutput.json and Tournaments/ with
    /// &lt;thread id&gt;.json, &lt;thread id&gt;.txt and outputJustThreads.json, and Matches/ if the run asks for it.
    /// <para>
    /// Tournaments are added one at a time, in the order of output.json (by thread name), and written right away, so a run
    /// holds the teams of one tournament instead of all of them (some 550,000 teams for all of Smogon's tournaments).
    /// Everything goes into a staging folder that <see cref="CommitAsync"/> moves into place: a run that stops or fails leaves
    /// the previous outputs as they were, and Tournaments/ holds only the tournaments of the run. Matches/ groups the Games
    /// across threads, so it is written at the end, from what <see cref="MatchFileRun"/> kept of each thread.
    /// </para>
    /// </summary>
    public sealed class OutputWriter : IDisposable
    {
        public const string TournamentsFolder = "Tournaments";
        private const string StagingFolder = ".output-new";
        private const string PreviousTournamentsFolder = ".Tournaments-previous";
        private const string PreviousMatchesFolder = ".Matches-previous";
        private const string JustThreadsFileName = "outputJustThreads.json";
        private const string NewOutputFileName = "newOutput.json";

        /// <summary>The order of output.json; tournaments must be added in it.</summary>
        public static readonly StringComparer NameOrder = StringComparer.InvariantCulture;

        private readonly string _directory;
        private readonly string _staging;
        private readonly string _stagingTournaments;
        private readonly JsonSerializer _serializer = new();
        private readonly JsonTextWriter _output;
        private readonly JsonTextWriter _newOutput;
        private readonly MatchFileRun? _matchFiles;
        private readonly List<(string Id, TournamentParser.Data.Thread Thread)> _threads = [];
        private bool _committed;

        public int Teams { get; private set; }

        /// <param name="matchFiles">Also writes Matches/ from it, if given.</param>
        public OutputWriter(string directory, MatchFileRun? matchFiles = null)
        {
            _directory = directory;
            _matchFiles = matchFiles;
            _staging = Path.Combine(directory, StagingFolder);
            if (Directory.Exists(_staging))
            {
                Directory.Delete(_staging, true);
            }
            _stagingTournaments = Directory.CreateDirectory(Path.Combine(_staging, TournamentsFolder)).FullName;
            _output = OpenObject(Path.Combine(_staging, RunOptions.OutputFileName));
            _newOutput = OpenObject(Path.Combine(_staging, NewOutputFileName));
        }

        /// <summary>Writes a scouted tournament. Add each tournament once, ordered by <see cref="NameOrder"/> of its name.</summary>
        public async Task AddAsync(string id, ThreadWithReplays tournament)
        {
            if (_threads.Count > 0 && NameOrder.Compare(_threads[^1].Thread.Name, tournament.Name) > 0)
            {
                throw new InvalidOperationException($"{tournament.Name} added after {_threads[^1].Thread.Name}: output.json is ordered by name");
            }
            _threads.Add((id, new TournamentParser.Data.Thread() { Name = tournament.Name, Id = tournament.Id, Locked = tournament.Locked }));

            if (tournament.Name is not null && tournament.Id is not null && tournament.ScoutingResult?.Teams is IEnumerable<Team> teams)
            {
                var request = new ScoutingRequest()
                {
                    Users = [tournament.Name],
                    Tiers = [tournament.Id],
                };
                tournament.ScoutingResult.Outputs = OutputPrinter.PrintObject(request, teams);
                Serialize(Path.Combine(_stagingTournaments, $"{tournament.Id}.json"), tournament.ScoutingResult);
                await File.WriteAllTextAsync(Path.Combine(_stagingTournaments, $"{tournament.Id}.txt"), OutputPrinter.Print(request, teams)).ConfigureAwait(false);
                Teams += teams.Count();
            }

            _output.WritePropertyName(id);
            _serializer.Serialize(_output, new OldThreadWithReplay(tournament));
            // Carries the Outputs set above.
            _newOutput.WritePropertyName(id);
            _serializer.Serialize(_newOutput, tournament);

            _matchFiles?.AddThread(id, tournament.ScoutingResult?.Teams);
        }

        /// <summary>Completes the files and moves them into place.</summary>
        public async Task CommitAsync()
        {
            CloseObject(_output);
            CloseObject(_newOutput);
            var justThreads = _threads.ToDictionary(entry => entry.Id, entry => entry.Thread);
            Serialize(Path.Combine(_staging, JustThreadsFileName), justThreads);
            Serialize(Path.Combine(_stagingTournaments, JustThreadsFileName), justThreads);
            // Before anything moves: a failure here leaves all previous outputs as they were.
            _matchFiles?.Write(Path.Combine(_staging, MatchFiles.Folder));

            foreach (var file in new[] { RunOptions.OutputFileName, JustThreadsFileName, NewOutputFileName })
            {
                File.Move(Path.Combine(_staging, file), Path.Combine(_directory, file), overwrite: true);
            }
            var previous = new List<string>() { MoveIntoPlace(TournamentsFolder, PreviousTournamentsFolder) };
            if (_matchFiles != null)
            {
                previous.Add(MoveIntoPlace(MatchFiles.Folder, PreviousMatchesFolder));
            }
            Directory.Delete(_staging, true);
            foreach (var folder in previous.Where(Directory.Exists))
            {
                await Task.Run(() => Directory.Delete(folder, true)).ConfigureAwait(false);
            }
            _committed = true;
        }

        /// <summary>Replaces a folder of the outputs by the staged one; returns where the previous one went.</summary>
        private string MoveIntoPlace(string folder, string previousFolder)
        {
            var live = Path.Combine(_directory, folder);
            var previous = Path.Combine(_directory, previousFolder);
            if (Directory.Exists(previous))
            {
                Directory.Delete(previous, true);
            }
            if (Directory.Exists(live))
            {
                Directory.Move(live, previous);
            }
            Directory.Move(Path.Combine(_staging, folder), live);
            return previous;
        }

        /// <summary>Without <see cref="CommitAsync"/>, removes everything written: the previous outputs stay as they were.</summary>
        public void Dispose()
        {
            if (_committed)
            {
                return;
            }
            ((IDisposable)_output).Dispose();
            ((IDisposable)_newOutput).Dispose();
            if (Directory.Exists(_staging))
            {
                Directory.Delete(_staging, true);
            }
        }

        private static JsonTextWriter OpenObject(string path)
        {
            var writer = new JsonTextWriter(File.CreateText(path));
            writer.WriteStartObject();
            return writer;
        }

        private static void CloseObject(JsonTextWriter writer)
        {
            writer.WriteEndObject();
            writer.Close();
        }

        private void Serialize(string path, object value)
        {
            using var file = File.CreateText(path);
            _serializer.Serialize(file, value);
        }
    }
}
