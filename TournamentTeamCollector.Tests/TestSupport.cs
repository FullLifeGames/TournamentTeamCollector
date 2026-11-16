using Microsoft.Extensions.Caching.Distributed;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Net;
using TournamentParser.Data;

namespace TournamentTeamCollector.Tests
{
    /// <summary>A replay server in memory: answers the registered URLs, everything else with 404, and counts requests.</summary>
    internal sealed class FakeReplayServer : HttpMessageHandler
    {
        private readonly ConcurrentDictionary<string, Func<HttpResponseMessage>> _answers = new();

        public ConcurrentQueue<string> Requests { get; } = new();

        public FakeReplayServer Serve(string url, string json)
        {
            _answers[url] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
            return this;
        }

        public FakeReplayServer Answer(string url, HttpStatusCode status)
        {
            _answers[url] = () => new HttpResponseMessage(status);
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Requests.Enqueue(url);
            var response = _answers.TryGetValue(url, out var answer) ? answer() : new HttpResponseMessage(HttpStatusCode.NotFound);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    /// <summary>An IDistributedCache in memory that keeps the entry options, to check expiries without waiting for them.</summary>
    internal sealed class RecordingCache : IDistributedCache
    {
        public ConcurrentDictionary<string, (byte[] Value, DistributedCacheEntryOptions Options)> Entries { get; } = new();

        public byte[]? Get(string key) => Entries.TryGetValue(key, out var entry) ? entry.Value : null;

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => Entries[key] = (value, options);

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }

        public void Refresh(string key) { }

        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Remove(string key) => Entries.TryRemove(key, out _);

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }
    }

    internal static class ParserOutput
    {
        public static TournamentMatch Match(string threadId, string threadName, string first, string second, params string[] replays) => new()
        {
            FirstUser = first,
            SecondUser = second,
            Thread = new TournamentParser.Data.Thread() { Id = threadId, Name = threadName, Locked = true },
            Replays = replays.ToList(),
            Finished = true,
            Winner = first,
        };

        /// <summary>The users of the matches, each match in the bag of both players, as TournamentParser builds them.</summary>
        public static Dictionary<string, User> Users(params TournamentMatch[] matches)
        {
            var users = new Dictionary<string, User>();
            foreach (var match in matches)
            {
                foreach (var name in new[] { match.FirstUser!, match.SecondUser! })
                {
                    if (!users.TryGetValue(name, out var user))
                    {
                        user = new User() { Name = name, NormalName = name.ToLowerInvariant() };
                        users.Add(name, user);
                    }
                    user.Matches.Add(match);
                }
            }
            return users;
        }

        /// <summary>Writes the users as TournamentParser.Cmd writes output.json.</summary>
        public static void Write(string path, IDictionary<string, User> users)
        {
            using var file = File.CreateText(path);
            new JsonSerializer().Serialize(file, users);
        }
    }

    internal sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("ttc-tests-").FullName;

        public string File(string name) => System.IO.Path.Combine(Path, name);

        public void Dispose()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(Path, true);
        }
    }
}
