using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AgentsPanelExtension;

// Shared back-off for rate-limited HTTP calls. Wraps a re-issuable request (a thunk, because each retry
// must send a FRESH HttpRequestMessage) and, on a 429 Too Many Requests, waits then retries a few times
// before giving up. The final response (a success, a non-429 error, or a surviving 429) is returned to
// the caller, which disposes it via `using` and inspects status as usual. Copied from MarketExtension's
// HttpRetry minus its RateLimitSignal coupling — here a surviving 429 is reported to the UI through the
// snapshot's UsageStatus.RateLimited instead of a global banner flag.
//
// Why so few retries / a short cap: the Claude usage endpoint's throttling windows are long (it has no
// published limits and community reports show 429s that persist for minutes), so hammering inside one
// refresh just burns goodwill. We honor a short Retry-After when the server sends one, otherwise back
// off 1s then 2s, and bail straight to the caller if a (Retry-After) wait would exceed MaxDelay. The
// next poll tick recovers once the window rolls over; meanwhile the repository's keep-last-good merge
// holds the last numbers and the UI explains the staleness.
internal static class HttpRetry
{
    private const int MaxAttempts = 3;                                   // initial try + up to 2 retries (1s, 2s)
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(8); // don't block a refresh longer than this

    public static async Task<HttpResponseMessage> SendAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> send, string tag, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(send);

        for (var attempt = 1; ; attempt++)
        {
            var response = await send(ct).ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.TooManyRequests)
                return response;

            // 429. Work out whether another attempt is worth it before disposing this response.
            var delay = RetryAfter(response) ?? TimeSpan.FromSeconds(1 << (attempt - 1)); // 1s, 2s, 4s, ...
            if (attempt >= MaxAttempts || delay > MaxDelay)
            {
                Log.Warn(tag, $"429 — giving up after {attempt} attempt(s) (next wait {delay.TotalSeconds:F0}s) — rate-limited");
                return response; // hand back the 429 so the caller degrades
            }

            response.Dispose(); // discard this 429 before re-issuing
            Log.Warn(tag, $"429 — backing off {delay.TotalSeconds:F0}s before retry {attempt + 1}/{MaxAttempts}");
            await Task.Delay(delay, ct).ConfigureAwait(false);
        }
    }

    // The server's Retry-After, if present and sane: a delta (seconds) directly, or an HTTP date
    // converted to a wait from now. Null when absent so the caller falls back to exponential back-off.
    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter is null)
            return null;

        if (retryAfter.Delta is { } delta && delta > TimeSpan.Zero)
            return delta;

        if (retryAfter.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : null;
        }

        return null;
    }
}
