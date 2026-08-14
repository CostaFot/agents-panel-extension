using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AgentsPanelExtension;

// Codex (ChatGPT subscription) usage via the ChatGPT backend's usage endpoint — the same numbers the
// Codex CLI's /status screen shows, authenticated with the token Codex already holds locally
// (CodexCredentialsReader).
//
// ⚠️ The endpoint is UNDOCUMENTED and unsupported for third parties, same tier as the Claude source:
// it can change shape (paths, field names, and plan_type values have all churned across 2025–2026),
// tighten rate limits, or vanish without notice. Everything endpoint-specific therefore lives in this
// Data/Codex/ folder behind IAgentUsageProvider, so a replacement source is a drop-in sibling class.
// Reference implementations: openai/codex codex-rs/backend-client + steipete/CodexBar.
//
// Failure mapping (degrade-and-log, never throw): no/empty credentials → NotConfigured; expired token
// or 401/403 → TokenExpired; surviving 429 → RateLimited; anything else → Error. The repository's
// keep-last-good merge holds the previous numbers under the new status.
internal sealed class CodexUsageProvider : IAgentUsageProvider
{
    private const string Tag = "Codex";

    // ChatGPT-auth path style ("wham" is the Codex backend's internal name); the {base}/api/codex/*
    // style is for API-key auth, which this provider doesn't use.
    private const string UsageEndpoint = "https://chatgpt.com/backend-api/wham/usage";

    // Honest self-identification (deliberate decision: no client spoofing). One place to bump.
    private const string UserAgent = "agents-panel/0.1.0";

    // One client for the process. Headers that never change ride on it; the Authorization and
    // account-id headers are per-request because credentials are re-read from disk on every fetch.
    private static readonly HttpClient Http = CreateClient();

    public string Id => "codex";

    public string DisplayName => "Codex";

    // Always in the active set, rendering "Sign in" when credentials are missing rather than
    // disappearing (see IAgentUsageProvider).
    public bool IsAvailable => true;

    public async Task<DomainUsageSnapshot> GetUsageAsync(CancellationToken ct = default)
    {
        // Read credentials at call time — a sign-in/sign-out applies on the next poll, no reload.
        var credentials = CodexCredentialsReader.Read();
        if (credentials is null)
            return DomainUsageSnapshot.Failed(Id, DisplayName, UsageStatus.NotConfigured);

        var planLabel = credentials.PlanTypeHint;
        if (credentials.IsExpired)
        {
            Log.Warn(Tag, "access token expired — not refreshing (by design); user must use Codex");
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
                    // Disambiguates accounts that belong to multiple ChatGPT workspaces; the CLI
                    // sends it whenever it knows the id.
                    if (!string.IsNullOrWhiteSpace(credentials.AccountId))
                        request.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", credentials.AccountId);
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
                stream, CodexJsonContext.Default.ApiCodexUsageDto, ct).ConfigureAwait(false);
            if (dto is null)
            {
                Log.Warn(Tag, "usage response deserialized to null");
                return DomainUsageSnapshot.Failed(Id, DisplayName, UsageStatus.Error) with { PlanLabel = planLabel };
            }

            // The response's plan_type is fresher than the JWT claim (plan changes show up here first).
            var snapshot = new DomainUsageSnapshot(
                Id, DisplayName, UsageStatus.Ok, MapWindows(dto, DateTimeOffset.UtcNow),
                dto.PlanType ?? planLabel, DateTimeOffset.UtcNow);
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
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static IReadOnlyList<DomainUsageWindow> MapWindows(ApiCodexUsageDto dto, DateTimeOffset now)
    {
        var windows = new List<DomainUsageWindow>(3);

        AddWindow(windows, dto.RateLimit?.PrimaryWindow, "primary", now, fallbackKind: UsageWindowKind.Session);
        AddWindow(windows, dto.RateLimit?.SecondaryWindow, "secondary", now, fallbackKind: UsageWindowKind.Week);

        // Model/feature-scoped extras render like Claude's per-model weekly windows: qualified rows
        // hidden behind the ShowModelWindows toggle. Null on the plans observed so far — defensive.
        if (dto.AdditionalRateLimits is { } extras)
        {
            foreach (var extra in extras)
            {
                var name = extra?.LimitName;
                var window = extra?.RateLimit?.PrimaryWindow;
                if (window is null)
                    continue;
                windows.Add(new DomainUsageWindow(
                    $"extra:{name ?? "limit"}", UsageWindowKind.ModelWeek, Clamp(window.UsedPercent),
                    ResetsAt(window, now), name));
            }
        }

        return windows;
    }

    // The window LABEL comes from its duration, not its primary/secondary slot: plans differ in what
    // the slots hold (Plus/Pro: 5h primary + weekly secondary; Go: one 30-day primary, no secondary).
    // The slot only breaks a tie when the endpoint omits the duration.
    private static void AddWindow(
        List<DomainUsageWindow> windows, ApiCodexWindowDto? dto, string id, DateTimeOffset now, UsageWindowKind fallbackKind)
    {
        if (dto is null)
            return;
        var kind = dto.LimitWindowSeconds switch
        {
            null => fallbackKind,
            <= 6 * 3600 => UsageWindowKind.Session,       // ~5-hour rolling window
            <= 8 * 86400 => UsageWindowKind.Week,         // ~7-day rolling window
            _ => UsageWindowKind.Month,                   // ~30-day rolling window (Go plan)
        };
        windows.Add(new DomainUsageWindow(id, kind, Clamp(dto.UsedPercent), ResetsAt(dto, now)));
    }

    private static double Clamp(double? usedPercent) => Math.Clamp(usedPercent ?? 0, 0, 100);

    // Both reset encodings exist in the wild: reset_at (epoch seconds, newer builds) is authoritative;
    // reset_after_seconds is the older relative form, anchored to "now" on our side. Neither → null.
    private static DateTimeOffset? ResetsAt(ApiCodexWindowDto dto, DateTimeOffset now) =>
        dto.ResetAt is { } at and > 0
            ? DateTimeOffset.FromUnixTimeSeconds(at)
            : dto.ResetAfterSeconds is { } after and >= 0
                ? now.AddSeconds(after)
                : null;
}
