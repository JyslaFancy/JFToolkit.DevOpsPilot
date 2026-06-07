using System.Text.Json;

namespace JFToolkit.DevOpsPilot.Config;

public sealed class JftkConfig
{
    public string? AzureDevOpsPat { get; set; }
    public string? AzureDevOpsOrg { get; set; }
    /// <summary>
    /// Full Azure DevOps / TFS base URL. If set, overrides AzureDevOpsOrg.
    /// Examples: https://dev.azure.com/myorg, https://tfs.company.com/tfs/DefaultCollection
    /// </summary>
    public string? AzureDevOpsUrl { get; set; }
    /// <summary>REST API version. Defaults to 7.1 for Azure DevOps. Use 5.0/6.0 for older TFS.</summary>
    public string? AzureDevOpsApiVersion { get; set; } = "7.1";
    public string? LlmProvider { get; set; } = "ollama";
    public string? OllamaModel { get; set; } = "qwen2.5:7b";
    public string? OllamaUrl { get; set; } = "http://localhost:11434";
    public string? OpenAiKey { get; set; }
    public string? OpenAiModel { get; set; }
    public string? OpenAiBaseUrl { get; set; }

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
