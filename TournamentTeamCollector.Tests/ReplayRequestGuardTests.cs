using NUnit.Framework;
using System.Net;

namespace TournamentTeamCollector.Tests
{
    public class ReplayRequestGuardTests
    {
        private const string Gone = "https://replay.pokemonshowdown.com/gen8ou-1.json";
        private const string Present = "https://replay.pokemonshowdown.com/gen9ou-2.json";

        private static (HttpClient Client, ReplayRequestGuard Guard, FakeReplayServer Server, RecordingCache Cache) Create(int maxRequests = 0)
        {
            var server = new FakeReplayServer().Serve(Present, "{}");
            var cache = new RecordingCache();
            var guard = new ReplayRequestGuard(cache, maxRequests, TimeSpan.FromDays(30), server);
            return (new HttpClient(guard), guard, server, cache);
        }

        [Test]
        public async Task AReplayThatIsGoneIsNotRequestedAgainUntilTheEntryExpires()
        {
            var (client, guard, server, cache) = Create();

            using (var first = await client.GetAsync(Gone))
            {
                Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            }
            using (var second = await client.GetAsync(Gone + "?query=ignored"))
            {
                Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            }

            Assert.That(server.Requests, Is.EqualTo(new[] { Gone }));
            Assert.That(guard.Sent, Is.EqualTo(1));
            Assert.That(guard.NewlyMissing, Is.EqualTo(1));
            Assert.That(guard.KnownMissing, Is.EqualTo(1));
            var entry = cache.Entries.Single();
            Assert.That(entry.Key, Does.Contain(Gone));
            Assert.That(entry.Value.Options.AbsoluteExpirationRelativeToNow, Is.EqualTo(TimeSpan.FromDays(30)));
        }

        [Test]
        public async Task OnlyGoneReplaysAreRemembered()
        {
            var (client, _, server, cache) = Create();
            server.Answer(Gone, HttpStatusCode.Forbidden);

            (await client.GetAsync(Present)).Dispose();
            (await client.GetAsync(Gone)).Dispose();
            (await client.GetAsync(Gone)).Dispose();

            Assert.That(cache.Entries, Is.Empty);
            Assert.That(server.Requests, Has.Count.EqualTo(3));
        }

        [Test]
        public async Task RefusalsInARowStopTheRun()
        {
            var (client, guard, server, _) = Create();
            server.Answer(Gone, HttpStatusCode.Forbidden);

            for (var i = 1; i < ReplayRequestGuard.RefusalsToStop; i++)
            {
                (await client.GetAsync(Gone)).Dispose();
            }
            Assert.That(guard.StopReason, Is.EqualTo(StopReason.None));

            Assert.ThrowsAsync(Is.InstanceOf<OperationCanceledException>(), () => client.GetAsync(Gone));
            Assert.That(guard.StopReason, Is.EqualTo(StopReason.Refused));
            Assert.That(guard.StopToken.IsCancellationRequested, Is.True);
            Assert.That(guard.StopMessage, Does.Contain("HTTP 403"));

            // Nothing goes out after the stop.
            Assert.ThrowsAsync(Is.InstanceOf<OperationCanceledException>(), () => client.GetAsync(Present));
            Assert.That(server.Requests, Has.Count.EqualTo(ReplayRequestGuard.RefusalsToStop));
        }

        [TestCase(HttpStatusCode.TooManyRequests)]
        [TestCase(HttpStatusCode.ServiceUnavailable)]
        public async Task ErrorsLeftAfterTheRetriesAreRefusals(HttpStatusCode status)
        {
            var (client, guard, server, _) = Create();
            server.Answer(Gone, status);

            for (var i = 1; i < ReplayRequestGuard.RefusalsToStop; i++)
            {
                (await client.GetAsync(Gone)).Dispose();
            }
            Assert.ThrowsAsync(Is.InstanceOf<OperationCanceledException>(), () => client.GetAsync(Gone));
            Assert.That(guard.StopReason, Is.EqualTo(StopReason.Refused));
        }

        [Test]
        public async Task AnAnswerBetweenRefusalsStartsTheCountAgain()
        {
            var (client, guard, server, _) = Create();
            const string refused = "https://replay.pokemonshowdown.com/gen9ou-3.json";
            server.Answer(refused, HttpStatusCode.Forbidden);

            for (var round = 0; round < 3; round++)
            {
                for (var i = 1; i < ReplayRequestGuard.RefusalsToStop; i++)
                {
                    (await client.GetAsync(refused)).Dispose();
                }
                (await client.GetAsync(round % 2 == 0 ? Present : Gone)).Dispose();
            }

            Assert.That(guard.StopReason, Is.EqualTo(StopReason.None));
        }

        [Test]
        public async Task TheRequestOverTheLimitStopsTheRun()
        {
            var (client, guard, server, _) = Create(maxRequests: 2);

            (await client.GetAsync(Present)).Dispose();
            (await client.GetAsync(Gone)).Dispose();
            // Known to be gone: answered without a request, so it does not count.
            (await client.GetAsync(Gone)).Dispose();
            Assert.ThrowsAsync(Is.InstanceOf<OperationCanceledException>(), () => client.GetAsync(Present));

            Assert.That(guard.StopReason, Is.EqualTo(StopReason.RequestLimit));
            Assert.That(guard.Sent, Is.EqualTo(2));
            Assert.That(server.Requests, Has.Count.EqualTo(2));
        }
    }
}
