using System.Text.Json;

namespace JFToolkit.DevOpsPilot.Config;

internal sealed class JftkConfig
{
    public string? AzureDevOpsPat { get; set; }
    public string? AzureDevOpsOrg { get; set; }
    public string? LlmProvider { get; set; } = "ollama";
    public string? OllamaModel { get; set; } = "qwen2.5:7b";
    public string? OllamaUrl { get; set; } = "http://localhost:11434";
    public string? OpenAiKey { get; set; }
    public string? OpenAiModel { get; set; }

    private static readonly string ConfigDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".jftoolkit");

    private static readonly string ConfigPath =
        Path.Combine(ConfigDir, "config.json");

    public static JftkConfig Load()
    {
        if (File.Exists(ConfigPath))
        {
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<JftkConfig>(json) ?? new();
        }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json);
    }
}
