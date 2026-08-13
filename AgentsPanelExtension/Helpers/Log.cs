using System;
using System.Diagnostics;

namespace AgentsPanelExtension;

// Lightweight tagged logger.
//
// All three levels write to the Debug stream (OutputDebugString) — watch them live in the VS
// Output window or Sysinternals DebugView. They are all marked [Conditional("DEBUG")], so in a
// Release build the calls (and their argument evaluation) are compiled away entirely: a shipped
// MSIX emits nothing here and has NO telemetry channel — no overhead, and nothing leaves the
// machine. Format: "[time] [LEVEL] [Tag] message". The <tag> is a component name (e.g. "Claude",
// "Repository"). NEVER pass secrets (the OAuth token) into a message.
internal static class Log
{
    [Conditional("DEBUG")]
    public static void Info(string tag, string message) => Write("INFO", tag, message);

    [Conditional("DEBUG")]
    public static void Warn(string tag, string message) => Write("WARN", tag, message);

    [Conditional("DEBUG")]
    public static void Error(string tag, string message, Exception? ex = null) =>
        Write("ERROR", tag, ex is null ? message : $"{message} — {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");

    [Conditional("DEBUG")]
    private static void Write(string level, string tag, string message) =>
        Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [{level}] [{tag}] {message}");
}
