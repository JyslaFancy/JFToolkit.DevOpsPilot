namespace JFToolkit.DevOpsPilot.Models;

/// <summary>
/// Represents an Azure DevOps project with its detected workflow configuration.
/// </summary>
public sealed record DevOpsProject
{
    public string Organization { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public string? ProcessTemplate { get; init; }
    public List<WorkItemType> WorkItemTypes { get; init; } = [];
}

public sealed record WorkItemType
{
    public string Name { get; init; } = "";
    public string? Description { get; init; }
}
