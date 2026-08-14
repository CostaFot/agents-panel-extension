using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace AgentsPanelExtension;

// Sums the last 24 hours' token usage AND gateway spend from opencode's local SQLite DB
// (<data dir>\opencode.db) — the Data/OpenCode sibling of the token log readers, with one twist:
// opencode records per-message COST in USD, so this reader also returns the rolling-24h spend that
// v1 surfaces in place of the (nonexistent) balance API. No network, no auth. Same never-throw
// contract (any failure degrades to null with a Warn).
//
// Source of truth is the `message` table: assistant rows carry a data JSON with
// providerID / cost (USD) / tokens { input, output, reasoning, cache: { read, write } } and a
// time_created in epoch MILLISECONDS (all verified live 2026-08). The `session` table's counters
// are deliberately NOT used — they're cumulative per session, so they can't be windowed by time.
// Spend counts only providerID == "opencode" (the Zen gateway — money that actually leaves the
// account); token counts cover ALL opencode activity, matching the other readers' "what this agent
// did on this machine" semantics. Extraction happens inside SQLite via json_extract, so message
// CONTENT never leaves the database.
//
// The DB is WAL-mode and live while opencode runs, hence the read strategy: a read-only open works
// both mid-session (the -shm exists) and after a clean exit (WAL checkpointed away); the one edge
// where it can fail is a crash-orphaned -wal without -shm, where read-only recovery is impossible —
// then fall back to copying the db+wal+shm to temp and reading the copy. Pooling is off so no file
// handle outlives a poll.
//
// Static (unlike the tailing token log readers, which hold per-file offsets): each poll is one
// self-contained windowed query, so there's no state to keep between polls.
internal static class OpenCodeUsageReader
{
    private const string Tag = "OpenCodeUsage";

    internal readonly record struct Result(DomainTokenStats Tokens, decimal GatewaySpend);

    public static Result? ReadLast24Hours()
    {
        var dbPath = Path.Combine(OpenCodeCredentialsReader.DataDirectory, "opencode.db");
        try
        {
            if (!File.Exists(dbPath))
            {
                Log.Info(Tag, "opencode.db not found — no local usage");
                return null;
            }

            try
            {
                return Query(dbPath);
            }
            catch (SqliteException ex)
            {
                Log.Warn(Tag, $"read-only query failed ({ex.SqliteErrorCode}) — retrying on a temp copy");
                return QueryOnCopy(dbPath);
            }
        }
        catch (Exception ex)
        {
            Log.Warn(Tag, $"db read failed — no usage stats this poll ({ex.GetType().Name}: {ex.Message})");
            return null;
        }
    }

    private static Result Query(string dbPath)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ConnectionString;

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandTimeout = 2; // maps to the SQLite busy timeout — a live writer shouldn't stall a poll
        command.CommandText =
            """
            SELECT json_extract(data, '$.providerID'),
                   json_extract(data, '$.cost'),
                   json_extract(data, '$.tokens.input'),
                   json_extract(data, '$.tokens.output'),
                   json_extract(data, '$.tokens.reasoning'),
                   json_extract(data, '$.tokens.cache.read'),
                   json_extract(data, '$.tokens.cache.write')
            FROM message
            WHERE time_created >= @cutoffMs
              AND json_extract(data, '$.role') = 'assistant'
            """;
        command.Parameters.AddWithValue(
            "@cutoffMs", DateTimeOffset.UtcNow.AddHours(-24).ToUnixTimeMilliseconds());

        long input = 0, output = 0, cacheRead = 0, cacheWrite = 0;
        double gatewaySpend = 0;
        var rows = 0;

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows++;
            input += GetLong(reader, 2);
            // opencode reports reasoning separately from output; our four-counter model folds it
            // into output (it's generated-token volume either way).
            output += GetLong(reader, 3) + GetLong(reader, 4);
            cacheRead += GetLong(reader, 5);
            cacheWrite += GetLong(reader, 6);

            if (!reader.IsDBNull(0) && reader.GetString(0) == "opencode" && !reader.IsDBNull(1))
                gatewaySpend += reader.GetDouble(1);
        }

        Log.Info(Tag, $"summed {rows} assistant message(s) in the last 24h");
        return new Result(
            new DomainTokenStats(input, output, cacheRead, cacheWrite),
            (decimal)gatewaySpend);
    }

    private static long GetLong(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? 0 : reader.GetInt64(ordinal);

    // Crash-orphaned-WAL fallback: snapshot the three files into temp and query the copy. A torn
    // copy under a live writer just throws into the caller's catch — null this poll, retried next.
    private static Result QueryOnCopy(string dbPath)
    {
        var copyDir = Path.Combine(Path.GetTempPath(), "AgentsPanelExtension");
        Directory.CreateDirectory(copyDir);
        var copyPath = Path.Combine(copyDir, "opencode.db");

        File.Copy(dbPath, copyPath, overwrite: true);
        CopySidecar(dbPath, copyPath, "-wal");
        CopySidecar(dbPath, copyPath, "-shm");

        return Query(copyPath);
    }

    private static void CopySidecar(string dbPath, string copyPath, string suffix)
    {
        var source = dbPath + suffix;
        var target = copyPath + suffix;
        if (File.Exists(source))
            File.Copy(source, target, overwrite: true);
        else
            File.Delete(target); // never pair a fresh db copy with a stale sidecar from a prior poll
    }
}
