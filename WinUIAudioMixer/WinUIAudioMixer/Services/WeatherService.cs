using System.Text.Json;

namespace WinUIAudioMixer.Services;

public sealed record WeatherSnapshot(
    double TempNow,
    double TempHigh,
    double TempLow,
    double WindSpeed,
    string Condition,
    string TempUnit,
    string WindUnit);

/// <summary>
/// Fetches current weather from Open-Meteo (free, no API key).
/// Reads weather-config.json from AppContext.BaseDirectory.
/// </summary>
public sealed class WeatherService : IDisposable
{
    private readonly HttpClient _http;
    private readonly double     _lat;
    private readonly double     _lon;
    private readonly bool       _fahrenheit;

    public bool IsConfigured { get; }

    private WeatherService(double lat, double lon, bool fahrenheit)
    {
        _lat        = lat;
        _lon        = lon;
        _fahrenheit = fahrenheit;
        IsConfigured = true;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    public static WeatherService? LoadAndCreate()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "weather-config.json");
            if (!File.Exists(path)) return null;

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var r = doc.RootElement;

            double lat = r.TryGetProperty("latitude",  out var latProp) ? latProp.GetDouble() : 0;
            double lon = r.TryGetProperty("longitude", out var lonProp) ? lonProp.GetDouble() : 0;
            string unit = r.TryGetProperty("unit", out var unitProp) ? unitProp.GetString() ?? "f" : "f";

            if (lat == 0 && lon == 0) return null;

            bool fahrenheit = !unit.StartsWith("c", StringComparison.OrdinalIgnoreCase);
            return new WeatherService(lat, lon, fahrenheit);
        }
        catch { return null; }
    }

    public async Task<WeatherSnapshot?> GetWeatherAsync()
    {
        try
        {
            string tempUnit = _fahrenheit ? "fahrenheit" : "celsius";
            string windUnit = _fahrenheit ? "mph"        : "kmh";

            var url = "https://api.open-meteo.com/v1/forecast" +
                      $"?latitude={_lat}&longitude={_lon}" +
                      "&current=temperature_2m,weather_code,wind_speed_10m" +
                      "&daily=temperature_2m_max,temperature_2m_min" +
                      $"&temperature_unit={tempUnit}&wind_speed_unit={windUnit}" +
                      "&timezone=auto&forecast_days=1";

            var json = await _http.GetStringAsync(url);
            using var doc  = JsonDocument.Parse(json);
            var root    = doc.RootElement;
            var current = root.GetProperty("current");

            double tempNow   = current.GetProperty("temperature_2m").GetDouble();
            int    wmoCode   = current.GetProperty("weather_code").GetInt32();
            double windSpeed = current.GetProperty("wind_speed_10m").GetDouble();

            var daily    = root.GetProperty("daily");
            double high  = daily.GetProperty("temperature_2m_max")[0].GetDouble();
            double low   = daily.GetProperty("temperature_2m_min")[0].GetDouble();

            string tUnit = _fahrenheit ? "°F" : "°C";
            string wUnit = _fahrenheit ? "mph" : "km/h";

            return new WeatherSnapshot(tempNow, high, low, windSpeed, WmoToCondition(wmoCode), tUnit, wUnit);
        }
        catch { return null; }
    }

    private static string WmoToCondition(int code) => code switch
    {
        0              => "Clear",
        1 or 2         => "Partly Cloudy",
        3              => "Overcast",
        45 or 48       => "Foggy",
        >= 51 and < 68 => "Rainy",
        >= 71 and < 78 => "Snowy",
        80 or 81 or 82 => "Rainy",
        85 or 86       => "Snowy",
        >= 95          => "Thunderstorm",
        _              => "–",
    };

    public void Dispose() => _http.Dispose();
}
