namespace JFToolkit.DevOpsPilot.Models;

public sealed record AnalysisReport
{
    public string WorkflowType { get; init; } = "";
    public int SprintLengthDays { get; init; }
    public int ActiveWorkItemCount { get; init; }
    public int OpenBugs { get; init; }
    public int OpenTasks { get; init; }
    public int OpenUserStories { get; init; }
    public string? CurrentIteration { get; init; }
    public List<string> Recommendations { get; init; } = [];
    public string RawAnalysis { get; init; } = "";
}
