using ShowdownReplayScouter.Core.Data;

namespace TournamentTeamCollector
{
    public class OldThreadWithReplay : TournamentParser.Data.Thread
    {
        public OldThreadWithReplay(ThreadWithReplays thread)
        {
            Locked = thread.Locked;
            Name = thread.Name;
            Id = thread.Id;
            Replays = thread.Replays;
            Teams = thread.ScoutingResult?.Teams ?? new List<Team>();
        }

        public List<string> Replays { get; set; } = new List<string>();
        public IEnumerable<Team> Teams { get; set; } = new List<Team>();
    }
}
