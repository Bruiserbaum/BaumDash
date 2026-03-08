using System.Text.Json;
using WinUIAudioMixer.Models;

namespace WinUIAudioMixer.Services;

public sealed record CalendarEvent(string Title, DateTime Start, DateTime End, string Description);

public sealed class GoogleCalendarService
{
    private static readonly string ConfigPath =
        Path.Combine(AppContext.BaseDirectory, "gcalendar-config.json");

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    static GoogleCalendarService()
    {
        // Google's iCal endpoint rejects requests without a browser-like User-Agent
        _http.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 BaumDash/1.0");
        _http.DefaultRequestHeaders.Add("Accept", "text/calendar, text/plain, */*");
    }

    public bool IsConfigured { get; }
    private readonly List<string> _icalUrls;

    public GoogleCalendarService(GoogleCalendarConfig cfg)
    {
        _icalUrls    = cfg.Calendars
            .Select(c => c.ICalUrl.Trim())
            .Where(u => !string.IsNullOrEmpty(u))
            .ToList();
        IsConfigured = _icalUrls.Count > 0;
    }

    public static GoogleCalendarConfig? LoadConfig()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return null;
            var cfg = JsonSerializer.Deserialize<GoogleCalendarConfig>(File.ReadAllText(ConfigPath));
            if (cfg == null) return null;

