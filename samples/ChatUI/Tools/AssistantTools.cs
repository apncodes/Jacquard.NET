using Jacquard.Core;

// Namespace matches the project root so the generated tool wrappers
// (AssistantTools_GetWeather_Tool, AssistantTools_GetCurrentTime_Tool)
// are accessible in Program.cs without extra usings.
namespace ChatUI;

/// <summary>
/// General-purpose assistant tools — weather lookup and time-zone conversion.
/// In production, GetWeather would call OpenWeatherMap or similar.
/// GetCurrentTime uses the real system clock with IANA time-zone identifiers.
/// </summary>
public sealed partial class AssistantTools
{
    private static readonly IReadOnlyDictionary<string, (string Condition, int TempC, int Humidity, string Wind)> _weather =
        new Dictionary<string, (string, int, int, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["london"]        = ("Overcast with light drizzle", 13, 82, "SW 18 km/h"),
            ["new york"]      = ("Partly cloudy",               21, 48, "NW 12 km/h"),
            ["tokyo"]         = ("Clear skies",                 19, 55, "E 8 km/h"),
            ["sydney"]        = ("Sunny",                       27, 38, "N 15 km/h"),
            ["paris"]         = ("Cloudy",                      15, 72, "W 14 km/h"),
            ["dubai"]         = ("Hot and sunny",               38, 30, "NE 20 km/h"),
            ["singapore"]     = ("Thundershowers",              29, 88, "Variable 5 km/h"),
            ["san francisco"] = ("Foggy morning",               16, 75, "SW 22 km/h"),
            ["berlin"]        = ("Light rain",                  11, 79, "N 10 km/h"),
            ["toronto"]       = ("Snow flurries",                3, 68, "NW 25 km/h"),
        };

    private static readonly IReadOnlyDictionary<string, string> _timezones =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["london"]        = "Europe/London",
            ["new york"]      = "America/New_York",
            ["los angeles"]   = "America/Los_Angeles",
            ["chicago"]       = "America/Chicago",
            ["toronto"]       = "America/Toronto",
            ["tokyo"]         = "Asia/Tokyo",
            ["sydney"]        = "Australia/Sydney",
            ["melbourne"]     = "Australia/Melbourne",
            ["paris"]         = "Europe/Paris",
            ["berlin"]        = "Europe/Berlin",
            ["dubai"]         = "Asia/Dubai",
            ["singapore"]     = "Asia/Singapore",
            ["hong kong"]     = "Asia/Hong_Kong",
            ["mumbai"]        = "Asia/Kolkata",
            ["san francisco"] = "America/Los_Angeles",
            ["utc"]           = "UTC",
        };

    /// <summary>
    /// Returns current weather conditions for a city.
    /// The source generator emits <c>AssistantTools_GetWeather_Tool</c> at compile time.
    /// </summary>
    [Tool("Get the current weather conditions for a city, including temperature, humidity, and wind.")]
    public string GetWeather(string city)
    {
        if (_weather.TryGetValue(city.Trim(), out var w))
            return $"Weather in {city}: {w.Condition}, {w.TempC}°C ({w.TempC * 9 / 5 + 32}°F), " +
                   $"humidity {w.Humidity}%, wind {w.Wind}.";

        return $"Weather data not available for '{city}'. " +
               "Available cities: London, New York, Tokyo, Sydney, Paris, Dubai, " +
               "Singapore, San Francisco, Berlin, Toronto.";
    }

    /// <summary>
    /// Returns the current local time in a city or timezone.
    /// The source generator emits <c>AssistantTools_GetCurrentTime_Tool</c> at compile time.
    /// </summary>
    [Tool("Get the current local date and time for a city or timezone (e.g. 'Tokyo', 'New York', 'UTC').")]
    public string GetCurrentTime(string location)
    {
        var key = location.Trim().ToLowerInvariant();
        if (!_timezones.TryGetValue(key, out var tzId))
            return $"Time zone not found for '{location}'. " +
                   "Try: London, New York, Tokyo, Sydney, Paris, Dubai, Singapore, UTC.";

        try
        {
            var tz  = TimeZoneInfo.FindSystemTimeZoneById(tzId);
            var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz);
            return $"Current time in {location}: {now:dddd, MMMM d yyyy 'at' h:mm tt} (UTC{now:zzz})";
        }
        catch
        {
            return $"Could not resolve timezone '{tzId}' on this system.";
        }
    }
}
