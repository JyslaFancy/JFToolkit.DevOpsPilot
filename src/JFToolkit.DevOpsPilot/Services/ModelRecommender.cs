namespace JFToolkit.DevOpsPilot.Services;

/// <summary>
/// Recommends the best Ollama model(s) based on detected hardware.
/// Pure logic — no I/O. Consumed by DevOpsPilot setup and the CLI.
/// </summary>
public static class ModelRecommender
{
    /// <summary>A single model recommendation with reasoning.</summary>
    public sealed record Recommendation(
        string Model,
        string Reason,
        int Priority  // lower = better fit
    );

    /// <summary>
    /// Hardware profile parsed from detector output.
    /// </summary>
    public sealed record Profile(
        double RamGb,
        bool HasDedicatedGpu,
        double? GpuMemoryGb,
        int CpuCores
    );

    /// <summary>
    /// Get recommended models sorted best→worst for the given hardware profile.
    /// Always returns at least one recommendation.
    /// </summary>
    public static List<Recommendation> GetRecommendations(Profile profile)
    {
        var results = new List<Recommendation>();

        var ram = profile.RamGb;
        var gpuMem = profile.HasDedicatedGpu ? profile.GpuMemoryGb ?? 0 : 0;
        var hasGpu = gpuMem >= 4; // 4+ GB VRAM for meaningful GPU acceleration

        // ── Tier 1: Best fit (GPU-accelerated models) ──
        if (hasGpu && gpuMem >= 16)
        {
            results.Add(new("qwen2.5:32b", $"32 GB VRAM GPU — {gpuMem} GB available. Full precision 32B runs great.", 1));
            results.Add(new("codestral:22b", $"{gpuMem} GB VRAM — excellent code model fits comfortably.", 2));
            results.Add(new("qwen2.5:14b", $"Fastest inference on {gpuMem} GB GPU.", 3));
        }
        else if (hasGpu && gpuMem >= 8)
        {
            results.Add(new("qwen2.5:14b", $"{gpuMem} GB VRAM GPU — 14B fits with room to spare.", 1));
            results.Add(new("codestral:22b", $"{gpuMem} GB VRAM — quantized 22B should fit.", 2));
            results.Add(new("qwen2.5:7b", $"Lightning fast on {gpuMem} GB GPU.", 3));
        }
        else if (hasGpu && gpuMem >= 4)
        {
            results.Add(new("qwen2.5:7b", $"{gpuMem} GB VRAM — 7B runs fully on GPU.", 1));
            results.Add(new("phi4:7b", $"Microsoft's latest, great for structured output.", 2));
            results.Add(new("llama3.2:3b", $"Ultra-fast on {gpuMem} GB GPU.", 3));
        }

        // ── Tier 2: System RAM based (CPU only or small GPU) ──
        if (ram >= 32)
        {
            results.Add(new("qwen2.5:14b", "32+ GB RAM — 14B runs comfortably on CPU.", results.Count > 0 ? 4 : 1));
            results.Add(new("qwen2.5:7b", "Plenty of RAM. 7B is snappy.", results.Count > 0 ? 5 : 2));
            results.Add(new("codestral:22b", "32 GB RAM — quantized code model may work on CPU.", results.Count > 0 ? 6 : 3));
        }
        else if (ram >= 16)
        {
            results.Add(new("qwen2.5:7b", "16 GB RAM — 7B is the sweet spot.", results.Count > 0 ? 4 : 1));
            results.Add(new("phi4:7b", "Great reasoning, fits in 16 GB.", results.Count > 0 ? 5 : 2));
            results.Add(new("llama3.2:3b", "Lightweight fallback, always fast.", results.Count > 0 ? 6 : 3));
        }
        else if (ram >= 8)
        {
            results.Add(new("llama3.2:3b", "8 GB RAM — 3B model recommended.", results.Count > 0 ? 4 : 1));
            results.Add(new("qwen2.5:3b", "Compact Qwen variant, good for 8 GB.", results.Count > 0 ? 5 : 2));
            results.Add(new("phi4:3b", "Microsoft Phi-4 mini, works in 8 GB.", results.Count > 0 ? 6 : 3));
        }
        else
        {
            results.Add(new("llama3.2:1b", "Limited RAM — smallest usable model.", results.Count > 0 ? 4 : 1));
            results.Add(new("tinyllama:1.1b", "Absolute minimum for basic tasks.", results.Count > 0 ? 5 : 2));
        }

        // Sort by priority, remove duplicates, take top 5
        return results
            .GroupBy(r => r.Model)
            .Select(g => g.OrderBy(r => r.Priority).First())
            .OrderBy(r => r.Priority)
            .Take(5)
            .ToList();
    }

    /// <summary>
    /// Get the single best model for this hardware.
    /// </summary>
    public static Recommendation GetBest(Profile profile)
    {
        return GetRecommendations(profile).First();
    }

    /// <summary>
    /// Convenience: get recommendations from HardwareInfo.
    /// </summary>
    public static List<Recommendation> GetRecommendations(HardwareDetector.HardwareInfo hw)
    {
        return GetRecommendations(new Profile(
            RamGb: hw.RamGb,
            HasDedicatedGpu: hw.HasDedicatedGpu,
            GpuMemoryGb: hw.GpuMemoryGb,
            CpuCores: hw.CpuCores
        ));
    }
}
