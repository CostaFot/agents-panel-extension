using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace AgentsPanelExtension;

// Sums the last 24 hours' token usage from Claude Code's own local transcripts
// (%USERPROFILE%\.claude\projects\<slug>\<session>.jsonl) — no network, no auth, works even when the
// OAuth token is expired. This is the "activity" companion to the quota endpoint: machine-local by
// nature (usage on other devices/web never appears here) and deliberately NOT mapped to quota %.
//
// Transcripts are append-only JSONL, so the reader tails incrementally: per file it remembers the byte
// offset after the last fully-parsed line plus the per-message usage seen so far, and each poll only
// parses bytes appended since. Active files reach tens of MB — re-reading them every 3-minute poll is
// the failure mode this cache exists to avoid. A file that SHRANK (rotation/rewrite) is reparsed from
// zero. Streaming rewrites repeat a message id across lines (verified live), so usage is keyed by
// message id with last-occurrence-wins, then summed.
//
// Never throws: any failure degrades to null (provider shows no token row) with a Warn. One instance
// per provider; the repository single-flights refreshes, so no internal locking is needed.
internal sealed class ClaudeTokenLogReader
{
    private const string Tag = "ClaudeTokens";

    private static string ProjectsRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");

    // One transcript's tail state. Messages holds every usage-bearing line seen in the file — the
    // rolling-window filter happens at sum time, not parse time, so entries age out of the total
    // naturally as the window slides without any reparsing.
    private sealed class FileState
    {
        public long Offset;
        public readonly Dictionary<string, MessageUsage> Messages = new(StringComparer.Ordinal);
    }

    private readonly record struct MessageUsage(
        DateTimeOffset Timestamp, long Input, long Output, long CacheRead, long CacheWrite);

    private readonly Dictionary<string, FileState> _files = new(StringComparer.OrdinalIgnoreCase);

    public DomainTokenStats? ReadLast24Hours()
    {
        try
        {
            var root = new DirectoryInfo(ProjectsRoot);
            if (!root.Exists)
            {
                Log.Info(Tag, "projects directory not found — no local transcripts");
                return null;
            }

            // Rolling 24-hour window (not calendar-day: an "as of midnight" reset would zero the row
            // right when a late-night session is in full swing). Only files touched inside the window
            // can contain qualifying lines, and only lines stamped inside it count.
            var windowStart = DateTimeOffset.UtcNow.AddHours(-24);

            var parsed = 0;
            foreach (var file in root.EnumerateFiles("*.jsonl", SearchOption.AllDirectories))
            {
                if (file.LastWriteTimeUtc < windowStart.UtcDateTime)
                    continue;
                TailFile(file);
                parsed++;
            }

            // Drop tail state for files that can no longer contribute (untouched for 24h) so the
            // cache doesn't grow a new entry per session forever.
            PruneStale(windowStart);

            long input = 0, output = 0, cacheRead = 0, cacheWrite = 0;
            var messages = 0;
            foreach (var state in _files.Values)
            {
                foreach (var usage in state.Messages.Values)
                {
                    if (usage.Timestamp < windowStart)
                        continue;
                    input += usage.Input;
                    output += usage.Output;
                    cacheRead += usage.CacheRead;
                    cacheWrite += usage.CacheWrite;
                    messages++;
                }
            }

            Log.Info(Tag, $"tailed {parsed} file(s): {messages} message(s) in the last 24h");
            return new DomainTokenStats(input, output, cacheRead, cacheWrite);
        }
        catch (Exception ex)
        {
            Log.Warn(Tag, $"token log read failed — no token stats this poll ({ex.GetType().Name}: {ex.Message})");
            return null;
        }
    }

    // Parse the bytes appended since the last poll. A per-file failure (sharing violation, torn write)
    // only skips THAT file this poll — the saved offset makes the next attempt cheap.
    private void TailFile(FileInfo file)
    {
        if (!_files.TryGetValue(file.FullName, out var state))
            _files[file.FullName] = state = new FileState();

        try
        {
            // Claude Code holds these open for append — share everything.
            using var stream = new FileStream(
                file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            if (stream.Length < state.Offset)
            {
                Log.Info(Tag, $"transcript shrank — reparsing from zero: {file.Name}");
                state.Offset = 0;
                state.Messages.Clear();
            }

            if (stream.Length == state.Offset)
                return;

            stream.Position = state.Offset;
            var buffer = new byte[stream.Length - state.Offset];
            stream.ReadExactly(buffer);

            // Only complete (newline-terminated) lines are consumed; a trailing partial line stays
            // un-consumed (offset not advanced past it) and is retried when the writer finishes it.
            var lineStart = 0;
            for (var i = 0; i < buffer.Length; i++)
            {
                if (buffer[i] != (byte)'\n')
                    continue;
                var line = buffer.AsSpan(lineStart, i - lineStart);
                if (line.Length > 0 && line[^1] == (byte)'\r')
                    line = line[..^1];
                ParseLine(line, state.Messages);
                lineStart = i + 1;
            }

            state.Offset += lineStart;
        }
        catch (Exception ex)
        {
            Log.Warn(Tag, $"failed tailing '{file.Name}' — skipped this poll ({ex.GetType().Name})");
        }
    }

    // One JSONL line → at most one Messages entry. Non-assistant lines, usage-less lines, and
    // malformed JSON are all consumed silently — transcripts legitimately contain many line shapes.
    private static void ParseLine(ReadOnlySpan<byte> line, Dictionary<string, MessageUsage> messages)
    {
        if (line.IsEmpty)
            return;

        ApiClaudeTranscriptLineDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize(line, ClaudeJsonContext.Default.ApiClaudeTranscriptLineDto);
        }
        catch (JsonException)
        {
            return;
        }

        if (dto is not { Type: "assistant", Message: { Id: { Length: > 0 } id, Usage: { } usage } })
            return;
        if (!DateTimeOffset.TryParse(dto.Timestamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
            return;

        // Last occurrence wins — a streaming rewrite of the same message replaces its earlier partial.
        messages[id] = new MessageUsage(
            timestamp,
            usage.InputTokens ?? 0,
            usage.OutputTokens ?? 0,
            usage.CacheReadInputTokens ?? 0,
            usage.CacheCreationInputTokens ?? 0);
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
