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
        ILlmProvider llm = new OllamaProvider(c.OllamaModel ?? "qwen2.5:7b", c.OllamaUrl ?? "http://localhost:11434");
        return new DevOpsPilot(c, ado, llm);
    }

    public static async Task SetupAsync(string? pat = null, string? org = null, string? model = null)
    {
        var c = JftkConfig.Load();
        if (pat != null) c.AzureDevOpsPat = pat;
        else if (string.IsNullOrWhiteSpace(c.AzureDevOpsPat)) { Console.Write("Azure DevOps PAT: "); c.AzureDevOpsPat = Console.ReadLine()?.Trim(); }
        if (org != null) c.AzureDevOpsOrg = org;
        else if (string.IsNullOrWhiteSpace(c.AzureDevOpsOrg)) { Console.Write("Organization: "); c.AzureDevOpsOrg = Console.ReadLine()?.Trim(); }
        if (model != null) c.OllamaModel = model;
        if (!OllamaSetup.IsInstalled())
            Console.WriteLine($"Ollama not installed. Install: {OllamaSetup.GetInstallInstructions()}");
        else if (!OllamaSetup.HasModel(c.OllamaModel!))
        {
            Console.WriteLine($"Pulling {c.OllamaModel}...");
            Console.WriteLine(await OllamaSetup.PullModelAsync(c.OllamaModel!) ? "OK" : "Failed");
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
