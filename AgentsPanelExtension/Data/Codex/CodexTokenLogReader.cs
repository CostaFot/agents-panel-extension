using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace AgentsPanelExtension;

// Sums the last 24 hours' token usage from Codex's own rollout logs
// (%CODEX_HOME%\sessions\yyyy\mm\dd\rollout-*.jsonl) — the Data/Codex sibling of
// ClaudeTokenLogReader: no network, no auth, works even when the OAuth token is expired. Same
// incremental tailing (per-file byte offset, only newly appended bytes parsed per poll, shrunken
// files reparsed from zero), same never-throw contract (any failure degrades to null with a Warn).
//
// The parse model differs from Claude's because the data does: token_count events carry a CUMULATIVE
// per-session total, so the reader diffs consecutive events into per-event deltas stamped with the
// event's own timestamp, then sums the deltas inside the rolling window. That keeps a session that
// spans the window edge honest (only its in-window turns count) and works on log generations that
// lack the per-turn last_token_usage field. A totals RESET mid-file (shouldn't happen; belt and
// braces) is treated as a fresh baseline rather than a negative delta.
//
// One instance per provider; the repository single-flights refreshes, so no internal locking.
internal sealed class CodexTokenLogReader
{
    private const string Tag = "CodexTokens";

    // Same CODEX_HOME resolution as CodexCredentialsReader — read per call so an env change applies
    // without a reload.
    private static string SessionsRoot
    {
        get
        {
            var home = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (string.IsNullOrWhiteSpace(home))
                home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
            return Path.Combine(home, "sessions");
        }
    }

    // One rollout's tail state: the running cumulative totals (diff baseline) plus the per-event
    // deltas seen so far. Deltas older than the window are pruned as it slides.
    private sealed class FileState
    {
        public long Offset;
        public TokenCounts PrevTotals;
        public readonly List<TokenDelta> Deltas = [];
    }

    private readonly record struct TokenCounts(long Input, long Output, long CacheRead, long CacheWrite);

    private readonly record struct TokenDelta(
        DateTimeOffset Timestamp, long Input, long Output, long CacheRead, long CacheWrite);

    private readonly Dictionary<string, FileState> _files = new(StringComparer.OrdinalIgnoreCase);

    public DomainTokenStats? ReadLast24Hours()
    {
        try
        {
            var root = new DirectoryInfo(SessionsRoot);
            if (!root.Exists)
            {
                Log.Info(Tag, "sessions directory not found — no local rollouts");
                return null;
            }

            var windowStart = DateTimeOffset.UtcNow.AddHours(-24);

            var parsed = 0;
            foreach (var file in root.EnumerateFiles("*.jsonl", SearchOption.AllDirectories))
            {
                if (file.LastWriteTimeUtc < windowStart.UtcDateTime)
                    continue;
                TailFile(file, windowStart);
                parsed++;
            }

            PruneStale(windowStart);

            long input = 0, output = 0, cacheRead = 0, cacheWrite = 0;
            var events = 0;
            foreach (var state in _files.Values)
            {
                foreach (var delta in state.Deltas)
                {
                    if (delta.Timestamp < windowStart)
                        continue;
                    input += delta.Input;
                    output += delta.Output;
                    cacheRead += delta.CacheRead;
                    cacheWrite += delta.CacheWrite;
                    events++;
                }
            }

            Log.Info(Tag, $"tailed {parsed} rollout(s): {events} token event(s) in the last 24h");
            return new DomainTokenStats(input, output, cacheRead, cacheWrite);
        }
        catch (Exception ex)
        {
            Log.Warn(Tag, $"rollout read failed — no token stats this poll ({ex.GetType().Name}: {ex.Message})");
            return null;
        }
    }

