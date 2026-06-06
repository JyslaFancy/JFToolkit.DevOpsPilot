namespace JFToolkit.DevOpsPilot.Services;

/// <summary>
/// Creates the correct ILlmProvider based on configuration.
/// Supports Ollama, OpenAI, DeepSeek, LM Studio, and any OpenAI-compatible endpoint.
/// </summary>
public static class LlmProviderFactory
{
    /// <summary>
    /// Create an LLM provider from config.
    /// </summary>
    /// <param name="config">JftkConfig with LlmProvider, keys, and URLs.</param>
    /// <returns>The configured ILlmProvider, defaulting to Ollama if unspecified.</returns>
    public static ILlmProvider Create(Config.JftkConfig config)
    {
        var provider = config.LlmProvider?.ToLowerInvariant() ?? "ollama";

        return provider switch
        {
            "openai" => new OpenAiProvider(
                config.OpenAiKey ?? "",
                config.OpenAiModel ?? "gpt-4o-mini"),

            "deepseek" => new OpenAiProvider(
                config.OpenAiKey ?? "",
                config.OpenAiModel ?? "deepseek-chat",
                baseUrl: "https://api.deepseek.com/v1"),

            "groq" => new OpenAiProvider(
                config.OpenAiKey ?? "",
                config.OpenAiModel ?? "llama-3.3-70b-versatile",
                baseUrl: "https://api.groq.com/openai/v1"),

            "xai" => new OpenAiProvider(
                config.OpenAiKey ?? "",
                config.OpenAiModel ?? "grok-2",
                baseUrl: "https://api.x.ai/v1"),

            "lmstudio" => new OpenAiProvider(
                "lm-studio",
                config.OpenAiModel ?? "local-model",
                baseUrl: config.OllamaUrl ?? "http://localhost:1234/v1"),

            // Custom OpenAI-compatible endpoint (user sets base URL + key)
            "custom" => new OpenAiProvider(
                config.OpenAiKey ?? "",
                config.OpenAiModel ?? "default",
                baseUrl: config.OpenAiBaseUrl ?? "http://localhost:8080/v1"),

            // Default: Ollama (local)
            _ => new OllamaProvider(
                config.OllamaModel ?? "qwen2.5:7b",
                config.OllamaUrl ?? "http://localhost:11434")
        };
    }
}
