using System.Text;
using JFToolkit.DevOpsPilot.Models;

namespace JFToolkit.DevOpsPilot.Services;

public class TaskService
{
    private readonly AzureDevOpsService _ado;
    private readonly ILlmProvider _llm;

    public TaskService(AzureDevOpsService ado, ILlmProvider llm) { _ado = ado; _llm = llm; }

    public async Task<WorkItem> CreateAsync(string project, string type, string title, string? description = null, string? iteration = null, int? priority = null)
        => await _ado.CreateWorkItemAsync(project, type, title, description, iteration, priority);

    public async Task<WorkItem> UpdateStateAsync(int id, string newState)
        => await _ado.UpdateWorkItemStateAsync(id, newState);

    public async Task<List<WorkItem>> ListActiveAsync(string project, string? iterationPath = null)
    {
        var filter = iterationPath is not null ? $"AND [System.IterationPath] = '{iterationPath}'" : "";
        var wiql = $"SELECT [System.Id], [System.Title], [System.State], [System.WorkItemType], [System.AssignedTo] FROM WorkItems WHERE [System.TeamProject] = @project AND [System.State] <> 'Closed' AND [System.State] <> 'Removed' AND [System.State] <> 'Done' {filter} ORDER BY [System.Id]";
        var refs = await _ado.QueryAsync(project, wiql);
        if (refs.Count == 0) return [];
        return await _ado.GetWorkItemsAsync(refs.Select(r => r.Id).ToList());
    }

    public async Task<List<WorkItem>> ListMyTasksAsync(string project, string currentIteration)
    {
        var wiql = $"SELECT [System.Id], [System.Title], [System.State], [System.WorkItemType] FROM WorkItems WHERE [System.TeamProject] = @project AND [System.IterationPath] = '{currentIteration}' AND [System.State] <> 'Closed' AND [System.State] <> 'Done' AND [System.AssignedTo] = @me ORDER BY [Microsoft.VSTS.Common.Priority] ASC, [System.Id]";
        var refs = await _ado.QueryAsync(project, wiql);
        if (refs.Count == 0) return [];
        return await _ado.GetWorkItemsAsync(refs.Select(r => r.Id).ToList());
    }

    public async Task<string> SuggestNextAsync(string project)
    {
        var active = await ListActiveAsync(project);
        var sb = new StringBuilder();
        sb.AppendLine("Active work items:");
        foreach (var wi in active.Take(50))
            sb.AppendLine($"  #{wi.Id} [{wi.Type}] {wi.State}: {wi.Title} (P:{wi.Priority} A:{wi.AssignedTo ?? "none"})");
        return await _llm.CompleteAsync("You are a DevOps assistant. Suggest the top 3 items to focus on and why. Be concise.", sb.ToString());
    }
}
