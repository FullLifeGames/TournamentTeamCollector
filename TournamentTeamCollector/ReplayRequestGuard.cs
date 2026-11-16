using Microsoft.Extensions.Caching.Distributed;
using System.Net;

namespace TournamentTeamCollector
{
    public enum StopReason
    {
        None,
        /// <summary>The request limit of the run is reached.</summary>
        RequestLimit,
        /// <summary>The replay server refused several requests in a row.</summary>
        Refused,
    }

    /// <summary>
    /// Sits in front of the polite handler for the replay downloads:
    /// <list type="bullet">
    /// <item>A replay the server answered with 404 or 410 is answered with 404 from the cache, without a request, until the entry
    /// expires. Of the replays linked in Smogon's tournaments, about 40 % are gone, and the analysis cache only keeps replays
    /// with a team, so without this every run would ask for all of them again.</item>
    /// <item>The request that would go over the limit of the run stops it.</item>
    /// <item><see cref="RefusalsToStop"/> refusals in a row (403, or 429/5xx left after the polite handler's retries) stop the run.
    /// They are not remembered as missing: a blocked crawler must not write off the replays it could not read.</item>
    /// </list>
    /// Stopping cancels <see cref="StopToken"/> and throws. The run passes that token to the scouter, which does not swallow
    /// exceptions once it is cancelled.
    /// </summary>
    public class ReplayRequestGuard : DelegatingHandler
    {
        public const int RefusalsToStop = 3;

        // The analysis cache of ShowdownReplayScouter uses "<replay>.json+v2+<player>" and "replays-<player>".
        private const string MissingKeyPrefix = "ttc:v1:missing:";
        private static readonly byte[] MissingMarker = [1];

        private readonly IDistributedCache _cache;
        private readonly int _maxRequests;
        private readonly TimeSpan _missingRecheck;
        private readonly CancellationTokenSource _stop = new();
        private int _sent;
        private int _knownMissing;
        private int _newlyMissing;
        private int _refusalsInARow;
        private int _stopReason;

        public ReplayRequestGuard(IDistributedCache cache, int maxRequests, TimeSpan missingRecheck, HttpMessageHandler innerHandler)
            : base(innerHandler)
        {
            _cache = cache;
            _maxRequests = maxRequests;
            _missingRecheck = missingRecheck;
        }

        /// <summary>Cancelled when the run has to stop; see <see cref="StopReason"/>.</summary>
        public CancellationToken StopToken => _stop.Token;

        public StopReason StopReason => (StopReason)Volatile.Read(ref _stopReason);

        public string? StopMessage { get; private set; }

        /// <summary>Requests passed on to the server (retries not counted).</summary>
        public int Sent => Volatile.Read(ref _sent);

        /// <summary>Requests answered from the cache because the server answered them with 404 before.</summary>
        public int KnownMissing => Volatile.Read(ref _knownMissing);

        /// <summary>Requests the server answered with 404 or 410 in this run.</summary>
        public int NewlyMissing => Volatile.Read(ref _newlyMissing);

        public override string ToString() =>
            $"Replay requests: {Sent} sent, {NewlyMissing} of them gone (404), {KnownMissing} known to be gone and not sent";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _stop.Token.ThrowIfCancellationRequested();

            var missingKey = request.RequestUri is Uri uri ? MissingKeyPrefix + uri.GetLeftPart(UriPartial.Path) : null;
            if (missingKey != null && await _cache.GetAsync(missingKey, cancellationToken).ConfigureAwait(false) != null)
            {
                Interlocked.Increment(ref _knownMissing);
                return new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request };
            }

            if (Interlocked.Increment(ref _sent) > _maxRequests && _maxRequests > 0)
            {
                Interlocked.Decrement(ref _sent);
                Stop(StopReason.RequestLimit, $"Request limit of {_maxRequests} reached.");
            }

            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                Interlocked.Exchange(ref _refusalsInARow, 0);
                Interlocked.Increment(ref _newlyMissing);
                if (missingKey != null)
                {
                    await _cache.SetAsync(
                        missingKey,
                        MissingMarker,
                        new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = _missingRecheck },
                        cancellationToken
                    ).ConfigureAwait(false);
                }
            }
            else if (IsRefusal(response.StatusCode))
            {
                if (Interlocked.Increment(ref _refusalsInARow) >= RefusalsToStop)
                {
                    var status = (int)response.StatusCode;
                    response.Dispose();
                    Stop(StopReason.Refused, $"The replay server refused {RefusalsToStop} requests in a row, the last with HTTP {status} for {request.RequestUri}.");
                }
            }
            else
            {
                Interlocked.Exchange(ref _refusalsInARow, 0);
            }
            return response;
        }

        private static bool IsRefusal(HttpStatusCode status) =>
            status is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests || (int)status >= 500;

        private void Stop(StopReason reason, string message)
        {
            if (Interlocked.CompareExchange(ref _stopReason, (int)reason, (int)StopReason.None) == (int)StopReason.None)
            {
                StopMessage = message;
                _stop.Cancel();
            }
            throw new OperationCanceledException(message, _stop.Token);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _stop.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
