using System.Text;
using System.Text.Json;
using JFToolkit.DevOpsPilot.Models;
using static JFToolkit.DevOpsPilot.Services.JsonHelp;

namespace JFToolkit.DevOpsPilot.Services;

public class ProjectAnalyzer
{
    private readonly ILlmProvider _llm;
    private readonly AzureDevOpsService _ado;

    public ProjectAnalyzer(ILlmProvider llm, AzureDevOpsService ado)
    { _llm = llm; _ado = ado; }

    public async Task<AnalysisReport> AnalyzeAsync(string projectName)
    {
        var project = await _ado.GetProjectAsync(projectName)
            ?? throw new InvalidOperationException($"Project '{projectName}' not found.");

        var wiql = "SELECT [System.Id], [System.Title], [System.State], [System.WorkItemType], [System.IterationPath] FROM WorkItems WHERE [System.TeamProject] = @project ORDER BY [System.Id]";
        var refs = await _ado.QueryAsync(projectName, wiql);
        var topRefs = refs.Take(200).ToList();
        var workItems = topRefs.Count > 0 ? await _ado.GetWorkItemsAsync(topRefs.Select(r => r.Id).ToList()) : [];

        var summary = new StringBuilder();
        summary.AppendLine($"Project: {project.Name}");
        summary.AppendLine($"Process: {project.ProcessTemplate ?? "Unknown"}");
        summary.AppendLine($"Types: {string.Join(", ", project.WorkItemTypes.Select(t => t.Name))}");
        summary.AppendLine($"Work Items: {workItems.Count}");
        foreach (var wi in workItems.Take(80))
            summary.AppendLine($"  #{wi.Id} [{wi.Type}] {wi.State}: {wi.Title}");

        var llmResponse = await _llm.CompleteAsync(
            "You are a DevOps analyst. Return ONLY valid JSON with: workflowType (Agile Scrum/Kanban/Basic/Custom), sprintLengthDays (int), recommendations (string array). No markdown.",
            summary.ToString());

        try
        {
            using var doc = JsonDocument.Parse(llmResponse);
            var root = doc.RootElement;
            int bugs = 0, tasks = 0, stories = 0;
            string? iter = null;
            foreach (var wi in workItems)
            {
                if (wi.State is not "Closed" and not "Removed" and not "Done")
                {
                    switch (wi.Type) { case "Bug": bugs++; break; case "Task": tasks++; break; case "User Story": stories++; break; }
                }
                iter ??= wi.IterationPath;
            }
            var recs = new List<string>();
            if (root.TryGetProperty("recommendations", out var rl))
                foreach (var r in rl.EnumerateArray()) recs.Add(r.GetString() ?? "");
            return new AnalysisReport
            {
                WorkflowType = root.SafeGetString("workflowType") ?? "Unknown",
                SprintLengthDays = root.TryGetProperty("sprintLengthDays", out var sld) ? sld.GetInt32() : 0,
                ActiveWorkItemCount = workItems.Count, OpenBugs = bugs, OpenTasks = tasks, OpenUserStories = stories,
                CurrentIteration = iter, Recommendations = recs, RawAnalysis = llmResponse
            };
        }
        catch
        {
            return new AnalysisReport { WorkflowType = project.ProcessTemplate ?? "Unknown", ActiveWorkItemCount = workItems.Count, RawAnalysis = llmResponse };
        }
    }
}
