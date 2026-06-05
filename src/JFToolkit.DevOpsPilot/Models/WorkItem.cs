namespace JFToolkit.DevOpsPilot.Models;

public sealed class WorkItem
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Type { get; set; } = "";
    public string State { get; set; } = "";
    public string? AssignedTo { get; set; }
    public string? IterationPath { get; set; }
    public string? AreaPath { get; set; }
    public int? Priority { get; set; }
    public string? Description { get; set; }
    public List<string> Tags { get; set; } = [];
    public string? Url { get; set; }
}
