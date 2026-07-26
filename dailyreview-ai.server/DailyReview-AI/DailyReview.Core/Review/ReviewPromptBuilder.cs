using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DailyReview.Core.Review;

public sealed class ReviewPromptBuilder
{
    private const string DefaultSystemPrompt = "Review the pull request diff and return only a JSON object containing code-review findings.";
    private readonly IConfiguration _configuration;
    private readonly ILogger<ReviewPromptBuilder> _logger;

    public ReviewPromptBuilder(IConfiguration configuration, ILogger<ReviewPromptBuilder> logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        _configuration = configuration;
        _logger = logger;
    }

    public string BuildPrompt(string diffContext) => $"{GetSystemPrompt()}\n\n{diffContext}";

    private string GetSystemPrompt()
    {
        var configuredPrompt = _configuration["SystemPrompt:Override"];
        if (!string.IsNullOrWhiteSpace(configuredPrompt))
        {
            return configuredPrompt;
        }

        var promptPath = Path.Combine(AppContext.BaseDirectory, "Prompts", "system-prompt.txt");
        if (File.Exists(promptPath))
        {
            return File.ReadAllText(promptPath);
        }

        _logger.LogWarning("System prompt file was not found at {PromptPath}; using the default system prompt.", promptPath);
        return DefaultSystemPrompt;
    }
}
