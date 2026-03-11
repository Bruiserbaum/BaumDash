using System.Runtime.CompilerServices;

namespace WinUIAudioMixer.Services;

/// <summary>
/// Lightweight structured logger that writes to baum-crash.log next to the exe.
/// All public methods are thread-safe.
/// </summary>
internal static class CrashLogger
{
    private static readonly string LogPath =
        Path.Combine(AppContext.BaseDirectory, "baum-crash.log");

    private static readonly object _lock = new();

    // ── Public API ────────────────────────────────────────────────────────────

    public static void Info(string message,
        [CallerMemberName] string caller = "",
        [CallerFilePath]   string file   = "")
        => Write("INFO ", message, null, caller, file);

    public static void Warn(string message,
        [CallerMemberName] string caller = "",
        [CallerFilePath]   string file   = "")
        => Write("WARN ", message, null, caller, file);

    public static void Error(string message, Exception? ex = null,
        [CallerMemberName] string caller = "",
        [CallerFilePath]   string file   = "")
        => Write("ERROR", message, ex, caller, file);

    public static void Fatal(string message, Exception? ex = null,
        [CallerMemberName] string caller = "",
        [CallerFilePath]   string file   = "")
        => Write("FATAL", message, ex, caller, file);

    // ── Startup header ────────────────────────────────────────────────────────

    /// <summary>Call once at program start to write a session-start banner.</summary>
    public static void SessionStart()
    {
        var sep = new string('─', 72);
        var ver = System.Reflection.Assembly
                      .GetExecutingAssembly()
                      .GetName().Version?.ToString() ?? "?";
        Write("INFO ", $"═══ BaumDash {ver} — session start ═══", null, "", "");
        Write("INFO ", $"OS: {Environment.OSVersion}  CLR: {Environment.Version}", null, "", "");
        Write("INFO ", sep, null, "", "");
    }

    // ── Core write ────────────────────────────────────────────────────────────

    private static void Write(string level, string message, Exception? ex,
                               string caller, string file)
    {
        try
        {
            var shortFile = string.IsNullOrEmpty(file)
                ? ""
                : Path.GetFileNameWithoutExtension(file);

            var location = (string.IsNullOrEmpty(shortFile) && string.IsNullOrEmpty(caller))
                ? ""
                : $"[{shortFile}/{caller}] ";

            var lines = new System.Text.StringBuilder();
            lines.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {location}{message}");

            if (ex != null)
            {
                lines.AppendLine($"  Exception : {ex.GetType().FullName}: {ex.Message}");
                if (ex.StackTrace != null)
                    foreach (var line in ex.StackTrace.Split('\n'))
                        lines.AppendLine($"    {line.TrimEnd()}");

                var inner = ex.InnerException;
                int depth = 0;
                while (inner != null && depth++ < 4)
                {
                    lines.AppendLine($"  Caused by : {inner.GetType().FullName}: {inner.Message}");
                    inner = inner.InnerException;
                }
            }

            lock (_lock)
            {
                // Keep log ≤ 2 MB — rotate by truncating the oldest half
                TrimIfNeeded();
                File.AppendAllText(LogPath, lines.ToString());
            }
        }
        catch
        {
            // Never let logging crash the app
        }
    }

    private static void TrimIfNeeded()
    {
        const long maxBytes = 2 * 1024 * 1024; // 2 MB
        try
        {
            if (!File.Exists(LogPath)) return;
            var info = new FileInfo(LogPath);
            if (info.Length < maxBytes) return;

            // Read all lines, keep the latter half
            var allLines = File.ReadAllLines(LogPath);
            var trimmed  = allLines.Skip(allLines.Length / 2).ToArray();
            File.WriteAllLines(LogPath, trimmed);
            File.AppendAllText(LogPath, "── log trimmed (was > 2 MB) ──" + Environment.NewLine);
        }
        catch { }
    }
}
