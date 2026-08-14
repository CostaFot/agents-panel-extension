using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AgentsPanelExtension;

// Claude usage via the OAuth usage endpoint — the same numbers Claude Code's /usage screen shows,
// authenticated with the token Claude Code already holds locally (ClaudeCredentialsReader).
//
// ⚠️ The endpoint is UNDOCUMENTED and unsupported for third parties (see notes/ in the repo root for
// the ToS discussion): it can change shape, tighten rate limits, or vanish without notice. Everything
// endpoint-specific therefore lives in this Data/Claude/ folder behind IAgentUsageProvider, so a future
// replacement source (e.g. a Claude Code statusline-cache reader) is a drop-in sibling class.
//
// Failure mapping (degrade-and-log, never throw): no/empty credentials → NotConfigured; expired token
// or 401/403 → TokenExpired; surviving 429 → RateLimited; anything else → Error. The repository's
// keep-last-good merge holds the previous numbers under the new status.
internal sealed class ClaudeUsageProvider : IAgentUsageProvider
{
    private const string Tag = "Claude";

    private const string UsageEndpoint = "https://api.anthropic.com/api/oauth/usage";

    // The beta header the endpoint requires alongside a consumer OAuth token.
    private const string AnthropicBeta = "oauth-2025-04-20";

    // One client for the process. Headers that never change ride on it; the Authorization header is
    // per-request because the token is re-read from disk on every fetch.
    private static readonly HttpClient Http = CreateClient();

    // Tails Claude Code's local transcripts for the last 24h's token counts. Stateful (per-file byte
    // offsets) — must live as long as the provider so increments stay cheap across polls.
    private readonly ClaudeTokenLogReader _tokenLogReader = new();

    public string Id => "claude";

    public string DisplayName => "Claude";

    // Claude is the point of v1: always in the active set, rendering "Sign in" when credentials are
    // missing rather than disappearing (see IAgentUsageProvider).
    public bool IsAvailable => true;

    public async Task<DomainUsageSnapshot> GetUsageAsync(CancellationToken ct = default)
    {
        // Token stats come from LOCAL logs — independent of the endpoint, so they ride on every
        // outcome, including NotConfigured/TokenExpired (the row still works while signed out).
        var snapshot = await FetchQuotaAsync(ct).ConfigureAwait(false);
        return snapshot with { TokenStats = _tokenLogReader.ReadLast24Hours() };
    }

    private async Task<DomainUsageSnapshot> FetchQuotaAsync(CancellationToken ct)
    {
        // Read credentials at call time — a sign-in/sign-out applies on the next poll, no reload.
        var credentials = ClaudeCredentialsReader.Read();
        if (credentials is null)
            return DomainUsageSnapshot.Failed(Id, DisplayName, UsageStatus.NotConfigured);

        var planLabel = PlanLabel(credentials);
        if (credentials.IsExpired)
        {
            Log.Warn(Tag, "access token expired — not refreshing (by design); user must open Claude Code");
            return DomainUsageSnapshot.Failed(Id, DisplayName, UsageStatus.TokenExpired) with { PlanLabel = planLabel };
        }

        try
        {
            // Thunk builds a FRESH request per attempt (HttpRequestMessage is single-use).
            // NEVER log the request headers — the Authorization header carries the token.
            using var response = await HttpRetry.SendAsync(
                c =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
                    request.Headers.TryAddWithoutValidation("anthropic-beta", AnthropicBeta);
                    return Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, c);
                },
                Tag,
                ct).ConfigureAwait(false);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                Log.Warn(Tag, $"usage request rejected ({(int)response.StatusCode}) — treating as expired/invalid token");
                return DomainUsageSnapshot.Failed(Id, DisplayName, UsageStatus.TokenExpired) with { PlanLabel = planLabel };
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return DomainUsageSnapshot.Failed(Id, DisplayName, UsageStatus.RateLimited) with { PlanLabel = planLabel };

            if (!response.IsSuccessStatusCode)
            {
                Log.Warn(Tag, $"usage request failed with {(int)response.StatusCode}");
                return DomainUsageSnapshot.Failed(Id, DisplayName, UsageStatus.Error) with { PlanLabel = planLabel };
            }

            var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var dto = await JsonSerializer.DeserializeAsync(
                stream, ClaudeJsonContext.Default.ApiClaudeUsageDto, ct).ConfigureAwait(false);
            if (dto is null)
            {
                Log.Warn(Tag, "usage response deserialized to null");
                return DomainUsageSnapshot.Failed(Id, DisplayName, UsageStatus.Error) with { PlanLabel = planLabel };
            }

