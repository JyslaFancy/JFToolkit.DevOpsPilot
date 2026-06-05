using System.Text.Json;
using JFToolkit.DevOpsPilot.Services;

namespace JFToolkit.DevOpsPilot.Chat;

/// <summary>
/// Interactive chat agent that uses an LLM to understand natural language
/// and execute Azure DevOps operations via DevOpsPilot.
/// </summary>
public class ChatAgent
{
    private readonly ILlmProvider _llm;
    private readonly DevOpsPilot _pilot;
    private readonly List<ChatMessage> _history = [];
    private string _currentProject = "";

    private const string SystemPrompt = """
        You are a DevOps assistant with access to an Azure DevOps project.
        You can perform these actions, but only when the user explicitly requests them:

        - scan: Analyze the project workflow and give recommendations
        - list: Show active work items (optionally filtered by iteration)
        - mine: Show work items assigned to the current user in a specific iteration
        - add: Create a new work item (type: Task/Bug/User Story, title, optional description)
        - done: Close/mark-complete a work item by ID
        - suggest: Analyze the current state and suggest what to work on next

        RULES:
        1. When the user asks a question that requires data (status, what to do, etc.),
           call the appropriate action FIRST, then summarize the results in Norwegian.
        2. If the user just wants to chat, respond conversationally in Norwegian.
        3. Keep responses concise — 2-4 sentences max unless listing items.
        4. Never make up work item IDs or statuses. Only report what the tool returns.

        You MUST respond in this exact JSON format:
        {
          "action": "chat" | "scan" | "list" | "mine" | "add" | "done" | "suggest",
          "args": {},
          "message": "Your natural language response in Norwegian"
        }

        For "add": args = { "type": "Task", "title": "...", "description": "..." }
        For "done": args = { "id": 12345 }
        For "list": args = { "iteration": "Sprint 12" }  (optional)
        For "mine": args = { "iteration": "Sprint 12" }
        For "scan"/"suggest"/"chat": args = {}

        If no action is needed, use "chat" and provide a helpful message.
        """;

    public ChatAgent(ILlmProvider llm, DevOpsPilot pilot)
    {
        _llm = llm;
        _pilot = pilot;
    }

    /// <summary>Process a user message and return the assistant's response.</summary>
    public async Task<string> SendAsync(string userMessage)
    {
        _history.Add(new ChatMessage("user", userMessage));

        var projectContext = string.IsNullOrEmpty(_currentProject)
            ? ""
            : $"Current project: {_currentProject}\n";

        var userPrompt = $"{projectContext}User message: {userMessage}";

        // First call: LLM decides what action to take
        var llmResponse = await _llm.CompleteAsync(SystemPrompt, userPrompt);
        var action = ParseAction(llmResponse);
        var message = action.message ?? "Beklager, jeg forstod ikke helt. Kan du omformulere?";

        // If action requires data, execute it and feed results back
        if (action.action != "chat" && !string.IsNullOrEmpty(_currentProject))
        {
            try
            {
                var result = await ExecuteActionAsync(action.action, action.args);
                // Feed result back to LLM for a natural summary
                var summaryPrompt = string.Format("""
                    Previous action result for project '{0}':
                    {1}

                    User originally asked: "{2}"
                    Summarize this result in Norwegian. Be concise.
                    Respond in JSON: {{ "action": "chat", "args": {{}}, "message": "..." }}
                    """, _currentProject, result, userMessage);
                var summary = await _llm.CompleteAsync(SystemPrompt, summaryPrompt);
                var summaryAction = ParseAction(summary);
                message = summaryAction.message ?? message;
            }
            catch (Exception ex)
            {
                message = $"Feil: {ex.Message}";
            }
        }
        else if (action.action != "chat")
        {
            message = "Du må først velge et prosjekt. Si for eksempel 'analyser MyProject'.";
        }

        _history.Add(new ChatMessage("assistant", message));
        return message;
    }

    /// <summary>Detect and set the current project from user message.</summary>
    public string? DetectProject(string userMessage)
    {
        // Simple heuristic: look for known project names or "analyser X" / "scan X"
        var lower = userMessage.ToLowerInvariant();
        var patterns = new[] { "analyser ", "scan ", "list ", "prosjekt ", "project " };
        foreach (var p in patterns)
        {
            var idx = lower.IndexOf(p, StringComparison.Ordinal);
            if (idx >= 0)
            {
                var after = userMessage[(idx + p.Length)..].Trim();
                var word = after.Split(' ')[0].Trim('"', '\'', '.', ',', '!', '?');
                if (word.Length > 0)
                {
                    _currentProject = word;
                    return word;
                }
            }
        }
        return null;
    }

    private static (string action, JsonElement args, string? message) ParseAction(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var action = root.TryGetProperty("action", out var a) ? a.GetString() ?? "chat" : "chat";
            var args = root.TryGetProperty("args", out var ar) ? ar.Clone() : default;
            var message = root.TryGetProperty("message", out var m) ? m.GetString() : null;
            return (action, args, message);
        }
        catch
        {
            // If JSON parsing fails, treat the whole response as a chat message
            return ("chat", default, json.Trim());
        }
    }

    private async Task<string> ExecuteActionAsync(string action, JsonElement args)
    {
        switch (action)
        {
            case "scan":
                var report = await _pilot.AnalyzeAsync(_currentProject);
                return $"Workflow: {report.WorkflowType}, Sprint: {report.SprintLengthDays}d, " +
                       $"Active: {report.ActiveWorkItemCount} (Bugs:{report.OpenBugs} Tasks:{report.OpenTasks} Stories:{report.OpenUserStories}). " +
                       $"Recommendations: {string.Join("; ", report.Recommendations)}";

            case "list":
                var iter = args.TryGetProperty("iteration", out var it) ? it.GetString() : null;
                var items = await _pilot.ListTasksAsync(_currentProject, iter);
                if (items.Count == 0) return "Ingen aktive work items.";
                var list = items.Select(i => $"#{i.Id} [{i.Type}] {i.State}: {i.Title}" + (i.AssignedTo != null ? $" ({i.AssignedTo})" : ""));
                return string.Join("\n", list);

            case "mine":
                var mineIter = args.TryGetProperty("iteration", out var mi) && mi.ValueKind != JsonValueKind.Null
                    ? mi.GetString()! : "";
                var myItems = await _pilot.ListMyTasksAsync(_currentProject, mineIter);
                if (myItems.Count == 0) return $"Ingen tasks tilordnet deg i '{mineIter}'.";
                return string.Join("\n", myItems.Select(i => $"#{i.Id} [{i.Type}] {i.State}: {i.Title}"));

            case "add":
                var type = args.TryGetProperty("type", out var tp) ? tp.GetString() ?? "Task" : "Task";
                var title = args.TryGetProperty("title", out var t) ? t.GetString() : null;
                if (string.IsNullOrWhiteSpace(title)) return "Mangler tittel for ny work item.";
                var desc = args.TryGetProperty("description", out var d) ? d.GetString() : null;
                var wi = await _pilot.AddTaskAsync(_currentProject, type, title, desc);
                return $"Opprettet #{wi.Id} [{wi.Type}] {wi.State}: {wi.Title}";

            case "done":
                if (args.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var id))
                {
                    var done = await _pilot.UpdateStateAsync(id, "Closed");
                    return $"#{done.Id} satt til {done.State}.";
                }
                return "Mangler eller ugyldig work item ID.";

            case "suggest":
                return await _pilot.SuggestAsync(_currentProject);

            default:
                return "Ukjent handling.";
        }
    }

    private record ChatMessage(string Role, string Content);
}
