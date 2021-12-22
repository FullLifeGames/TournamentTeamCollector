using ShowdownReplayScouter.Core.Data;

namespace TournamentTeamCollector
{
    public class ThreadWithReplays : TournamentParser.Data.Thread
    {
        public ThreadWithReplays()
        {
        }

        public ThreadWithReplays(TournamentParser.Data.Thread thread)
        {
            this.Locked = thread.Locked;
            this.Name = thread.Name;
            this.Id = thread.Id;
        }

        public List<string> Replays { get; set; } = new List<string>();
        public IEnumerable<Team> Teams { get; set; } = new List<Team>();
    }
}
