using System.Text;
using System.Text.Json;

namespace JFToolkit.DevOpsPilot.Services;

public class OllamaProvider : ILlmProvider
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly string _baseUrl;

    public OllamaProvider(string model = "qwen2.5:7b", string baseUrl = "http://localhost:11434")
    {
        _model = model;
        _baseUrl = baseUrl.TrimEnd('/');
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    }

    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            var resp = await _http.GetAsync($"{_baseUrl}/api/tags");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt)
    {
        var body = JsonSerializer.Serialize(new
        {
            model = _model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            stream = false
        });

        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync($"{_baseUrl}/api/chat", content);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("message").GetProperty("content").GetString()?.Trim() ?? "";
    }
}