            var snapshot = new DomainUsageSnapshot(
                Id, DisplayName, UsageStatus.Ok, MapWindows(dto), planLabel, DateTimeOffset.UtcNow);
            Log.Info(Tag, $"usage fetched ({snapshot.Windows.Count} window(s))");
            return snapshot;
        }
        catch (OperationCanceledException)
        {
            throw; // cancellation is not a failure — let it propagate
        }
        catch (Exception ex)
        {
            Log.Error(Tag, "usage fetch failed", ex);
            return DomainUsageSnapshot.Failed(Id, DisplayName, UsageStatus.Error) with { PlanLabel = planLabel };
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", AppInfo.UserAgent);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static IReadOnlyList<DomainUsageWindow> MapWindows(ApiClaudeUsageDto dto)
    {
        var windows = new List<DomainUsageWindow>(5);

        if (dto.Limits is { Count: > 0 } limits)
        {
            // Preferred source: the generic limits array — the only place per-model weekly limits live
            // on current accounts (the named seven_day_opus/sonnet fields are null there).
            foreach (var limit in limits)
                AddLimit(windows, limit);
        }
        else
        {
            // Legacy fallback: the named windows.
            AddWindow(windows, dto.FiveHour, "five_hour", UsageWindowKind.Session);
            AddWindow(windows, dto.SevenDay, "seven_day", UsageWindowKind.Week);
            AddWindow(windows, dto.SevenDayOpus, "seven_day_opus", UsageWindowKind.ModelWeek, "Opus");
            AddWindow(windows, dto.SevenDaySonnet, "seven_day_sonnet", UsageWindowKind.ModelWeek, "Sonnet");
        }

        // Extra usage only renders when the account has it enabled; it has no reset time (monthly
        // credit balance), and carries Used/Limit for the "$x of $y" display.
        if (dto.ExtraUsage is { IsEnabled: true } extra)
        {
            windows.Add(new DomainUsageWindow(
                "extra_usage", UsageWindowKind.ExtraUsage, Clamp(extra.Utilization),
                ResetsAt: null, Qualifier: null, Used: extra.UsedCredits, Limit: extra.MonthlyLimit));
        }

        return windows;
    }

    // One entry of the generic limits array → one window. Unknown kinds are skipped (logged) rather
    // than guessed at — an undocumented endpoint can grow new kinds at any time.
    private static void AddLimit(List<DomainUsageWindow> windows, ApiClaudeLimitDto limit)
    {
        switch (limit.Kind)
        {
            case "session":
                windows.Add(new DomainUsageWindow(
                    "session", UsageWindowKind.Session, Clamp(limit.Percent), ParseResetsAt(limit.ResetsAt)));
                break;
            case "weekly_all":
                windows.Add(new DomainUsageWindow(
                    "weekly_all", UsageWindowKind.Week, Clamp(limit.Percent), ParseResetsAt(limit.ResetsAt)));
                break;
            case "weekly_scoped":
                var qualifier = limit.Scope?.Model?.DisplayName;
                windows.Add(new DomainUsageWindow(
                    $"weekly_scoped:{qualifier ?? "model"}", UsageWindowKind.ModelWeek, Clamp(limit.Percent),
                    ParseResetsAt(limit.ResetsAt), qualifier));
                break;
            default:
                Log.Info(Tag, $"skipping unknown limit kind '{limit.Kind}'");
                break;
        }
    }

    // A null window (e.g. seven_day_opus on plans without per-model limits) is simply omitted.
    private static void AddWindow(
        List<DomainUsageWindow> windows, ApiClaudeWindowDto? dto, string id, UsageWindowKind kind, string? qualifier = null)
    {
        if (dto is null)
            return;
        windows.Add(new DomainUsageWindow(id, kind, Clamp(dto.Utilization), ParseResetsAt(dto.ResetsAt), qualifier));
    }

    private static double Clamp(double? utilization) => Math.Clamp(utilization ?? 0, 0, 100);

    // ISO-8601 UTC per observed responses; anything unparseable degrades to null (no reset shown).
    private static DateTimeOffset? ParseResetsAt(string? raw) =>
        DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;

    // Raw-ish plan metadata from the credentials file (e.g. "max · max_20x"); UiUsage prettifies for
    // display. Null when neither field is present.
    private static string? PlanLabel(ClaudeCredentialsReader.ClaudeCredentials credentials)
    {
        var type = credentials.SubscriptionType?.Trim();
        var tier = credentials.RateLimitTier?.Trim();
        return (string.IsNullOrEmpty(type), string.IsNullOrEmpty(tier)) switch
        {
            (true, true) => null,
            (false, true) => type,
            (true, false) => tier,
            _ => string.Equals(type, tier, StringComparison.OrdinalIgnoreCase) ? type : $"{type} · {tier}",
        };
    }
}
