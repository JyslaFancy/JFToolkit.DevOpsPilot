using System.Diagnostics;

namespace JFToolkit.DevOpsPilot.Services;

public static class OllamaSetup
{
    public static bool IsInstalled()
    {
        try
        {
            var psi = new ProcessStartInfo("ollama", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(3000);
            return proc?.ExitCode == 0;
        }
        catch { return false; }
    }

    public static string GetInstallInstructions()
    {
        if (OperatingSystem.IsWindows()) return "winget install Ollama.Ollama";
        if (OperatingSystem.IsMacOS()) return "brew install ollama";
        if (OperatingSystem.IsLinux()) return "curl -fsSL https://ollama.com/install.sh | sh";
        return "Visit https://ollama.com/download";
    }

    public static async Task<bool> PullModelAsync(string model = "qwen2.5:7b", int timeoutMinutes = 30)
    {
        if (!IsInstalled()) return false;
        try
        {
            var psi = new ProcessStartInfo("ollama", $"pull {model}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return false;

            // Read stdout asynchronously (prevent pipe-buffer deadlock)
            var readOut = proc.StandardOutput.ReadToEndAsync();

            // Stream stderr lines to console — ollama pull shows progress on stderr
            var cts = new CancellationTokenSource(TimeSpan.FromMinutes(timeoutMinutes));
            var readErr = Task.Run(async () =>
            {
                var sb = new System.Text.StringBuilder();
                while (!cts.Token.IsCancellationRequested)
                {
                    var line = await proc.StandardError.ReadLineAsync(cts.Token);
                    if (line == null) break;
                    Console.WriteLine($"  {line}");
                    sb.AppendLine(line);
                }
                return sb.ToString();
            });

            try { await proc.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException) { proc.Kill(); Console.Error.WriteLine("Timed out."); return false; }

            var stdout = await readOut;
            var stderr = await readErr;
            return proc.ExitCode == 0;
        }
        catch { return false; }
    }

    public static bool HasModel(string model)
    {
        if (!IsInstalled()) return false;
        try
        {
            var psi = new ProcessStartInfo("ollama", "list")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return false;
            // Read FIRST, wait later — classic deadlock fix.
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(5000);
            return output.Contains(model, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>
    /// List all locally pulled Ollama models.
    /// Returns empty list if Ollama is not installed or unreachable.
    /// </summary>
    public static List<string> ListLocalModels()
    {
        if (!IsInstalled()) return [];

        try
        {
            var psi = new ProcessStartInfo("ollama", "list")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return [];

            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(5000);

            if (proc.ExitCode != 0) return [];

            // Skip header line "NAME              ID              SIZE    MODIFIED"
            var models = new List<string>();
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("NAME", StringComparison.OrdinalIgnoreCase))
                    continue;
                // First column is model name (before first space group)
                var name = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(name))
                    models.Add(name);
            }
            return models;
        }
        catch { return []; }
    }
}
