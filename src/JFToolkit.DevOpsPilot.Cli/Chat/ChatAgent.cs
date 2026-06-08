using System.Text.Json;
using JFToolkit.DevOpsPilot.Memory;
using JFToolkit.DevOpsPilot.Services;

namespace JFToolkit.DevOpsPilot.Chat;

/// <summary>
/// Interactive chat agent — secretary persona that proactively keeps the developer
/// organized and informed about their Azure DevOps project.
/// Uses MemPalace for cross-session memory.
/// </summary>
public class ChatAgent
{
    private readonly ILlmProvider _llm;
    private readonly DevOpsPilot _pilot;
    private readonly MemPalace _mem;
    private readonly List<ChatMessage> _history = [];
    private string _currentProject = "";
    private int _sessionId;
    private int _seq;

    private const string SecretarySystemPrompt = """
        You are PILOT — a proactive DevOps secretary for an Azure DevOps project.
        You keep the developer organized, informed, and focused on what matters.

        YOUR PERSONALITY:
        - Warm and professional, like a trusted executive assistant who knows the project
        - Proactive: you anticipate needs. If you see something worth mentioning, say it.
          Don't wait to be asked the perfect question.
        - Context-aware: you remember what was discussed and follow up naturally.
          "You closed #42 — want to pick up #38 next?"
        - Efficient: prioritize. Don't dump 20 items — highlight the 3-5 that matter most.
        - Direct: you take action. The user should never need to know which "command" to use.
        - Honest: never invent IDs, statuses, or data. Only report what the tools return.
          If you don't know something, say so.

        YOUR CAPABILITIES (actions you can take):
        - scan:   Analyze project workflow, spot bottlenecks, give recommendations
        - list:   Show active work items (all, or filtered by iteration)
        - mine:   Show work items assigned to the current user
        - add:    Create a new work item (Task, Bug, User Story)
        - done:   Close/complete a work item by ID
        - suggest: Analyze the board and recommend what to work on next

        BEHAVIOR RULES:
        1. At session start, you receive a BRIEFING with the user's tasks and project state.
           Use it to greet them warmly and surface what's important right away.
        2. When the user asks something that needs data, fetch it SILENTLY — just do it.
           NEVER say "use the list command" or "you can run scan". You ARE the tool.
        3. If the user says "what should I do?" or seems unsure, proactively suggest
           based on priorities, deadlines, and blockers.
        4. After completing an action (closing a task, creating one), naturally suggest
           the logical next step.
        5. Keep responses warm but concise. Use bullet points for lists of 3+ items.
        6. The user may type in Norwegian — you understand both languages,
           but always respond in English.

        RESPONSE FORMAT — you MUST respond in this exact JSON:
        {
          "action": "chat" | "scan" | "list" | "mine" | "add" | "done" | "suggest",
          "args": {},
          "message": "Your warm, secretary-style response in English"
        }

        Action args:
          "add":     { "type": "Task", "title": "...", "description": "..." }
          "done":    { "id": 12345 }
          "list":    { "iteration": "Sprint 12" }   (optional)
          "mine":    { "iteration": "Sprint 12" }
          "scan" / "suggest" / "chat":  {}

        If no action is needed, use "chat" and be conversational.
        """;

    public ChatAgent(ILlmProvider llm, DevOpsPilot pilot, MemPalace mem)
    {
        _llm = llm;
        _pilot = pilot;
        _mem = mem;
    }

    /// <summary>
    /// Start a new session for the given project — fetches a live briefing,
    /// loads recent chat history, and injects project memories into context.
    /// </summary>
    public async Task<string> StartSessionAsync(string project)
    {
        _currentProject = project;
        _sessionId = _mem.CreateSession(project);
        _seq = 0;

        // Load recent messages from past sessions
        var recent = _mem.LoadRecentMessages(project, count: 20);
        _history.AddRange(recent);

        // Inject project memory
        var memoryContext = _mem.BuildMemoryContext(project);
        if (!string.IsNullOrEmpty(memoryContext))
            _history.Insert(0, new ChatMessage("system", memoryContext.TrimEnd()));

        // ── Build live briefing ──
        string briefing;
        try
        {
            briefing = await BuildBriefingAsync(project);
        }
        catch
        {
            briefing = $"Project '{project}' is active. I'm ready to help — just ask!";
        }

        // Inject briefing as an invisible system message the LLM SEES as context,
        // but the user doesn't see as a separate message
        _history.Add(new ChatMessage("system",
            $"SESSION BRIEFING (inject naturally into your first greeting):\n{briefing}"));

        return briefing;
    }

