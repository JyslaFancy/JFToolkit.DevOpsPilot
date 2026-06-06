using System.Text;
using System.Text.Json;

namespace JFToolkit.DevOpsPilot.Services;

/// <summary>
/// LLM provider for OpenAI API and OpenAI-compatible endpoints
/// (DeepSeek, Groq, LM Studio, Azure OpenAI, xAI, etc.).
/// </summary>
public class OpenAiProvider : ILlmProvider
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly string _baseUrl;
    private readonly string _apiKey;

    public OpenAiProvider(string apiKey, string model = "gpt-4o-mini",
                          string? baseUrl = null)
    {
        _apiKey = apiKey;
        _model = model;
        _baseUrl = (baseUrl ?? "https://api.openai.com").TrimEnd('/');
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
    }

    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/v1/models");
            req.Headers.Add("Authorization", $"Bearer {_apiKey}");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var resp = await _http.SendAsync(req, cts.Token);
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
            }
        });

        var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/chat/completions")
        {
            Content = content
        };
        req.Headers.Add("Authorization", $"Bearer {_apiKey}");

        var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var choices = doc.RootElement.GetProperty("choices");
        if (choices.GetArrayLength() == 0)
            return "";

        return choices[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString()?.Trim() ?? "";
    }
}
