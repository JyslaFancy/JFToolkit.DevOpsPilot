namespace JFToolkit.DevOpsPilot.Services;

public interface ILlmProvider
{
    Task<bool> IsAvailableAsync();
    Task<string> CompleteAsync(string systemPrompt, string userPrompt);
}
