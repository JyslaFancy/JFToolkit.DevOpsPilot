using JFToolkit.DevOpsPilot;
using JFToolkit.DevOpsPilot.Models;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

var cmd = args[0].ToLowerInvariant();

try
{
    switch (cmd)
    {
        case "setup":
            await DevOpsPilot.SetupAsync();
            break;

        case "scan":
            Require(args, 1, "devops-pilot scan <project>");
            var pilot = DevOpsPilot.Create();
            Console.WriteLine($"Analyzing '{args[1]}'...\n");
            var report = await pilot.AnalyzeAsync(args[1]);
            PrintReport(report);
            break;

        case "list":
            if (args.Length >= 2 && args[1] == "projects")
            {
                foreach (var p in await DevOpsPilot.Create().ListProjectsAsync())
                    Console.WriteLine($"  {p.Name}  {p.Description ?? "(no description)"}");
            }
            else
            {
                Require(args, 1, "devops-pilot list <project> [iteration]");
                var items = await DevOpsPilot.Create().ListTasksAsync(args[1], args.ElementAtOrDefault(2));
                PrintItems(items);
            }
            break;

        case "mine":
            Require(args, 1, "devops-pilot mine <project> <iteration>");
            Require(args, 2, "devops-pilot mine <project> <iteration>");
            Console.WriteLine($"My tasks in '{args[2]}':\n");
            PrintItems(await DevOpsPilot.Create().ListMyTasksAsync(args[1], args[2]));
            break;

        case "add":
            Require(args, 1, "devops-pilot add <project> <type> <title>");
            var type = args.Length > 2 ? args[2] : "Task";
            var title = string.Join(" ", args.Skip(args.Length > 2 ? 3 : 2));
            if (string.IsNullOrWhiteSpace(title)) { Console.Error.WriteLine("Error: Title required."); return 1; }
            var wi = await DevOpsPilot.Create().AddTaskAsync(args[1], type, title);
            Console.WriteLine($"✓ Created #{wi.Id} [{wi.Type}] {wi.State}: {wi.Title}");
            break;

        case "done":
            Require(args, 1, "devops-pilot done <id>");
            if (!int.TryParse(args[1], out var id)) { Console.Error.WriteLine("Error: Invalid work item ID."); return 1; }
            var d = await DevOpsPilot.Create().UpdateStateAsync(id, "Closed");
            Console.WriteLine($"✓ #{d.Id} → {d.State}");
            break;

        case "suggest":
            Require(args, 1, "devops-pilot suggest <project>");
            Console.WriteLine("Analyzing with LLM...\n");
            Console.WriteLine(await DevOpsPilot.Create().SuggestAsync(args[1]));
            break;

        default:
            Console.Error.WriteLine($"Unknown command: {cmd}");
            PrintUsage();
            return 1;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

return 0;

// ── Helpers ──

static void PrintUsage()
{
    Console.WriteLine("JFToolkit.DevOpsPilot — Azure DevOps + local LLM (Ollama)");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  devops-pilot setup");
    Console.WriteLine("  devops-pilot scan <project>              Analyze workflow (Scrum/Kanban/etc.)");
    Console.WriteLine("  devops-pilot list projects                List all projects");
    Console.WriteLine("  devops-pilot list <project> [iteration]   List active work items");
    Console.WriteLine("  devops-pilot mine <project> <iteration>   Your work items in a sprint");
    Console.WriteLine("  devops-pilot add <project> <type> <title> Create a work item");
    Console.WriteLine("  devops-pilot done <id>                    Close a work item");
    Console.WriteLine("  devops-pilot suggest <project>            LLM suggests what to work on");
    Console.WriteLine();
    Console.WriteLine("Install: dotnet tool install -g JFToolkit.DevOpsPilot");
    Console.WriteLine("Requires: Ollama (optional, for scan/suggest), Azure DevOps PAT");
}

static void Require(string[] a, int idx, string usage)
{
    if (a.Length <= idx)
    {
        Console.Error.WriteLine($"Usage: {usage}");
        Environment.Exit(1);
    }
}

static void PrintReport(AnalysisReport r)
{
    Console.WriteLine($"  Workflow:        {r.WorkflowType}");
    Console.WriteLine($"  Sprint length:   {r.SprintLengthDays} days");
    Console.WriteLine($"  Active items:    {r.ActiveWorkItemCount}");
    Console.WriteLine($"    Bugs:          {r.OpenBugs}");
    Console.WriteLine($"    Tasks:         {r.OpenTasks}");
    Console.WriteLine($"    User Stories:  {r.OpenUserStories}");
    if (r.CurrentIteration != null)
        Console.WriteLine($"  Current sprint:  {r.CurrentIteration}");

    if (r.Recommendations.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("  Recommendations:");
        foreach (var rec in r.Recommendations)
            Console.WriteLine($"    → {rec}");
    }
}

static void PrintItems(List<WorkItem> items)
{
    if (items.Count == 0)
    {
        Console.WriteLine("  (no items)");
        return;
    }

    foreach (var i in items)
    {
        var assigned = i.AssignedTo is not null ? $" [{i.AssignedTo}]" : "";
        Console.WriteLine($"  #{i.Id,-6} [{i.Type,-12}] {i.State,-10}{assigned} {i.Title}");
    }
    Console.WriteLine($"  — {items.Count} item(s) —");
}