    /// <summary>Process a user message and return the assistant's response.</summary>
    public async Task<string> SendAsync(string userMessage)
    {
        // Guard: never try to save messages without a valid session
        if (!HasSession)
            return "I need a project first. Say something like 'analyze MyProject' and I'll get us set up.";

        _seq++;

        // ── Slash commands ──
        var slashResult = HandleSlashCommand(userMessage.Trim());
        if (slashResult != null)
        {
            _mem.SaveMessage(_sessionId, _seq, "user", userMessage);
            _seq++;
            _mem.SaveMessage(_sessionId, _seq, "assistant", slashResult);
            _history.Add(new ChatMessage("user", userMessage));
            _history.Add(new ChatMessage("assistant", slashResult));
            return slashResult;
        }

        _history.Add(new ChatMessage("user", userMessage));
        _mem.SaveMessage(_sessionId, _seq, "user", userMessage);

        var projectContext = string.IsNullOrEmpty(_currentProject)
            ? ""
            : $"Current project: {_currentProject}\n";

        var userPrompt = $"{projectContext}User message: {userMessage}";

        // ── First LLM call: decide action ──
        var llmResponse = await _llm.CompleteAsync(SecretarySystemPrompt, userPrompt);
        var action = ParseAction(llmResponse);
        var message = action.message ?? "I'm sorry, I didn't quite catch that. Could you rephrase?";

        // ── Execute action if needed ──
        if (action.action != "chat" && !string.IsNullOrEmpty(_currentProject))
        {
            try
            {
                var result = await ExecuteActionAsync(action.action, action.args);

                // Feed results back to LLM for a polished secretary-style summary
                var summaryPrompt = $"""
                    Previous action ({action.action}) result for project '{_currentProject}':
                    {result}

                    User originally asked: "{userMessage}"

                    Summarize this naturally in your secretary voice.
                    Prioritize — highlight what's urgent, what's blocked, what's next.
                    Be warm and proactive. Suggest a logical next step if appropriate.

                    Respond in JSON: {"{"} "action": "chat", "args": {"{"}{"}"}, "message": "..." {"}"}
                    """;

                var summary = await _llm.CompleteAsync(SecretarySystemPrompt, summaryPrompt);
                var summaryAction = ParseAction(summary);
                message = summaryAction.message ?? message;
            }
            catch (Exception ex)
            {
                message = $"I ran into an issue: {ex.Message}. Let me know if you want me to try again.";
            }
        }
        else if (action.action != "chat")
        {
            message = "I'd love to help with that, but I need to know which project first. " +
                      "Just tell me the project name — for example 'analyze MyProject'.";
        }

        _seq++;
        _mem.SaveMessage(_sessionId, _seq, "assistant", message);
        _history.Add(new ChatMessage("assistant", message));
        return message;
    }

    /// <summary>Detect and set the current project from user message.</summary>
    public string? DetectProject(string userMessage)
    {
        var lower = userMessage.ToLowerInvariant();
        var patterns = new[] { "analyse ", "analyser ", "analyze ", "scan ", "list ", "prosjekt ", "project " };
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

    public bool HasSession => _sessionId != 0;

    // ── Private helpers ──

    /// <summary>Fetch both user tasks and project overview for the session briefing.</summary>
    private async Task<string> BuildBriefingAsync(string project)
    {
        var lines = new List<string>();

        // 1. Get user's tasks (mine)
        try
        {
            // Try to find current iteration from active items
            var allItems = await _pilot.ListTasksAsync(project);
            var iteration = allItems
                .Where(i => i.IterationPath != null)
                .GroupBy(i => i.IterationPath)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault()?.Key;

            if (iteration != null)
            {
                var myItems = await _pilot.ListMyTasksAsync(project, iteration);
                if (myItems.Count > 0)
                {
                    lines.Add($"YOUR TASKS in '{iteration}':");
                    foreach (var i in myItems)
                        lines.Add($"  #{i.Id} [{i.State}] {i.Type}: {i.Title}");
                }
                else
                {
                    lines.Add($"You have no tasks assigned in '{iteration}'.");
                }
            }
        }
        catch { /* briefing is best-effort */ }

        // 2. Quick project overview (active items, top priorities)
        try
        {
            var active = await _pilot.ListTasksAsync(project);
            if (active.Count > 0)
            {
                var bugs = active.Count(i => i.Type == "Bug");
                var tasks = active.Count(i => i.Type == "Task");
                var stories = active.Count(i => i.Type is "User Story" or "Product Backlog Item");
                lines.Add($"PROJECT OVERVIEW: {active.Count} active items ({bugs} bugs, {tasks} tasks, {stories} stories)");

                // Show a few highest-priority or interesting items
                var highlights = active
                    .Where(i => i.Priority <= 2 || i.State is "In Progress" or "Active")
                    .Take(5).ToList();
                if (highlights.Count > 0)
                {
                    lines.Add("Notable items:");
                    foreach (var i in highlights)
                        lines.Add($"  #{i.Id} [{i.State}] {i.Type}: {i.Title}" +
                                  (i.AssignedTo != null ? $" → {i.AssignedTo}" : ""));
                }
            }
            else
            {
                lines.Add("PROJECT OVERVIEW: No active work items. A clean slate!");
            }
        }
        catch { /* best-effort */ }

        return string.Join('\n', lines);
    }

    /// <summary>Handle built-in slash commands for memory management.</summary>
    private string? HandleSlashCommand(string input)
    {
        if (!input.StartsWith('/')) return null;

        var parts = input[1..].Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        var cmd = parts[0].ToLowerInvariant();
        var rest = parts.Length > 1 ? parts[1] : "";

        if (string.IsNullOrEmpty(_currentProject))
            return "I need a project first. Say something like 'analyze MyProject' and I'll get us set up.";

        switch (cmd)
        {
            case "memory":
            case "mem":
                var mems = _mem.GetAllMemories(_currentProject);
                if (mems.Count == 0)
                    return $"No saved memories for '{_currentProject}'.\nUse /remember <key> <value> to store useful facts.";
                var lines = new List<string> { $"**Memories for {_currentProject}:**" };
                foreach (var (k, v) in mems)
                    lines.Add($"  • **{k}**: {v}");
                return string.Join('\n', lines);

            case "remember":
            case "rem":
                var eqIdx = rest.IndexOf('=');
                if (eqIdx < 0) { eqIdx = rest.IndexOf(' '); }
                if (eqIdx < 0)
                    return "Use: /remember <key> <value>\nExample: /remember CI uses GitHub Actions";
                var key = rest[..eqIdx].Trim();
                var val = rest[(eqIdx + 1)..].Trim();
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(val))
                    return "Both key and value are required.";
                _mem.Remember(_currentProject, key, val);
                return $"✓ Got it: **{key}** = {val}";

            case "forget":
                if (string.IsNullOrWhiteSpace(rest))
                    return "Use: /forget <key>";
                _mem.Forget(_currentProject, rest.Trim());
                return $"✓ Forgotten: **{rest.Trim()}**";

            case "history":
            case "hist":
                var count = 10;
                if (int.TryParse(rest, out var n) && n > 0 && n <= 50)
                    count = n;
                var recent = _mem.LoadRecentMessages(_currentProject, count);
                if (recent.Count == 0)
                    return "No previous messages for this project.";
                var histLines = new List<string> { $"**Last {recent.Count} messages for {_currentProject}:**" };
                foreach (var m in recent)
                    histLines.Add($"  [{m.Role}] {Truncate(m.Content, 120)}");
                return string.Join('\n', histLines);

            case "sessions":
                var sessions = _mem.ListSessions(_currentProject);
                if (sessions.Count == 0)
                    return $"No previous sessions for '{_currentProject}'.";
                var sessLines = new List<string> { $"**Sessions for {_currentProject}:**" };
                foreach (var s in sessions)
                    sessLines.Add($"  #{s.Id} — {s.Title} ({s.MessageCount} messages, {s.CreatedAt})");
                return string.Join('\n', sessLines);

            default:
                return null;
        }
    }

