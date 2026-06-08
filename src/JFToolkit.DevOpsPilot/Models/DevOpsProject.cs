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
    public List<AreaNode> Areas { get; init; } = [];
    public List<TeamInfo> Teams { get; init; } = [];
}

public sealed record WorkItemType
{
    public string Name { get; init; } = "";
    public string? Description { get; init; }
}

/// <summary>
/// Represents an Area Path node in the classification hierarchy.
/// </summary>
public sealed record AreaNode
{
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";    // Full path e.g. "PowerProducts\\Hardware\\Sensors"
    public List<AreaNode> Children { get; init; } = [];
}

/// <summary>
/// Represents a Team with its members.
/// </summary>
public sealed record TeamInfo
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public List<string> Members { get; init; } = [];  // Display names
    public List<string> AreaPaths { get; init; } = []; // Area paths owned by this team
}
