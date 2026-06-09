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

        // Determine base URL: explicit URL takes priority, fall back to dev.azure.com/{org}
        var baseUrl = !string.IsNullOrWhiteSpace(c.AzureDevOpsUrl)
            ? c.AzureDevOpsUrl
            : c.AzureDevOpsOrg is not null
                ? $"https://dev.azure.com/{c.AzureDevOpsOrg}"
                : throw new InvalidOperationException("Azure DevOps organization or URL not configured. Run Setup first.");

        var apiVersion = c.AzureDevOpsApiVersion ?? "7.1";
        var ado = new AzureDevOpsService(baseUrl, c.AzureDevOpsPat, apiVersion);
        ILlmProvider llm = LlmProviderFactory.Create(c);
        return new DevOpsPilot(c, ado, llm);
    }

    public static async Task SetupAsync(
        string? pat = null, string? org = null, string? adoUrl = null,
        string? apiVersion = null, string? provider = null,
        string? model = null, string? apiKey = null,
        bool autoModel = false)
    {
        var c = JftkConfig.Load();
        if (pat != null) c.AzureDevOpsPat = pat;
        else if (string.IsNullOrWhiteSpace(c.AzureDevOpsPat)) { Console.Write("Azure DevOps PAT: "); c.AzureDevOpsPat = Console.ReadLine()?.Trim(); }

        // If URL is explicitly provided, use it (TFS on-prem). Otherwise prompt for org (cloud).
        if (adoUrl != null)
        {
            c.AzureDevOpsUrl = adoUrl;
        }
        else if (org != null)
        {
            c.AzureDevOpsOrg = org;
        }
        else if (string.IsNullOrWhiteSpace(c.AzureDevOpsUrl) && string.IsNullOrWhiteSpace(c.AzureDevOpsOrg))
        {
            Console.Write("Azure DevOps org (leave empty for TFS/on-prem URL): ");
            var input = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(input))
            {
                Console.Write("TFS / Azure DevOps Server URL (e.g. https://tfs.company.com/tfs/DefaultCollection): ");
                c.AzureDevOpsUrl = Console.ReadLine()?.Trim();
            }
            else
            {
                c.AzureDevOpsOrg = input;
            }
        }

        if (apiVersion != null) c.AzureDevOpsApiVersion = apiVersion;
        else if (!string.IsNullOrWhiteSpace(c.AzureDevOpsUrl) && string.IsNullOrWhiteSpace(c.AzureDevOpsApiVersion))
        {
            Console.Write($"API version [7.1]: ");
            var v = Console.ReadLine()?.Trim();
            if (!string.IsNullOrWhiteSpace(v)) c.AzureDevOpsApiVersion = v;
        }

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
            // ─── Ollama / local LLM ──────────────────────
            if (!OllamaSetup.IsInstalled())
            {
                Console.WriteLine($"Ollama not installed. Install: {OllamaSetup.GetInstallInstructions()}");
                Console.WriteLine("Install Ollama first, then re-run setup.");
                c.LlmProvider = "ollama";
                c.Save();
                Console.WriteLine("Partial config saved (Ollama setup pending).");
                return;
            }

            // Hardware detection + model recommendation
            var hw = HardwareDetector.Detect();
            Console.WriteLine();
            Console.WriteLine("  ── System hardware ──");
            Console.WriteLine($"  {hw}");

            if (autoModel && model == null)
            {
                // Auto-pick the best model
                var best = ModelRecommender.GetRecommendations(hw).First();
                c.OllamaModel = best.Model;
                Console.WriteLine();
                Console.WriteLine($"  Auto-selected model: {best.Model}");
                Console.WriteLine($"  Reason: {best.Reason}");
            }
            else if (model != null)
            {
                c.OllamaModel = model;
            }
            else
            {
                // Show recommendations and let user choose
                var localModels = OllamaSetup.ListLocalModels();
                var recommendations = ModelRecommender.GetRecommendations(hw);

                Console.WriteLine();
                Console.WriteLine("  ── Recommended models for your hardware ──");

                int idx = 1;
                var shown = new HashSet<string>();
                foreach (var rec in recommendations)
                {
                    if (shown.Add(rec.Model))
                    {
                        var alreadyPulled = localModels.Contains(rec.Model, StringComparer.OrdinalIgnoreCase)
                            ? " [already downloaded]"
                            : "";
                        Console.WriteLine($"  [{idx}] {rec.Model,-20} — {rec.Reason}{alreadyPulled}");
                        idx++;
                    }
                }

                // Show any locally pulled models not in recommendations
                foreach (var lm in localModels)
                {
                    if (!shown.Contains(lm, StringComparer.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"  [{idx}] {lm,-20} — already downloaded");
                        shown.Add(lm);
                        idx++;
                    }
                }

                Console.WriteLine();
                Console.Write($"  Choose model number, or type a model name [{c.OllamaModel ?? "qwen2.5:7b"}]: ");
                var choice = Console.ReadLine()?.Trim();

                if (!string.IsNullOrWhiteSpace(choice))
                {
                    // Try to parse as number first
                    if (int.TryParse(choice, out var num) && num >= 1 && num <= shown.Count)
                    {
                        c.OllamaModel = shown.ElementAt(num - 1);
                    }
                    else
                    {
                        c.OllamaModel = choice; // custom model name
                    }
                }
            }

            // Pull model if missing
            if (!OllamaSetup.HasModel(c.OllamaModel!))
            {
                Console.WriteLine();
                Console.WriteLine($"  Pulling {c.OllamaModel}...");
                var success = await OllamaSetup.PullModelAsync(c.OllamaModel!);
                Console.WriteLine(success ? $"  ✓ {c.OllamaModel} ready!" : $"  ✗ Failed to pull {c.OllamaModel}. Pull manually with 'ollama pull {c.OllamaModel}'.");
            }
            else
            {
                Console.WriteLine($"  ✓ {c.OllamaModel} is already available.");
            }
        }

        c.Save();
        Console.WriteLine();
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
