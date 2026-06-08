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

        // Include area/team structure for smarter analysis
        if (project.Areas.Count > 0)
        {
            summary.AppendLine("Area Paths:");
            foreach (var area in project.Areas)
                summary.AppendLine(FormatAreaTree(area, 1));
        }
        if (project.Teams.Count > 0)
        {
            summary.AppendLine("Teams:");
            foreach (var team in project.Teams)
            {
                var areas = team.AreaPaths.Count > 0 ? $" (areas: {string.Join(", ", team.AreaPaths)})" : "";
                var members = team.Members.Count > 0 ? $" [{string.Join(", ", team.Members)}]" : "";
                summary.AppendLine($"  {team.Name}{areas}{members}");
            }
        }

        foreach (var wi in workItems.Take(80))
            summary.AppendLine($"  #{wi.Id} [{wi.Type}] {wi.State}: {wi.Title}");

        var llmResponse = await _llm.CompleteAsync(
            "You are a DevOps analyst. You MUST respond with ONLY a raw JSON object — no markdown, no code fences, no explanation. " +
            "The JSON object must have exactly these fields: workflowType (one of: Agile Scrum, Kanban, Basic, Custom), sprintLengthDays (integer 7-30), recommendations (array of 2-5 short strings).",
            summary.ToString());

        // Strip markdown code fences if model wrapped the response
        var cleaned = StripMarkdownJson(llmResponse);

        try
        {
            using var doc = JsonDocument.Parse(cleaned);
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
                CurrentIteration = iter, Recommendations = recs, RawAnalysis = llmResponse,
                AreaCount = project.Areas.Count, TeamCount = project.Teams.Count
            };
        }
        catch (JsonException jex)
        {
            return new AnalysisReport
            {
                WorkflowType = project.ProcessTemplate ?? "Unknown",
                ActiveWorkItemCount = workItems.Count,
                RawAnalysis = llmResponse,
                Recommendations = [$"LLM JSON parsing failed: {jex.Message[..Math.Min(jex.Message.Length, 100)]}. Raw: {Trunc(llmResponse, 200)}"]
            };
        }
        catch (Exception ex)
        {
            return new AnalysisReport
            {
                WorkflowType = project.ProcessTemplate ?? "Unknown",
                ActiveWorkItemCount = workItems.Count,
                RawAnalysis = llmResponse,
                Recommendations = [$"Analysis failed: {ex.Message}"]
            };
        }
    }

    /// <summary>
    /// Strip markdown code fences (```json ... ```) from LLM responses.
    /// Many models wrap JSON in code blocks even when told not to.
    /// </summary>
    private static string StripMarkdownJson(string text)
    {
        var t = text.Trim();
        // Strip leading ```json or ``` fences
        if (t.StartsWith("```"))
        {
            var end = t.IndexOf('\n');
            if (end > 0) t = t[(end + 1)..];
        }
        // Strip trailing ``` fences
        if (t.EndsWith("```"))
            t = t[..^3].TrimEnd();
        return t.Trim();
    }

    private static string Trunc(string s, int max) => s.Length <= max ? s : s[..max] + "...";

    private static string FormatAreaTree(AreaNode node, int depth)
    {
        var indent = new string(' ', depth * 2);
        var line = $"{indent}{node.Name}";
        if (node.Children.Count == 0) return line;
        var children = string.Join("\n", node.Children.Select(c => FormatAreaTree(c, depth + 1)));
        return $"{line}\n{children}";
    }
}
