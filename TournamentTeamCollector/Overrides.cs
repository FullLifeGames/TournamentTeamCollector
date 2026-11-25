using Newtonsoft.Json;

namespace TournamentTeamCollector
{
    /// <summary>
    /// Hand-kept corrections of what the collector derives (Data/overrides.json): a thread's Tournament and Round, a
    /// Tournament's name or Category, two Smogon users that are one Player, an Alt's Player. The derivation is a heuristic
    /// over hand-written thread names, so the few results it gets wrong are fixed here rather than in its rules.
    /// </summary>
    public sealed class Overrides
    {
        public sealed class ThreadOverride
        {
            /// <summary>The Tournament the thread belongs to; it groups with the threads of that name as derived ones do.</summary>
            [JsonProperty("tournament")]
            public string? Tournament { get; set; }

            /// <summary>The Round of the thread; "" when the thread is the whole Tournament.</summary>
            [JsonProperty("round")]
            public string? Round { get; set; }
        }

        public sealed class TournamentOverride
        {
            [JsonProperty("name")]
            public string? Name { get; set; }

            [JsonProperty("category")]
            public string? Category { get; set; }
        }

        /// <summary>Thread id to its Tournament and Round.</summary>
        [JsonProperty("threads")]
        public Dictionary<string, ThreadOverride> Threads { get; set; } = [];

        /// <summary>Tournament id, as the collector derives it without this entry, to its name and Category.</summary>
        [JsonProperty("tournaments")]
        public Dictionary<string, TournamentOverride> Tournaments { get; set; } = [];

        /// <summary>A Smogon user key to the key of the Player it belongs to (the parser's keys are lower case).</summary>
        [JsonProperty("players")]
        public Dictionary<string, string> Players { get; set; } = [];

        /// <summary>A Showdown name to the key of its Player; names are compared as Showdown compares them.</summary>
        [JsonProperty("alts")]
        public Dictionary<string, string> Alts { get; set; } = [];

        /// <summary>The Player aliases with lower-case keys on both sides.</summary>
        public IReadOnlyDictionary<string, string> PlayerAliases() =>
            Players.ToDictionary(entry => entry.Key.Trim().ToLowerInvariant(), entry => entry.Value.Trim().ToLowerInvariant());

        /// <summary>Reads the overrides; a file that is not an object of these four tables is an error.</summary>
        public static Overrides Load(string path)
        {
            try
            {
                var overrides = JsonConvert.DeserializeObject<Overrides>(File.ReadAllText(path), new JsonSerializerSettings
                {
                    MissingMemberHandling = MissingMemberHandling.Error,
                }) ?? throw new InvalidDataException($"{path} is empty");
                overrides.Threads ??= [];
                overrides.Tournaments ??= [];
                overrides.Players ??= [];
                overrides.Alts ??= [];
                return overrides;
            }
            catch (JsonException e)
            {
                throw new InvalidDataException($"{path} is no valid overrides file: {e.Message}", e);
            }
        }
    }
}
