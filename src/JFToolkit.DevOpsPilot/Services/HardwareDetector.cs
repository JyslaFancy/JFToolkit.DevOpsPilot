using System.Diagnostics;

namespace JFToolkit.DevOpsPilot.Services;

/// <summary>
/// Cross-platform hardware detection.
/// Detects RAM, GPU, CPU cores, disk space, and OS info.
/// Works on Windows, Linux, and macOS without external dependencies.
/// </summary>
public static class HardwareDetector
{
    public sealed record HardwareInfo(
        double RamGb,
        double AvailableRamGb,
        int CpuCores,
        string OsPlatform,
        string Architecture,
        string? GpuName,
        double? GpuMemoryGb,
        double? FreeDiskGb
    )
    {
        public bool HasGpu => GpuName != null;
        public bool HasDedicatedGpu => GpuMemoryGb >= 2;

        public override string ToString()
        {
            var gpu = HasGpu
                ? $"{GpuName}" + (GpuMemoryGb.HasValue ? $" ({GpuMemoryGb:F0} GB)" : "")
                : "None detected";
            var disk = FreeDiskGb.HasValue ? $"{FreeDiskGb:F0} GB free" : "Unknown";
            return $"OS: {OsPlatform} {Architecture} | RAM: {RamGb:F0} GB ({AvailableRamGb:F0} free) | " +
                   $"CPU: {CpuCores} cores | GPU: {gpu} | Disk: {disk}";
        }
    }

    /// <summary>
    /// Detect all hardware in one call. Best-effort — fields may be null if detection fails.
    /// </summary>
    public static HardwareInfo Detect()
    {
        var ram = DetectRam();
        var gpu = DetectGpu();
        var cpu = Environment.ProcessorCount;
        var os = GetOsPlatform();
        var arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString();
        var disk = DetectFreeDiskSpace();

        return new HardwareInfo(
            RamGb: Math.Round(ram.total, 0),
            AvailableRamGb: Math.Round(ram.available, 0),
            CpuCores: cpu,
            OsPlatform: os,
            Architecture: arch,
            GpuName: gpu.name,
            GpuMemoryGb: gpu.memoryGb,
            FreeDiskGb: disk
        );
    }

    // ─── RAM ───────────────────────────────────────────────

