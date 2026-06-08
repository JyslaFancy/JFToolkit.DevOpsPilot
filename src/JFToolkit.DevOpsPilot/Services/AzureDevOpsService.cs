using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JFToolkit.DevOpsPilot.Models;

namespace JFToolkit.DevOpsPilot.Services;

public class AzureDevOpsService
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _apiVersion;

    /// <summary>
    /// Creates an Azure DevOps / TFS service client.
    /// </summary>
    /// <param name="baseUrl">Base URL, e.g. https://dev.azure.com/myorg or https://tfs.company.com/tfs/DefaultCollection</param>
    /// <param name="pat">Personal Access Token</param>
    /// <param name="apiVersion">REST API version (default 7.1)</param>
    public AzureDevOpsService(string baseUrl, string pat, string apiVersion = "7.1")
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _apiVersion = apiVersion;
        _http = new HttpClient();
        var bytes = Encoding.UTF8.GetBytes($":{pat}");
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(bytes));
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<List<DevOpsProject>> GetProjectsAsync()
    {
        var url = $"{_baseUrl}/_apis/projects?api-version={_apiVersion}";
        var json = await GetAsync(url);
        var items = json.GetProperty("value");
        var projects = new List<DevOpsProject>();
        foreach (var item in items.EnumerateArray())
        {
            projects.Add(new DevOpsProject
            {
                Organization = _baseUrl,
                Name = item.GetProperty("name").GetString() ?? "",
                Description = item.SafeGetString("description")
            });
        }
        return projects;
    }

    public async Task<DevOpsProject?> GetProjectAsync(string project)
    {
        var url = $"{_baseUrl}/_apis/projects/{Uri.EscapeDataString(project)}?includeCapabilities=true&api-version={_apiVersion}";
        var json = await GetAsync(url);
        var name = json.GetProperty("name").GetString() ?? "";
        var desc = json.SafeGetString("description");
        string? processTemplate = null;
        if (json.TryGetProperty("capabilities", out var caps))
            if (caps.TryGetProperty("processTemplate", out var pt))
                processTemplate = pt.GetProperty("templateName").GetString();
        var types = await GetWorkItemTypesAsync(project);

        // Fetch areas and teams in parallel (best-effort — older TFS may not support these)
        var areasTask = GetAreasAsync(project);
        var teamsTask = GetTeamsAsync(project);

        List<AreaNode> areas;
        List<TeamInfo> teams;
        try { areas = await areasTask; } catch { areas = []; }
        try { teams = await teamsTask; } catch { teams = []; }

        return new DevOpsProject
        {
            Organization = _baseUrl, Name = name, Description = desc,
            ProcessTemplate = processTemplate, WorkItemTypes = types,
            Areas = areas, Teams = teams
        };
    }

    public async Task<List<AreaNode>> GetAreasAsync(string project)
    {
        var url = $"{_baseUrl}/{Uri.EscapeDataString(project)}/_apis/wit/classificationnodes/areas?$depth=10&api-version={_apiVersion}";
        var json = await GetAsync(url);
        var root = ParseAreaNode(json, "");
        return root?.Children ?? [];
    }

    private static AreaNode? ParseAreaNode(JsonElement node, string parentPath)
    {
        var name = node.SafeGetString("name");
        if (name == null) return null;
        var path = string.IsNullOrEmpty(parentPath) ? name : $"{parentPath}\\{name}";
        var area = new AreaNode { Name = name, Path = path };

        if (node.TryGetProperty("children", out var children))
            foreach (var child in children.EnumerateArray())
            {
                var childNode = ParseAreaNode(child, path);
                if (childNode != null)
                    area.Children.Add(childNode);
            }

        return area;
    }

    public async Task<List<TeamInfo>> GetTeamsAsync(string project)
    {
        var url = $"{_baseUrl}/_apis/projects/{Uri.EscapeDataString(project)}/teams?api-version={_apiVersion}";
        var json = await GetAsync(url);
        var teams = new List<TeamInfo>();

        foreach (var item in json.GetProperty("value").EnumerateArray())
        {
            var teamId = item.GetProperty("id").GetString() ?? "";
            var teamName = item.GetProperty("name").GetString() ?? "";
            var teamDesc = item.SafeGetString("description");

            // Fetch team members
            var members = new List<string>();
            try
            {
                var membersUrl = $"{_baseUrl}/_apis/projects/{Uri.EscapeDataString(project)}/teams/{teamId}/members?api-version={_apiVersion}";
                var membersJson = await GetAsync(membersUrl);
                foreach (var m in membersJson.GetProperty("value").EnumerateArray())
                {
                    var displayName = m.GetProperty("identity").GetProperty("displayName").GetString();
                    if (displayName != null) members.Add(displayName);
                }
            }
            catch { /* members are best-effort */ }

            // Fetch team area paths (fieldValues)
            var areaPaths = new List<string>();
            try
            {
                var fieldsUrl = $"{_baseUrl}/{Uri.EscapeDataString(project)}/{teamId}/_apis/work/teamsettings/teamfieldvalues?api-version={_apiVersion}";
                var fieldsJson = await GetAsync(fieldsUrl);
                foreach (var f in fieldsJson.GetProperty("values").EnumerateArray())
                {
                    var areaPath = f.GetProperty("value").GetString();
                    if (areaPath != null) areaPaths.Add(areaPath);
                }
            }
            catch { /* area paths are best-effort */ }

            teams.Add(new TeamInfo
            {
                Id = teamId,
                Name = teamName,
                Description = teamDesc,
                Members = members,
                AreaPaths = areaPaths
            });
        }

        return teams;
    }

    public async Task<List<WorkItemType>> GetWorkItemTypesAsync(string project)
    {
        var url = $"{_baseUrl}/{Uri.EscapeDataString(project)}/_apis/wit/workitemtypes?api-version={_apiVersion}";
        var json = await GetAsync(url);
        var types = new List<WorkItemType>();
        foreach (var item in json.GetProperty("value").EnumerateArray())
            types.Add(new WorkItemType { Name = item.GetProperty("name").GetString() ?? "", Description = item.SafeGetString("description") });
        return types;
    }

    internal async Task<List<WorkItemRef>> QueryAsync(string project, string wiql)
    {
        var url = $"{_baseUrl}/{Uri.EscapeDataString(project)}/_apis/wit/wiql?api-version={_apiVersion}";
        var body = JsonSerializer.Serialize(new { query = wiql });
        var json = await PostAsync(url, body);
        var refs = new List<WorkItemRef>();
        foreach (var item in json.GetProperty("workItems").EnumerateArray())
            refs.Add(new WorkItemRef { Id = item.GetProperty("id").GetInt32(), Url = item.GetProperty("url").GetString() ?? "" });
        return refs;
    }

    public async Task<List<WorkItem>> GetWorkItemsAsync(List<int> ids)
    {
        if (ids.Count == 0) return [];
        var idStr = string.Join(",", ids);
        var url = $"{_baseUrl}/_apis/wit/workitems?ids={idStr}&api-version={_apiVersion}&$expand=all";
        var json = await GetAsync(url);
        var items = new List<WorkItem>();
        foreach (var item in json.GetProperty("value").EnumerateArray())
        {
            var fields = item.GetProperty("fields");
            items.Add(new WorkItem
            {
                Id = item.GetProperty("id").GetInt32(),
                Title = fields.SafeGetString("System.Title") ?? "",
                Type = fields.SafeGetString("System.WorkItemType") ?? "",
                State = fields.SafeGetString("System.State") ?? "",
                AssignedTo = fields.TryGetProperty("System.AssignedTo", out var at) && at.ValueKind != JsonValueKind.Null ? at.GetProperty("displayName").GetString() : null,
                IterationPath = fields.SafeGetString("System.IterationPath"),
                AreaPath = fields.SafeGetString("System.AreaPath"),
                Priority = fields.TryGetProperty("Microsoft.VSTS.Common.Priority", out var p) && p.ValueKind != JsonValueKind.Null ? (int?)p.GetInt32() : null,
                Description = fields.SafeGetString("System.Description"),
                Tags = fields.TryGetProperty("System.Tags", out var t) && t.ValueKind != JsonValueKind.Null ? (t.GetString() ?? "").Split(';').Select(x => x.Trim()).Where(x => x.Length > 0).ToList() : [],
                Url = item.SafeGetString("url")
            });
        }
        return items;
    }

    public async Task<WorkItem> CreateWorkItemAsync(string project, string type, string title, string? description = null, string? iterationPath = null, int? priority = null, List<string>? tags = null)
    {
        var url = $"{_baseUrl}/{Uri.EscapeDataString(project)}/_apis/wit/workitems/${type}?api-version={_apiVersion}";
        var ops = new List<object> { new { op = "add", path = "/fields/System.Title", value = title } };
        if (description != null) ops.Add(new { op = "add", path = "/fields/System.Description", value = description });
        if (iterationPath != null) ops.Add(new { op = "add", path = "/fields/System.IterationPath", value = iterationPath });
        if (priority.HasValue) ops.Add(new { op = "add", path = "/fields/Microsoft.VSTS.Common.Priority", value = priority.Value });
        if (tags != null && tags.Count > 0) ops.Add(new { op = "add", path = "/fields/System.Tags", value = string.Join("; ", tags) });
        var body = JsonSerializer.Serialize(ops);
        var json = await PatchAsync(url, body);
        var fields = json.GetProperty("fields");
        return new WorkItem { Id = json.GetProperty("id").GetInt32(), Title = fields.GetProperty("System.Title").GetString() ?? title, Type = fields.GetProperty("System.WorkItemType").GetString() ?? type, State = fields.GetProperty("System.State").GetString() ?? "New", Url = json.SafeGetString("url") };
    }

    public async Task<WorkItem> UpdateWorkItemStateAsync(int id, string newState)
    {
        var url = $"{_baseUrl}/_apis/wit/workitems/{id}?api-version={_apiVersion}";
        var body = JsonSerializer.Serialize(new[] { new { op = "add", path = "/fields/System.State", value = newState } });
        var json = await PatchAsync(url, body);
        var fields = json.GetProperty("fields");
        return new WorkItem { Id = json.GetProperty("id").GetInt32(), Title = fields.GetProperty("System.Title").GetString() ?? "", State = fields.GetProperty("System.State").GetString() ?? newState };
    }

    private async Task<JsonElement> GetAsync(string url)
    {
        try
        {
            var resp = await _http.GetAsync(url);
            resp.EnsureSuccessStatusCode();
            return JsonSerializer.Deserialize<JsonElement>(await resp.Content.ReadAsStringAsync());
        }
        catch (HttpRequestException ex)
        {
            throw new HttpRequestException(BuildErrorMessage("GET", url, ex), ex);
        }
    }

    private async Task<JsonElement> PostAsync(string url, string body)
    {
        try
        {
            var resp = await _http.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"));
            resp.EnsureSuccessStatusCode();
            return JsonSerializer.Deserialize<JsonElement>(await resp.Content.ReadAsStringAsync());
        }
        catch (HttpRequestException ex)
        {
            throw new HttpRequestException(BuildErrorMessage("POST", url, ex), ex);
        }
    }

    private async Task<JsonElement> PatchAsync(string url, string body)
    {
        try
        {
            var resp = await _http.PatchAsync(url, new StringContent(body, Encoding.UTF8, "application/json-patch+json"));
            resp.EnsureSuccessStatusCode();
            return JsonSerializer.Deserialize<JsonElement>(await resp.Content.ReadAsStringAsync());
        }
        catch (HttpRequestException ex)
        {
            throw new HttpRequestException(BuildErrorMessage("PATCH", url, ex), ex);
        }
    }

    private static string BuildErrorMessage(string method, string url, HttpRequestException ex)
    {
        var statusCode = ex.StatusCode?.ToString() ?? "unknown";
        var hints = statusCode switch
        {
            "NotFound" => " — Check: project name, API version (older TFS may need --api-version 5.0 or 4.0), or if the resource exists",
            "Unauthorized" => " — Check: PAT is valid and has Read access",
            "Forbidden" => " — Check: PAT has required scopes (Work Items: Read)",
            _ => ""
        };
        return $"{method} {url} → HTTP {(int?)ex.StatusCode} {statusCode}{hints}";
    }
}

internal sealed record WorkItemRef { public int Id { get; init; } public string Url { get; init; } = ""; }

internal static class JsonHelp
{
    public static string? SafeGetString(this JsonElement e, string name)
    {
        if (e.TryGetProperty(name, out var p) && p.ValueKind != JsonValueKind.Null) return p.GetString();
        return null;
    }
}
