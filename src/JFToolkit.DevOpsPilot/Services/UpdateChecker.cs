using System.Reflection;
using System.Text.Json;

namespace JFToolkit.DevOpsPilot.Services;

/// <summary>
/// Checks NuGet.org for newer versions of the tool.
/// Non-blocking — fires on startup, shows a message if update is available.
/// </summary>
public static class UpdateChecker
{
    private static readonly string NuGetUrl =
        "https://api.nuget.org/v3-flatcontainer/jftoolkit.devopspilot/index.json";

    private static string? _latestVersion;

    /// <summary>
    /// Start an async version check in the background.
    /// Call at startup, then call <see cref="ShowIfAvailable"/> after a short delay.
    /// </summary>
    public static void CheckInBackground()
    {
        Task.Run(async () =>
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var json = await http.GetStringAsync(NuGetUrl);
                using var doc = JsonDocument.Parse(json);
                var versions = doc.RootElement.GetProperty("versions");
                // Last entry is the latest
                var count = versions.GetArrayLength();
                if (count > 0)
                    _latestVersion = versions[count - 1].GetString();
            }
            catch
            {
                // Silently ignore — update check is best-effort
            }
        });
    }

    /// <summary>
    /// If a newer version is available, print a message.
    /// Call this after startup tasks complete.
    /// </summary>
    public static void ShowIfAvailable()
    {
        if (_latestVersion is null) return;

        var current = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion?
            .Split('+')[0]; // Strip git hash

        if (current is null) return;

        // Simple version comparison
        if (TryParseVersion(_latestVersion, out var latest) &&
            TryParseVersion(current, out var curr) &&
            latest > curr)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;

            // 3 empty boxes vertically centered
            Console.WriteLine($"  ╔════════════════════════════════════════════╗");
            Console.WriteLine($"  ║  Update available: v{curr} → v{_latestVersion}              ║");
            Console.WriteLine($"  ║                                            ║");
            Console.WriteLine($"  ║  Run:                                      ║");
            Console.WriteLine($"  ║  dotnet tool update -g JFToolkit.DevOpsPilot║");
            Console.WriteLine($"  ╚════════════════════════════════════════════╝");
            Console.ResetColor();
            Console.WriteLine();
        }
    }

    private static bool TryParseVersion(string? v, out Version version)
    {
        version = new Version(0, 0);
        if (v is null) return false;
        // Strip leading 'v' if present
        if (v.StartsWith('v')) v = v[1..];
        return Version.TryParse(v, out version);
    }
}