    private static (double total, double available) DetectRam()
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                // Parse /proc/meminfo — works on WSL and native Linux
                var lines = File.ReadAllLines("/proc/meminfo");
                long totalKb = 0, availKb = 0;
                foreach (var line in lines)
                {
                    if (line.StartsWith("MemTotal:"))
                        totalKb = long.Parse(line.Split(':')[1].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]);
                    if (line.StartsWith("MemAvailable:"))
                        availKb = long.Parse(line.Split(':')[1].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]);
                }
                if (totalKb > 0)
                    return (totalKb / 1024.0 / 1024.0, availKb > 0 ? availKb / 1024.0 / 1024.0 : totalKb / 1024.0 / 1024.0 * 0.5);
            }

            if (OperatingSystem.IsMacOS())
            {
                // sysctl hw.memsize
                var total = RunAndParse("sysctl", "-n hw.memsize", parseFirstLine: s =>
                    long.TryParse(s, out var b) ? b / 1024.0 / 1024.0 / 1024.0 : 8.0);
                if (total > 0)
                    return (total, total * 0.6); // estimate available
            }

            if (OperatingSystem.IsWindows())
            {
                // Use wmic on Windows
                var total = RunAndParse("wmic", "computersystem get TotalPhysicalMemory", parseFirstLine: s =>
                    long.TryParse(s, out var b) ? b / 1024.0 / 1024.0 / 1024.0 : 8.0);
                var free = RunAndParse("wmic", "OS get FreePhysicalMemory", parseFirstLine: s =>
                    long.TryParse(s, out var kb) ? kb / 1024.0 / 1024.0 : total * 0.5);
                if (total > 0)
                    return (total, free > 0 ? free : total * 0.5);
            }
        }
        catch { /* best-effort */ }

        return (8.0, 4.0); // fallback
    }

    // ─── GPU ───────────────────────────────────────────────

    private static (string? name, double? memoryGb) DetectGpu()
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                // nvidia-smi
                var out_nv = RunCommand("nvidia-smi", "--query-gpu=name,memory.total --format=csv,noheader,nounits");
                if (!string.IsNullOrWhiteSpace(out_nv))
                {
                    var parts = out_nv.Split(',');
                    if (parts.Length >= 2 &&
                        double.TryParse(parts[1].Trim(), out var memMiB))
                    {
                        return (parts[0].Trim(), Math.Round(memMiB / 1024.0, 1));
                    }
                }
                // AMD GPU via /sys/class/drm
                // Fall through to lspci
                var lspci = RunCommand("lspci", "-v | grep -i vga");
                if (!string.IsNullOrWhiteSpace(lspci))
                {
                    var first = lspci.Split('\n').FirstOrDefault()?.Trim();
                    if (!string.IsNullOrWhiteSpace(first))
                        return (first, null);
                }
            }

            if (OperatingSystem.IsMacOS())
            {
                var sp = RunCommand("system_profiler", "SPDisplaysDataType");
                if (!string.IsNullOrWhiteSpace(sp))
                {
                    // Extract GPU name from line like "Chipset Model: Apple M2 Pro"
                    var lines = sp.Split('\n');
                    string? name = null;
                    foreach (var line in lines)
                    {
                        if (line.Contains("Chipset Model:") || line.Contains("Vendor:"))
                        {
                            var parts = line.Split(':', 2);
                            if (parts.Length == 2) name = parts[1].Trim();
                            break;
                        }
                    }
                    if (name != null) return (name, null);
                }
            }

            if (OperatingSystem.IsWindows())
            {
                var wmic = RunCommand("wmic", "path win32_videocontroller get name,adapterram /format:csv");
                if (!string.IsNullOrWhiteSpace(wmic))
                {
                    var lines = wmic.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    if (lines.Length > 1)
                    {
                        var parts = lines[1].Split(',');
                        if (parts.Length >= 3)
                        {
                            var name = parts[1].Trim();
                            double? mem = null;
                            if (long.TryParse(parts[2].Trim(), out var bytes) && bytes > 0)
                                mem = Math.Round(bytes / 1024.0 / 1024.0 / 1024.0, 1);
                            return (name, mem);
                        }
                    }
                }
            }
        }
        catch { /* best-effort */ }

        return (null, null);
    }

    // ─── Disk ──────────────────────────────────────────────

    private static double? DetectFreeDiskSpace()
    {
        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (Directory.Exists(home))
            {
                var drive = new DriveInfo(Path.GetPathRoot(home) ?? "/");
                return Math.Round(drive.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0, 1);
            }
        }
        catch { }

        try
        {
            // Fallback: check root
            var root = OperatingSystem.IsWindows() ? "C:\\" : "/";
            var drive = new DriveInfo(root);
            return Math.Round(drive.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0, 1);
        }
        catch { return null; }
    }

    // ─── Helpers ───────────────────────────────────────────

    private static string GetOsPlatform()
    {
        if (OperatingSystem.IsWindows()) return "Windows";
        if (OperatingSystem.IsMacOS()) return "macOS";
        if (OperatingSystem.IsLinux()) return "Linux";
        return System.Runtime.InteropServices.RuntimeInformation.OSDescription;
    }

    private static string? RunCommand(string file, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return null;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(5000);
            return proc.ExitCode == 0 ? output.Trim() : null;
        }
        catch { return null; }
    }

    private static double RunAndParse(string file, string args, Func<string, double> parseFirstLine)
    {
        var output = RunCommand(file, args);
        if (string.IsNullOrWhiteSpace(output)) return 0;
        var firstLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                              .Select(l => l.Trim())
                              .FirstOrDefault(l => l.Length > 0 && l.Any(char.IsDigit));
        if (firstLine == null) return 0;
        try { return parseFirstLine(firstLine); }
        catch { return 0; }
    }
}