    private void TailFile(FileInfo file, DateTimeOffset windowStart)
    {
        if (!_files.TryGetValue(file.FullName, out var state))
            _files[file.FullName] = state = new FileState();

        try
        {
            // Codex holds active rollouts open for append — share everything.
            using var stream = new FileStream(
                file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            if (stream.Length < state.Offset)
            {
                Log.Info(Tag, $"rollout shrank — reparsing from zero: {file.Name}");
                state.Offset = 0;
                state.PrevTotals = default;
                state.Deltas.Clear();
            }

            if (stream.Length == state.Offset)
            {
                state.Deltas.RemoveAll(d => d.Timestamp < windowStart);
                return;
            }

            stream.Position = state.Offset;
            var buffer = new byte[stream.Length - state.Offset];
            stream.ReadExactly(buffer);

            // Only complete (newline-terminated) lines are consumed; a trailing partial line stays
            // un-consumed and is retried when the writer finishes it.
            var lineStart = 0;
            for (var i = 0; i < buffer.Length; i++)
            {
                if (buffer[i] != (byte)'\n')
                    continue;
                var line = buffer.AsSpan(lineStart, i - lineStart);
                if (line.Length > 0 && line[^1] == (byte)'\r')
                    line = line[..^1];
                ParseLine(line, state);
                lineStart = i + 1;
            }

            state.Offset += lineStart;

            // A long-running rollout keeps appending inside the window while its old deltas age out —
            // prune so a file's list stays bounded to ~one day of turns.
            state.Deltas.RemoveAll(d => d.Timestamp < windowStart);
        }
        catch (Exception ex)
        {
            Log.Warn(Tag, $"failed tailing '{file.Name}' — skipped this poll ({ex.GetType().Name})");
        }
    }

    // One JSONL line → at most one delta. Non-token_count events and malformed JSON are consumed
    // silently — rollouts legitimately contain many line shapes.
    private static void ParseLine(ReadOnlySpan<byte> line, FileState state)
    {
        if (line.IsEmpty)
            return;

        ApiCodexRolloutLineDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize(line, CodexJsonContext.Default.ApiCodexRolloutLineDto);
        }
        catch (JsonException)
        {
            return;
        }

        if (dto is not { Payload: { Type: "token_count", Info.TotalTokenUsage: { } raw } })
            return;
        if (!DateTimeOffset.TryParse(dto.Timestamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
            return;

        // Cumulative totals → this event's delta. input_tokens includes the cached subset, so fresh
        // input is the difference (clamped — the fields come from an undocumented log format).
        var cacheRead = raw.CachedInputTokens ?? 0;
        var totals = new TokenCounts(
            Math.Max(0, (raw.InputTokens ?? 0) - cacheRead),
            raw.OutputTokens ?? 0,
            cacheRead,
            raw.CacheWriteInputTokens ?? 0);

        // A component going backwards means the session's counter reset — rebaseline on the current
        // totals instead of recording a negative delta.
        var prev = state.PrevTotals;
        var reset = totals.Input < prev.Input || totals.Output < prev.Output
            || totals.CacheRead < prev.CacheRead || totals.CacheWrite < prev.CacheWrite;
        var delta = reset
            ? totals
            : new TokenCounts(
                totals.Input - prev.Input,
                totals.Output - prev.Output,
                totals.CacheRead - prev.CacheRead,
                totals.CacheWrite - prev.CacheWrite);
        state.PrevTotals = totals;

        if (delta is { Input: 0, Output: 0, CacheRead: 0, CacheWrite: 0 })
            return; // heartbeat/no-op event — nothing to record

        state.Deltas.Add(new TokenDelta(timestamp, delta.Input, delta.Output, delta.CacheRead, delta.CacheWrite));
    }

    private void PruneStale(DateTimeOffset windowStart)
    {
        List<string>? stale = null;
        foreach (var (path, _) in _files)
        {
            // Missing files prune too (File.GetLastWriteTimeUtc returns a sentinel far in the past).
            if (File.GetLastWriteTimeUtc(path) < windowStart.UtcDateTime)
                (stale ??= []).Add(path);
        }

        if (stale is null)
            return;
        foreach (var path in stale)
            _files.Remove(path);
    }
}
