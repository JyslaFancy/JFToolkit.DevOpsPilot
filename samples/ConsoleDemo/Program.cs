using JFToolkit.DevOpsPilot;
using JFToolkit.DevOpsPilot.Models;

if (args.Length == 0) { Usage(); return; }

var cmd = args[0].ToLowerInvariant();

try
{
    switch (cmd)
    {
        case "setup":
            await DevOpsPilot.SetupAsync();
            break;
        case "scan":
            Require(args, 1, "scan <project>");
            await PrintReport((await DevOpsPilot.Create().AnalyzeAsync(args[1])));
            break;
        case "list":
            if (args.Length >= 2 && args[1] == "projects")
            {
                foreach (var p in await DevOpsPilot.Create().ListProjectsAsync())
                    Console.WriteLine($"  {p.Name}  {p.Description}");
            }
            else
            {
                Require(args, 1, "list <project> [iteration]");
                PrintItems(await DevOpsPilot.Create().ListTasksAsync(args[1], args.Length > 2 ? args[2] : null));
            }
            break;
        case "mine":
            Require(args, 1, "mine <project> <iteration>"); Require(args, 2, "mine <project> <iteration>");
            Console.WriteLine($"My tasks in '{args[2]}':");
            PrintItems(await DevOpsPilot.Create().ListMyTasksAsync(args[1], args[2]));
            break;
        case "add":
            Require(args, 1, "add <project> <type> <title>");
            var type = args.Length > 2 ? args[2] : "Task";
            var title = string.Join(" ", args.Skip(args.Length > 2 ? 3 : 2));
            if (string.IsNullOrWhiteSpace(title)) { Console.WriteLine("Title required."); return; }
            var wi = await DevOpsPilot.Create().AddTaskAsync(args[1], type, title);
            Console.WriteLine($"OK  #{wi.Id} [{wi.Type}] {wi.State}: {wi.Title}");
            break;
        case "done":
            Require(args, 1, "done <id>");
            if (!int.TryParse(args[1], out var id)) { Console.WriteLine("Invalid ID."); return; }
            var d = await DevOpsPilot.Create().UpdateStateAsync(id, "Closed");
            Console.WriteLine($"OK  #{d.Id} -> {d.State}");
            break;
        case "suggest":
            Require(args, 1, "suggest <project>");
            Console.WriteLine(await DevOpsPilot.Create().SuggestAsync(args[1]));
            break;
        default:
            Console.WriteLine($"Unknown: {cmd}");
            Usage();
            break;
    }
}
catch (Exception ex) { Console.WriteLine($"Error: {ex.Message}"); }

static void Usage()
{
    Console.WriteLine("JFToolkit.DevOpsPilot — Azure DevOps + Ollama");
    Console.WriteLine("  setup                      configure PAT + org");
    Console.WriteLine("  scan <project>             analyze project workflow");
    Console.WriteLine("  list projects              list all projects");
    Console.WriteLine("  list <project> [iter]      list active work items");
    Console.WriteLine("  mine <project> <iter>      list my work items");
    Console.WriteLine("  add <project> <type> <title>");
    Console.WriteLine("  done <work-item-id>");
    Console.WriteLine("  suggest <project>          LLM suggests next task");
}
static void Require(string[] a, int i, string usage) { if (a.Length <= i) { Console.WriteLine($"Usage: {usage}"); Environment.Exit(1); } }
static async Task PrintReport(AnalysisReport r)
{
    Console.WriteLine($"Workflow: {r.WorkflowType}  Sprint: {r.SprintLengthDays}d  Active: {r.ActiveWorkItemCount}");
    Console.WriteLine($"  Bugs:{r.OpenBugs} Tasks:{r.OpenTasks} Stories:{r.OpenUserStories}");
    if (r.Recommendations.Count > 0) { Console.WriteLine("Recommendations:"); foreach (var x in r.Recommendations) Console.WriteLine($"  -> {x}"); }
}
static void PrintItems(List<WorkItem> items) { foreach (var i in items) Console.WriteLine($"  #{i.Id,-6} [{i.Type,-12}] {i.State,-10} {i.AssignedTo ?? "",-10} {i.Title}"); }
