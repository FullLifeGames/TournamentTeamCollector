using System.Text.RegularExpressions;

namespace TournamentTeamCollector
{
    /// <summary>A Tournament: one or more Tournament Threads, with its Category.</summary>
    public sealed class Tournament
    {
        public required string Id { get; init; }

        public required string Name { get; init; }

        /// <summary>The year of the newest thread, null if no thread has a date.</summary>
        public int? Year { get; init; }

        /// <summary>The index of the Category in <see cref="CategoryRules.Categories"/>.</summary>
        public int Category { get; init; }

        /// <summary>The forum most of its threads sit in, null if unknown.</summary>
        public string? Forum { get; init; }

        /// <summary>The threads (indices into <see cref="MatchTable.Threads"/>) with their Round labels, oldest first.</summary>
        public required IReadOnlyList<(int Thread, string Round)> Threads { get; init; }

        /// <summary>The day of the newest thread's first match post, -1 if unknown.</summary>
        public int NewestDay { get; init; }
    }

    /// <summary>
    /// Groups the threads into Tournaments, as the prototype of the Tournament Teams view does: threads whose Tournament names
    /// (<see cref="TournamentNames.Split"/>) have the same key belong together, unless more than <see cref="GapDays"/> days lie
    /// between them (the UU Open of 2016 is not the UU Open of 2017). Names that several Tournaments share get the year their
    /// first thread started, and ids are made from the names.
    /// </summary>
    public static class TournamentGrouping
    {
        public const int GapDays = 150;

        private static readonly Regex Reserved = new("^(?:con|prn|aux|nul|com[0-9]|lpt[0-9])$", RegexOptions.CultureInvariant);

        private sealed class Cluster
        {
            public required string Name { get; set; }
            public required List<(int Thread, string Round)> Threads { get; init; }
            public int FirstDay { get; init; }
            public int NewestDay { get; init; }
            public long FirstThreadId { get; init; }
            public string Id { get; set; } = "";
        }

        /// <summary>The Tournaments, newest first, and the Tournament of each thread.</summary>
        public static (IReadOnlyList<Tournament> Tournaments, int[] TournamentOfThread) Derive(
            MatchTable matches, ForumTree? forums, CategoryRules rules, Overrides overrides)
        {
            var threads = matches.Threads;
            var byKey = new Dictionary<string, List<(int Thread, string Tournament, string Round)>>(StringComparer.Ordinal);
            for (var thread = 0; thread < threads.Count; thread++)
            {
                var (tournament, round) = TournamentNames.Split(threads[thread].Name);
                if (overrides.Threads.TryGetValue(threads[thread].Id, out var correction))
                {
                    tournament = string.IsNullOrWhiteSpace(correction.Tournament) ? tournament : correction.Tournament.Trim();
                    round = correction.Round?.Trim() ?? round;
                }
                // A name without Latin letters or digits groups only with the same name.
                var key = TournamentNames.Key(tournament) is { Length: > 0 } latin ? latin : "\u0001" + tournament.ToLowerInvariant();
                if (!byKey.TryGetValue(key, out var group))
                {
                    byKey.Add(key, group = []);
                }
                group.Add((thread, tournament, round));
            }

            var clusters = new List<Cluster>();
            foreach (var group in byKey.Values)
            {
                group.Sort((x, y) => (threads[x.Thread].FirstDay, threads[x.Thread].NumericId).CompareTo((threads[y.Thread].FirstDay, threads[y.Thread].NumericId)));
                var current = new List<(int Thread, string Tournament, string Round)>();
                var last = -1;
                foreach (var entry in group)
                {
                    var thread = threads[entry.Thread];
                    if (current.Count > 0 && thread.FirstDay >= 0 && last >= 0 && thread.FirstDay - last > GapDays)
                    {
                        clusters.Add(ToCluster(current, threads));
                        current = [];
                    }
                    current.Add(entry);
                    last = Math.Max(last, Math.Max(thread.FirstDay, thread.LastDay));
                }
                clusters.Add(ToCluster(current, threads));
            }

            // The same name for several Tournaments: each gets the year its first thread started.
            foreach (var sameName in clusters.GroupBy(cluster => TournamentNames.Key(cluster.Name) is { Length: > 0 } key ? key : cluster.Name).Where(g => g.Count() > 1))
            {
                foreach (var cluster in sameName.Where(cluster => cluster.FirstDay >= 0))
                {
                    cluster.Name = $"{cluster.Name} ({YearOf(cluster.FirstDay)})";
                }
            }

            // Oldest first, so that a Tournament keeps its id when newer ones of the same name come.
            clusters.Sort((x, y) => (x.FirstDay, x.FirstThreadId).CompareTo((y.FirstDay, y.FirstThreadId)));
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var cluster in clusters)
            {
                cluster.Id = UniqueId(cluster.Name, cluster.FirstThreadId, ids);
            }