    private static (string action, JsonElement args, string? message) ParseAction(string json)
    {
        try
        {
            var t = json.Trim();
            if (t.StartsWith("```"))
            {
                var end = t.IndexOf('\n');
                if (end > 0) t = t[(end + 1)..];
            }
            if (t.EndsWith("```"))
                t = t[..^3].TrimEnd();

            using var doc = JsonDocument.Parse(t);
            var root = doc.RootElement;
            var action = root.TryGetProperty("action", out var a) ? a.GetString() ?? "chat" : "chat";
            var args = root.TryGetProperty("args", out var ar) ? ar.Clone() : default;
            var message = root.TryGetProperty("message", out var m) ? m.GetString() : null;
            return (action, args, message);
        }
        catch
        {
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
                if (items.Count == 0) return "No active work items.";
                var list = items.Select(i =>
                    $"#{i.Id} [{i.Type}] {i.State}: {i.Title}" +
                    (i.AssignedTo != null ? $" ({i.AssignedTo})" : "") +
                    (i.Priority > 0 ? $" P{i.Priority}" : ""));
                return string.Join("\n", list);

            case "mine":
                var mineIter = args.TryGetProperty("iteration", out var mi) && mi.ValueKind != JsonValueKind.Null
                    ? mi.GetString()! : "";
                var myItems = await _pilot.ListMyTasksAsync(_currentProject, mineIter);
                if (myItems.Count == 0) return $"No tasks assigned to you in '{mineIter}'.";
                return string.Join("\n", myItems.Select(i =>
                    $"#{i.Id} [{i.Type}] {i.State}: {i.Title} P{i.Priority}"));

            case "add":
                var type = args.TryGetProperty("type", out var tp) ? tp.GetString() ?? "Task" : "Task";
                var title = args.TryGetProperty("title", out var t) ? t.GetString() : null;
                if (string.IsNullOrWhiteSpace(title)) return "Missing title for new work item.";
                var desc = args.TryGetProperty("description", out var d) ? d.GetString() : null;
                var wi = await _pilot.AddTaskAsync(_currentProject, type, title, desc);
                return $"Created #{wi.Id} [{wi.Type}] {wi.State}: {wi.Title}";

            case "done":
                if (args.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var id))
                {
                    var done = await _pilot.UpdateStateAsync(id, "Closed");
                    return $"#{done.Id} → {done.State}.";
                }
                return "Missing or invalid work item ID.";

            case "suggest":
                return await _pilot.SuggestAsync(_currentProject);

            default:
                return "Unknown action.";
        }
    }

    private static string Truncate(string text, int maxLen)
        => text.Length <= maxLen ? text : text[..(maxLen - 3)] + "...";
}
