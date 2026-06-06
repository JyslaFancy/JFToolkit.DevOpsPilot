using JFToolkit.DevOpsPilot.Config;
using JFToolkit.DevOpsPilot.Models;
using JFToolkit.DevOpsPilot.Services;

namespace JFToolkit.DevOpsPilot;

public class DevOpsPilot
{
    private readonly AzureDevOpsService _ado;
    private readonly ILlmProvider _llm;
    private readonly ProjectAnalyzer _analyzer;
    private readonly TaskService _tasks;

    private DevOpsPilot(JftkConfig config, AzureDevOpsService ado, ILlmProvider llm)
    {
        _ado = ado; _llm = llm;
        _analyzer = new ProjectAnalyzer(llm, ado);
        _tasks = new TaskService(ado, llm);
    }

    public static DevOpsPilot Create()
    {
        var c = JftkConfig.Load();
        if (string.IsNullOrWhiteSpace(c.AzureDevOpsPat))
            throw new InvalidOperationException("Azure DevOps PAT not configured. Run Setup first.");
        if (string.IsNullOrWhiteSpace(c.AzureDevOpsOrg))
            throw new InvalidOperationException("Azure DevOps organization not configured. Run Setup first.");
        var ado = new AzureDevOpsService(c.AzureDevOpsOrg, c.AzureDevOpsPat);
        ILlmProvider llm = LlmProviderFactory.Create(c);
        return new DevOpsPilot(c, ado, llm);
    }

    public static async Task SetupAsync(string? pat = null, string? org = null, string? provider = null, string? model = null, string? apiKey = null)
    {
        var c = JftkConfig.Load();
        if (pat != null) c.AzureDevOpsPat = pat;
        else if (string.IsNullOrWhiteSpace(c.AzureDevOpsPat)) { Console.Write("Azure DevOps PAT: "); c.AzureDevOpsPat = Console.ReadLine()?.Trim(); }
        if (org != null) c.AzureDevOpsOrg = org;
        else if (string.IsNullOrWhiteSpace(c.AzureDevOpsOrg)) { Console.Write("Organization: "); c.AzureDevOpsOrg = Console.ReadLine()?.Trim(); }

        // LLM provider selection
        if (provider != null) c.LlmProvider = provider;
        else
        {
            Console.Write($"LLM provider [ollama/openai/deepseek/groq/xai/lmstudio/custom] (default: {c.LlmProvider ?? "ollama"}): ");
            var p = Console.ReadLine()?.Trim();
            if (!string.IsNullOrWhiteSpace(p)) c.LlmProvider = p;
        }

        if (model != null) { c.OpenAiModel = model; c.OllamaModel = model; }

        var prov = (c.LlmProvider ?? "ollama").ToLowerInvariant();
        if (prov is "openai" or "deepseek" or "groq" or "xai" or "custom")
        {
            if (apiKey != null) c.OpenAiKey = apiKey;
            else if (string.IsNullOrWhiteSpace(c.OpenAiKey))
            {
                Console.Write($"{prov.ToUpperInvariant()} API Key: ");
                c.OpenAiKey = Console.ReadLine()?.Trim();
            }
            if (model != null) c.OpenAiModel = model;
            else if (string.IsNullOrWhiteSpace(c.OpenAiModel))
            {
                var defaultModel = prov switch
                {
                    "openai" => "gpt-4o-mini",
                    "deepseek" => "deepseek-chat",
                    "groq" => "llama-3.3-70b-versatile",
                    "xai" => "grok-2",
                    _ => "default"
                };
                Console.Write($"Model [{defaultModel}]: ");
                var m = Console.ReadLine()?.Trim();
                c.OpenAiModel = string.IsNullOrWhiteSpace(m) ? defaultModel : m;
            }
            if (prov == "custom" && string.IsNullOrWhiteSpace(c.OpenAiBaseUrl))
            {
                Console.Write("Base URL [http://localhost:8080/v1]: ");
                var url = Console.ReadLine()?.Trim();
                c.OpenAiBaseUrl = string.IsNullOrWhiteSpace(url) ? "http://localhost:8080/v1" : url;
            }
        }
        else
        {
            // Ollama / LM Studio
            if (model != null) c.OllamaModel = model;
            else if (string.IsNullOrWhiteSpace(c.OllamaModel))
            {
                Console.Write($"Ollama model [{c.OllamaModel ?? "qwen2.5:7b"}]: ");
                var m = Console.ReadLine()?.Trim();
                if (!string.IsNullOrWhiteSpace(m)) c.OllamaModel = m;
            }
            if (!OllamaSetup.IsInstalled())
                Console.WriteLine($"Ollama not installed. Install: {OllamaSetup.GetInstallInstructions()}");
            else if (!OllamaSetup.HasModel(c.OllamaModel!))
            {
                Console.WriteLine($"Pulling {c.OllamaModel}...");
                Console.WriteLine(await OllamaSetup.PullModelAsync(c.OllamaModel!) ? "OK" : "Failed");
            }
        }

        c.Save();
        Console.WriteLine("Config saved.");
    }

    public async Task<AnalysisReport> AnalyzeAsync(string project) => await _analyzer.AnalyzeAsync(project);
    public async Task<List<DevOpsProject>> ListProjectsAsync() => await _ado.GetProjectsAsync();
    public async Task<List<WorkItem>> ListTasksAsync(string project, string? iteration = null) => await _tasks.ListActiveAsync(project, iteration);
    public async Task<List<WorkItem>> ListMyTasksAsync(string project, string iteration) => await _tasks.ListMyTasksAsync(project, iteration);
    public async Task<WorkItem> AddTaskAsync(string project, string type, string title, string? description = null, string? iteration = null, int? priority = null) => await _tasks.CreateAsync(project, type, title, description, iteration, priority);
    public async Task<WorkItem> UpdateStateAsync(int id, string newState) => await _tasks.UpdateStateAsync(id, newState);
    public async Task<string> SuggestAsync(string project) => await _tasks.SuggestNextAsync(project);
    public async Task<bool> IsLlmAvailableAsync() => await _llm.IsAvailableAsync();
}
