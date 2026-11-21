using Newtonsoft.Json;
using System.Text.RegularExpressions;

namespace TournamentTeamCollector
{
    /// <summary>
    /// The Category rules (Data/categories.json): which kind of Tournament a Tournament is. A rule on the Tournament's name
    /// wins over a rule on its forum (the nearest forum up the tree that has one), which wins over the fallback; an override
    /// in the overrides file wins over all. The Categories and their names are whatever the file says: the owner of the site
    /// changes them there, and no code depends on a Category's name.
    /// </summary>
    public sealed class CategoryRules
    {
        private sealed class RuleFile
        {
            /// <summary>All Categories, in the order the site lists them.</summary>
            [JsonProperty("categories")]
            public List<string>? Categories { get; set; }

            /// <summary>The Category of a Tournament no rule names.</summary>
            [JsonProperty("fallback")]
            public string? Fallback { get; set; }

            /// <summary>Rules on the Tournament's name, the first that matches wins.</summary>
            [JsonProperty("names")]
            public List<NameRule>? Names { get; set; }

            /// <summary>Rules on the forum, by forum key.</summary>
            [JsonProperty("forums")]
            public List<ForumRule>? Forums { get; set; }
        }

        private sealed class NameRule
        {
            [JsonProperty("category")]
            public string? Category { get; set; }

            /// <summary>A .NET regular expression, matched case-insensitively anywhere in the name.</summary>
            [JsonProperty("pattern")]
            public string? Pattern { get; set; }

            /// <summary>Names that also match this expression are not matched by the rule.</summary>
            [JsonProperty("except")]
            public string? Except { get; set; }
        }

        private sealed class ForumRule
        {
            [JsonProperty("category")]
            public string? Category { get; set; }

            [JsonProperty("forums")]
            public List<string>? Forums { get; set; }
        }

        private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

        private readonly List<(int Category, Regex Pattern, Regex? Except)> _names;
        private readonly Dictionary<string, int> _forums;

        private CategoryRules(IReadOnlyList<string> categories, int fallback, List<(int, Regex, Regex?)> names, Dictionary<string, int> forums)
        {
            Categories = categories;
            Fallback = fallback;
            _names = names;
            _forums = forums;
        }

        public IReadOnlyList<string> Categories { get; }

        /// <summary>The index of the fallback Category.</summary>
        public int Fallback { get; }

        /// <summary>The index of a Category by its name, or null.</summary>
        public int? Find(string? category)
        {
            for (var i = 0; i < Categories.Count; i++)
            {
                if (string.Equals(Categories[i], category, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return null;
        }

        /// <summary>The Category of the first name rule that matches, or null.</summary>
        public int? ByName(string tournamentName)
        {
            foreach (var (category, pattern, except) in _names)
            {
                if (pattern.IsMatch(tournamentName) && except?.IsMatch(tournamentName) != true)
                {
                    return category;
                }
            }
            return null;
        }

        /// <summary>The Category of the nearest forum up the tree that has a rule, or null.</summary>
        public int? ByForum(string? forum, ForumTree? tree)
        {
            if (forum == null)
            {
                return null;
            }
            foreach (var current in tree?.SelfAndAncestors(forum) ?? [forum])
            {
                if (_forums.TryGetValue(current, out var category))
                {
                    return category;
                }
            }
            return null;
        }

        /// <summary>Reads the rules; a rule naming an unknown Category or an invalid expression is an error.</summary>
        public static CategoryRules Load(string path)
        {
            RuleFile file;
            try
            {
                file = JsonConvert.DeserializeObject<RuleFile>(File.ReadAllText(path), new JsonSerializerSettings
                {
                    MissingMemberHandling = MissingMemberHandling.Error,
                }) ?? throw new InvalidDataException($"{path} is empty");
            }
            catch (JsonException e)
            {
                throw new InvalidDataException($"{path} is no valid Category rules file: {e.Message}", e);
            }

            var categories = file.Categories ?? [];
            if (categories.Count == 0 || categories.Any(string.IsNullOrWhiteSpace) || categories.Distinct(StringComparer.OrdinalIgnoreCase).Count() != categories.Count)
            {
                throw new InvalidDataException($"{path}: \"categories\" must list distinct, non-empty names");
            }
            int Category(string? name, string where) =>
                categories.FindIndex(category => string.Equals(category, name, StringComparison.OrdinalIgnoreCase)) is var index and >= 0
                    ? index
                    : throw new InvalidDataException($"{path}: {where} names the Category \"{name}\", which \"categories\" does not list");
            Regex Expression(string? pattern, string where)
            {
                try
                {
                    return new Regex(pattern ?? throw new InvalidDataException($"{path}: {where} has no pattern"),
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);
                }
                catch (ArgumentException e)
                {
                    throw new InvalidDataException($"{path}: {where} has an invalid pattern: {e.Message}", e);
                }
            }

            var fallback = Category(file.Fallback, "\"fallback\"");
            var names = new List<(int, Regex, Regex?)>();
            foreach (var (rule, i) in (file.Names ?? []).Select((rule, i) => (rule, i)))
            {
                var where = $"name rule {i + 1}";
                names.Add((Category(rule.Category, where), Expression(rule.Pattern, where), rule.Except == null ? null : Expression(rule.Except, where)));
            }
            var forums = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var rule in file.Forums ?? [])
            {
                var category = Category(rule.Category, "a forum rule");
                foreach (var forum in rule.Forums ?? [])
                {
                    if (!forums.TryAdd(forum, category))
                    {
                        throw new InvalidDataException($"{path}: the forum {forum} has two rules");
                    }
                }
            }
            return new CategoryRules(categories, fallback, names, forums);
        }
    }
}