            // Migrate legacy single ICalUrl → Calendars list
            if (!string.IsNullOrWhiteSpace(cfg.ICalUrl) && cfg.Calendars.Count == 0)
            {
                cfg.Calendars.Add(new CalendarEntry { Name = "My Calendar", ICalUrl = cfg.ICalUrl });
                cfg.ICalUrl = null;
            }
            return cfg;
        }
        catch { return null; }
    }

    /// <summary>Fetches all iCal feeds in parallel and returns upcoming events merged and sorted, plus any per-URL errors.</summary>
    public async Task<(List<CalendarEvent> Events, List<string> Errors)> FetchUpcomingAsync(int daysAhead = 60)
    {
        var from = DateTime.Today;
        var to   = from.AddDays(daysAhead);

        var tasks   = _icalUrls.Select(url => FetchAndParseAsync(url, from, to));
        var results = await Task.WhenAll(tasks);

        var events = results
            .SelectMany(r => r.Events)
            .OrderBy(e => e.Start)
            .ToList();

        var errors = results
            .Select(r => r.Error)
            .Where(e => e != null)
            .Cast<string>()
            .ToList();

        return (events, errors);
    }

    private static async Task<(IEnumerable<CalendarEvent> Events, string? Error)> FetchAndParseAsync(
        string url, DateTime from, DateTime to)
    {
        try
        {
            var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return (Enumerable.Empty<CalendarEvent>(),
                        $"HTTP {(int)response.StatusCode} from {new Uri(url).Host}");

            var raw = await response.Content.ReadAsStringAsync();

            // Detect HTML response (e.g. Google login redirect returned as 200)
            if (raw.TrimStart().StartsWith("<", StringComparison.Ordinal))
                return (Enumerable.Empty<CalendarEvent>(),
                        $"Got HTML instead of iCal from {new Uri(url).Host} — check your URL");

            return (ParseIcal(raw, from, to), null);
        }
        catch (Exception ex)
        {
            return (Enumerable.Empty<CalendarEvent>(), $"Failed to fetch calendar: {ex.Message}");
        }
    }

    // ── iCal parser ───────────────────────────────────────────────────────────

    private static List<CalendarEvent> ParseIcal(string ical, DateTime from, DateTime to)
    {
        var results = new List<CalendarEvent>();
        var lines   = UnfoldLines(ical);

        string? summary = null, description = null, rrule = null;
        DateTime? start = null, end = null;
        var exDates = new List<DateTime>();
        bool inEvent = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            if (line == "BEGIN:VEVENT")
            {
                inEvent = true;
                summary = description = rrule = null;
                start = end = null;
                exDates.Clear();
                continue;
            }

            if (line == "END:VEVENT")
            {
                if (inEvent && start.HasValue)
                {
                    var dur = end.HasValue ? end.Value - start.Value : TimeSpan.Zero;
                    var title = summary ?? "(no title)";
                    var desc  = description ?? "";

                    if (string.IsNullOrEmpty(rrule))
                    {
                        if (start.Value >= from && start.Value <= to)
                            results.Add(new CalendarEvent(title, start.Value, end ?? start.Value, desc));
                    }
                    else
                    {
                        results.AddRange(ExpandRRule(title, desc, start.Value, dur, rrule, exDates, from, to));
                    }
                }
                inEvent = false;
                continue;
            }

            if (!inEvent) continue;

            var sep = line.IndexOf(':');
            if (sep < 0) continue;
            var prop     = line[..sep];
            var val      = line[(sep + 1)..];
            var propName = prop.Contains(';') ? prop[..prop.IndexOf(';')] : prop;

            switch (propName)
            {
                case "SUMMARY":     summary     = UnescapeText(val); break;
                case "DESCRIPTION": description = UnescapeText(val); break;
                case "DTSTART":     start       = ParseIcalDate(val); break;
                case "DTEND":       end         = ParseIcalDate(val); break;
                case "RRULE":       rrule       = val; break;
                case "EXDATE":
                    var ex = ParseIcalDate(val);
                    if (ex.HasValue) exDates.Add(ex.Value);
                    break;
            }
        }

        return results;
    }

    // ── RRULE expander ────────────────────────────────────────────────────────

    private static IEnumerable<CalendarEvent> ExpandRRule(
        string title, string description,
        DateTime start, TimeSpan duration, string rrule,
        IReadOnlyList<DateTime> exDates,
        DateTime windowStart, DateTime windowEnd)
    {
        var parts = rrule.Split(';')
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);

        if (!parts.TryGetValue("FREQ", out var freq)) yield break;

        int interval = int.TryParse(parts.GetValueOrDefault("INTERVAL", "1"), out var iv) ? Math.Max(1, iv) : 1;

        DateTime? until = null;
        if (parts.TryGetValue("UNTIL", out var u)) until = ParseIcalDate(u);

        int? count = null;
        if (parts.TryGetValue("COUNT", out var cs) && int.TryParse(cs, out var c) && c > 0) count = c;

        // BYDAY: strip positional prefix (e.g. "1MO" → "MO", "-1FR" → "FR")
        DayOfWeek[]? byDays = null;
        if (parts.TryGetValue("BYDAY", out var bd))
            byDays = bd.Split(',').Select(d => DayCodeToDayOfWeek(d.TrimStart('-', '1', '2', '3', '4', '5'))).ToArray();

        var exDateSet = exDates.Select(d => d.Date).ToHashSet();

        // Jump cursor close to the window to avoid iterating from years ago
        var (cursor, skipped) = SkipToWindow(start, freq, interval, byDays, windowStart);
        int totalCount = skipped; // approximate COUNT tracking

        var safetyEnd = windowEnd.AddDays(7 * (interval + 1) + 32);

        while (cursor <= safetyEnd)
        {
            if (until.HasValue && cursor > until.Value) yield break;
            if (count.HasValue && totalCount >= count.Value) yield break;

            IEnumerable<DateTime> candidates;

            if (freq == "WEEKLY" && byDays is { Length: > 0 })
            {
                // Monday of the week containing cursor
                var weekMon = cursor.Date.AddDays(-(((int)cursor.DayOfWeek + 6) % 7));
                candidates = byDays
                    .Select(d => weekMon.AddDays(((int)d + 6) % 7).Add(start.TimeOfDay))
                    .OrderBy(d => d);
            }
            else
            {
                candidates = new[] { cursor };
            }

            foreach (var candidate in candidates)
            {
                if (candidate < start) continue;
                if (until.HasValue && candidate > until.Value) yield break;
                if (count.HasValue && totalCount >= count.Value) yield break;

                if (!exDateSet.Contains(candidate.Date))
                {
                    if (candidate >= windowStart && candidate <= windowEnd)
                        yield return new CalendarEvent(title, candidate, candidate + duration, description);
                    totalCount++;
                }
            }

            cursor = Advance(cursor, freq, interval);
        }
    }

    private static (DateTime Cursor, int Skipped) SkipToWindow(
        DateTime start, string freq, int interval, DayOfWeek[]? byDays, DateTime windowStart)
    {
        if (start >= windowStart) return (start, 0);

        switch (freq)
        {
            case "DAILY":
            {
                int n = Math.Max(0, (int)((windowStart - start).TotalDays / interval) - 1);
                return (start.AddDays(n * interval), n);
            }
            case "WEEKLY":
            {
                int n = Math.Max(0, (int)((windowStart - start).TotalDays / (7.0 * interval)) - 1);
                int perWeek = byDays?.Length ?? 1;
                return (start.AddDays(n * 7 * interval), n * perWeek);
            }
            case "MONTHLY":
            {
                int totalMonths = (windowStart.Year * 12 + windowStart.Month) -
                                  (start.Year        * 12 + start.Month);
                int n = Math.Max(0, totalMonths / interval - 1);
                return (start.AddMonths(n * interval), n);
            }
            case "YEARLY":
            {
                int n = Math.Max(0, (windowStart.Year - start.Year) / interval - 1);
                return (start.AddYears(n * interval), n);
            }
            default:
                return (start, 0);
        }
    }

    private static DateTime Advance(DateTime d, string freq, int interval) => freq switch
    {
        "DAILY"   => d.AddDays(interval),
        "WEEKLY"  => d.AddDays(7 * interval),
        "MONTHLY" => d.AddMonths(interval),
        "YEARLY"  => d.AddYears(interval),
        _         => d.AddYears(100), // unknown freq: stop
    };

    private static DayOfWeek DayCodeToDayOfWeek(string code) => code.ToUpper() switch
    {
        "MO" => DayOfWeek.Monday,
        "TU" => DayOfWeek.Tuesday,
        "WE" => DayOfWeek.Wednesday,
        "TH" => DayOfWeek.Thursday,
        "FR" => DayOfWeek.Friday,
        "SA" => DayOfWeek.Saturday,
        _    => DayOfWeek.Sunday,
    };

    // ── iCal helpers ──────────────────────────────────────────────────────────

    private static IEnumerable<string> UnfoldLines(string ical)
    {
        var result  = new List<string>();
        string? current = null;
        foreach (var line in ical.Split('\n'))
        {
            var l = line.TrimEnd('\r');
            if (l.Length > 0 && (l[0] == ' ' || l[0] == '\t'))
                current = (current ?? "") + l[1..];
            else
            {
                if (current != null) result.Add(current);
                current = l;
            }
        }
        if (current != null) result.Add(current);
        return result;
    }

    private static DateTime? ParseIcalDate(string val)
    {
        // DATE-only: 20240307 → midnight local
        if (val.Length == 8 && !val.Contains('T'))
        {
            if (DateTime.TryParseExact(val, "yyyyMMdd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var d))
                return d;
        }
        // DATE-TIME UTC: 20240307T100000Z
        if (val.EndsWith('Z'))
        {
            if (DateTime.TryParseExact(val, "yyyyMMdd'T'HHmmss'Z'",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal |
                System.Globalization.DateTimeStyles.AdjustToUniversal, out var utc))
                return utc.ToLocalTime();
        }
        // DATE-TIME with TZID (treat as local)
        if (DateTime.TryParseExact(val, "yyyyMMdd'T'HHmmss",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var local))
            return local;

        return null;
    }

    private static string UnescapeText(string s) =>
        s.Replace("\\n", "\n").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\");
}
