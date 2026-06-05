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

    public static async Task<bool> PullModelAsync(string model = "qwen2.5:7b")
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
            await proc.WaitForExitAsync();
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
            proc.WaitForExit(5000);
            return proc.StandardOutput.ReadToEnd().Contains(model, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
