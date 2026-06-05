using JFToolkit.DevOpsPilot;
using JFToolkit.DevOpsPilot.Chat;
using JFToolkit.DevOpsPilot.Models;

if (args.Length == 0)
{
    PrintLogo();
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

        case "chat":
            await RunChatAsync(args);
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

static void PrintLogo()
{
    Console.WriteLine(@"
       __.---.__
      (__ o   o __)
       (_______)
      //       \\
     || GOOGLES ||
   ╔═╩═══════════╩════════════════════════════════╗
   ║    ____            ____             ____     ║
   ║   / __ \___ _   __/ __ \____  _____/ __ \    ║
   ║  / / / / _ \ | / / / / / __ \/ ___/ /_/ /    ║
   ║ / /_/ /  __/ |/ / /_/ / /_/ (__  ) ____/     ║
   ║/_____/\___/|___/\____/ .___/____/_/           ║
   ║                     /_/                       ║
   ╠═══════════════════════════════════════════════╣
   ║   Azure DevOps + Lokal AI  •  devops-pilot    ║
   ╚═══════════════════════════════════════════════╝
");
}

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  devops-pilot setup");
    Console.WriteLine("  devops-pilot scan <project>              Analyze workflow (Scrum/Kanban/etc.)");
    Console.WriteLine("  devops-pilot list projects                List all projects");
    Console.WriteLine("  devops-pilot list <project> [iteration]   List active work items");
    Console.WriteLine("  devops-pilot mine <project> <iteration>   Your work items in a sprint");
    Console.WriteLine("  devops-pilot add <project> <type> <title> Create a work item");
    Console.WriteLine("  devops-pilot done <id>                    Close a work item");
    Console.WriteLine("  devops-pilot suggest <project>            LLM suggests what to work on");
    Console.WriteLine("  devops-pilot chat [project]               Interactive AI chat about your tasks");
    Console.WriteLine();
    Console.WriteLine("Install: dotnet tool install -g JFToolkit.DevOpsPilot");
    Console.WriteLine("Requires: Ollama (optional, for scan/suggest/chat), Azure DevOps PAT");
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

static async Task RunChatAsync(string[] args)
{
    PrintLogo();
    var pilot = DevOpsPilot.Create();

    var available = await pilot.IsLlmAvailableAsync();
    if (!available)
    {
        Console.WriteLine("Ollama is not running. Start it with 'ollama serve' and try again.");
        return;
    }

    var config = JFToolkit.DevOpsPilot.Config.JftkConfig.Load();
    var llm = new JFToolkit.DevOpsPilot.Services.OllamaProvider(
        config.OllamaModel ?? "qwen2.5:7b",
        config.OllamaUrl ?? "http://localhost:11434");

    var agent = new ChatAgent(llm, pilot);

    var project = args.ElementAtOrDefault(1);
    if (project != null)
    {
        agent.DetectProject($"scan {project}");
        Console.WriteLine($"Prosjekt: {project}");
    }
    else
    {
        Console.WriteLine("Prosjekt ikke angitt. Si 'analyser <navn>' i chatten for å velge.");
    }

    Console.WriteLine("Skriv /exit for å avslutte.\n");

    while (true)
    {
        Console.Write("> ");
        var input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input)) continue;
        if (input.Trim() == "/exit") break;

        agent.DetectProject(input);

        Console.Write("\n");
        var response = await agent.SendAsync(input);
        Console.WriteLine(response);
        Console.WriteLine();
    }

    Console.WriteLine("Ha det!");
}
