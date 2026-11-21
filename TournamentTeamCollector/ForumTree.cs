using Newtonsoft.Json;

namespace TournamentTeamCollector
{
    /// <summary>
    /// forums.json of TournamentParser: Smogon's forum tree and the forum each thread sits in. The Category of a Tournament
    /// comes from the forum of its threads, so this is read when the parser has written it.
    /// </summary>
    public sealed class ForumTree
    {
        private sealed class ForumFile
        {
            [JsonProperty("forums")]
            public Dictionary<string, ForumEntry>? Forums { get; set; }

            [JsonProperty("threads")]
            public Dictionary<string, string>? Threads { get; set; }
        }

        private sealed class ForumEntry
        {
            [JsonProperty("u")]
            public string? Url { get; set; }

            [JsonProperty("p")]
            public string? Parent { get; set; }

            [JsonProperty("n")]
            public string? Title { get; set; }
        }

        private readonly Dictionary<string, string?> _parents;
        private readonly Dictionary<string, string> _threads;

        private ForumTree(Dictionary<string, string?> parents, Dictionary<string, string> threads)
        {
            _parents = parents;
            _threads = threads;
        }

        public int ForumCount => _parents.Count;

        public int ThreadCount => _threads.Count;

        /// <summary>The key of the forum the thread sits in, or null if the file does not know the thread.</summary>
        public string? ForumOf(string threadId) => _threads.TryGetValue(threadId, out var forum) ? forum : null;

        /// <summary>The forum and the forums above it, nearest first.</summary>
        public IEnumerable<string> SelfAndAncestors(string forum)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (string? current = forum; current != null && seen.Add(current); current = _parents.GetValueOrDefault(current))
            {
                yield return current;
            }
        }

        public static ForumTree Create(IReadOnlyDictionary<string, string?> parents, IReadOnlyDictionary<string, string> threads) =>
            new(parents.ToDictionary(), threads.ToDictionary());

        /// <summary>Reads forums.json; throws <see cref="InvalidDataException"/> if it is not in the parser's shape.</summary>
        public static ForumTree Load(string path)
        {
            ForumFile? file;
            try
            {
                using var reader = new JsonTextReader(File.OpenText(path));
                file = new JsonSerializer().Deserialize<ForumFile>(reader);
            }
            catch (JsonException e)
            {
                throw new InvalidDataException($"{path} is no forum file: {e.Message}", e);
            }
            if (file?.Forums == null || file.Threads == null)
            {
                throw new InvalidDataException($"{path} is no forum file: it needs \"forums\" and \"threads\"");
            }
            return new ForumTree(
                file.Forums.ToDictionary(entry => entry.Key, entry => entry.Value?.Parent, StringComparer.Ordinal),
                new Dictionary<string, string>(file.Threads, StringComparer.Ordinal)
            );
        }
    }
}
