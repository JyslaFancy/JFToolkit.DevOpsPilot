using System.Diagnostics;

namespace JFToolkit.DevOpsPilot.Services;

/// <summary>
/// Cross-platform hardware detection.
/// Detects RAM, GPU, CPU cores, disk space, and OS info.
/// Uses PowerShell on Windows, /proc/meminfo on Linux, sysctl on macOS.
/// All detection is best-effort — fields may be null/fallback if detection fails.
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
                var total = RunAndParse("sysctl", "-n hw.memsize", parseFirstLine: s =>
                    long.TryParse(s, out var b) ? b / 1024.0 / 1024.0 / 1024.0 : 0);
                if (total > 0)
                    return (total, total * 0.6);
            }

            if (OperatingSystem.IsWindows())
            {
                // PowerShell — wmic is deprecated/removed on Windows 11 24H2+
                var totalBytes = RunAndParse("powershell",
                    "-NoProfile -Command \"[double](Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory\"",
                    parseFirstLine: s => double.TryParse(s, out var b) ? b / 1024.0 / 1024.0 / 1024.0 : 0);

                var freeKb = RunAndParse("powershell",
                    "-NoProfile -Command \"[double](Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory\"",
                    parseFirstLine: s => double.TryParse(s, out var kb) ? kb / 1024.0 / 1024.0 : 0);

                if (totalBytes > 0)
                    return (totalBytes, freeKb > 0 ? freeKb : totalBytes * 0.5);
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
                    if (parts.Length >= 2 && double.TryParse(parts[1].Trim(), out var memMiB))
                        return (parts[0].Trim(), Math.Round(memMiB / 1024.0, 1));
                }
                // fallback: lspci
                var lspci = RunCommand("bash", "-c \"lspci | grep -i 'vga\\|3d\\|display' | head -1\"");
                if (!string.IsNullOrWhiteSpace(lspci))
                {
                    // Extract name after ": "
                    var colon = lspci.IndexOf(": ");
                    var gpuName = colon > 0 ? lspci[(colon + 2)..].Trim() : lspci.Trim();
                    return (gpuName, null);
                }
            }

            if (OperatingSystem.IsMacOS())
            {
                var sp = RunCommand("system_profiler", "SPDisplaysDataType");
                if (!string.IsNullOrWhiteSpace(sp))
                {
                    var lines = sp.Split('\n');
                    foreach (var line in lines)
                    {
                        if (line.Contains("Chipset Model:") || line.Contains("Vendor:"))
                        {
                            var parts = line.Split(':', 2);
                            if (parts.Length == 2) return (parts[1].Trim(), null);
                            break;
                        }
                    }
                }
            }

            if (OperatingSystem.IsWindows())
            {
                // PowerShell — Get-CimInstance Win32_VideoController
                // Output: Name|AdapterRAM per line, sorted by RAM desc (dedicated GPU first)
                var psResult = RunCommand("powershell",
                    "-NoProfile -Command \"Get-CimInstance Win32_VideoController | " +
                    "Sort-Object AdapterRAM -Descending | " +
                    "ForEach-Object { $_.Name + '|' + $_.AdapterRAM }\"",
                    timeoutMs: 10000);

                if (!string.IsNullOrWhiteSpace(psResult))
                {
                    var lines = psResult.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var parts = line.Trim().Split('|');
                        if (parts.Length >= 2)
                        {
                            var name = parts[0].Trim();
                            double? mem = null;
                            if (long.TryParse(parts[1].Trim(), out var bytes) && bytes > 0)
                                mem = Math.Round(bytes / 1024.0 / 1024.0 / 1024.0, 1);
                            return (name, mem);
                        }
                    }
                    // If no adapter RAM, at least return the name
                    if (lines.Length > 0 && !string.IsNullOrWhiteSpace(lines[0]))
                        return (lines[0].Trim(), null);
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

    private static string? RunCommand(string file, string args, int timeoutMs = 5000)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc == null) return null;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(timeoutMs);
            return proc.ExitCode == 0 ? output.Trim() : null;
        }
        catch { return null; }
    }

    private static double RunAndParse(string file, string args, Func<string, double> parseFirstLine, int timeoutMs = 8000)
    {
        var output = RunCommand(file, args, timeoutMs);
        if (string.IsNullOrWhiteSpace(output)) return 0;
        var firstLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                              .Select(l => l.Trim())
                              .FirstOrDefault(l => l.Length > 0 && l.Any(c => char.IsDigit(c) || c == '.'));
        if (firstLine == null) return 0;
        try { return parseFirstLine(firstLine); }
        catch { return 0; }
    }
}