            var tournamentOfThread = new int[threads.Count];
            var tournaments = new List<Tournament>(clusters.Count);
            foreach (var cluster in clusters)
            {
                var name = cluster.Name;
                var id = cluster.Id;
                int? category = null;
                if (overrides.Tournaments.TryGetValue(cluster.Id, out var correction))
                {
                    if (!string.IsNullOrWhiteSpace(correction.Name) && correction.Name.Trim() != name)
                    {
                        name = correction.Name.Trim();
                        ids.Remove(id);
                        id = UniqueId(name, cluster.FirstThreadId, ids);
                    }
                    if (correction.Category != null)
                    {
                        category = rules.Find(correction.Category)
                            ?? throw new InvalidDataException($"The override of {cluster.Id} names the Category \"{correction.Category}\", which the rules do not list");
                    }
                }
                var forum = MainForum(cluster.Threads.Select(entry => threads[entry.Thread].Id), forums);
                category ??= rules.ByName(name) ?? rules.ByForum(forum, forums) ?? rules.Fallback;
                tournaments.Add(new Tournament()
                {
                    Id = id,
                    Name = name,
                    Year = cluster.NewestDay >= 0 ? YearOf(cluster.NewestDay) : null,
                    Category = category.Value,
                    Forum = forum,
                    Threads = cluster.Threads,
                    NewestDay = cluster.NewestDay,
                });
            }
            tournaments.Sort((x, y) => y.NewestDay != x.NewestDay ? y.NewestDay.CompareTo(x.NewestDay) : string.CompareOrdinal(x.Id, y.Id));
            for (var i = 0; i < tournaments.Count; i++)
            {
                foreach (var (thread, _) in tournaments[i].Threads)
                {
                    tournamentOfThread[thread] = i;
                }
            }
            return (tournaments, tournamentOfThread);
        }

        public static int YearOf(int day) => DateTime.UnixEpoch.AddDays(day).Year;

        private static Cluster ToCluster(List<(int Thread, string Tournament, string Round)> entries, IReadOnlyList<MatchTable.ThreadEntry> threads)
        {
            var days = entries.Select(entry => threads[entry.Thread].FirstDay).Where(day => day >= 0).ToList();
            return new Cluster()
            {
                Name = entries[0].Tournament,
                Threads = entries.Select(entry => (entry.Thread, entry.Round)).ToList(),
                FirstDay = days.Count > 0 ? days.Min() : -1,
                NewestDay = days.Count > 0 ? days.Max() : -1,
                FirstThreadId = threads[entries[0].Thread].NumericId,
            };
        }

        /// <summary>
        /// The slug of the name; if another Tournament has it already, followed by the id of the Tournament's first thread,
        /// which does not change while its grouping does not.
        /// </summary>
        private static string UniqueId(string name, long firstThreadId, HashSet<string> ids)
        {
            var slug = TournamentNames.Slug(name) is { Length: > 0 } latin ? latin : "tournament";
            if (!Reserved.IsMatch(slug) && ids.Add(slug))
            {
                return slug;
            }
            var id = $"{slug}-{firstThreadId}";
            for (var n = 2; !ids.Add(id); n++)
            {
                id = $"{slug}-{firstThreadId}-{n}";
            }
            return id;
        }

        /// <summary>The forum most of the threads sit in; on a tie the first key in ordinal order. Null if none is known.</summary>
        private static string? MainForum(IEnumerable<string> threadIds, ForumTree? forums) =>
            forums == null
                ? null
                : threadIds.Select(forums.ForumOf).OfType<string>()
                    .GroupBy(forum => forum, StringComparer.Ordinal)
                    .OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => group.Key).FirstOrDefault();
    }
}
