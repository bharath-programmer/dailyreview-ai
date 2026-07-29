using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DailyReview.Core.Models;
using DailyReview.Core.Review;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DailyReview.Core.Services;

public sealed class OpenAiCompatibleReviewClient : IReviewModelClient
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly string _configurationSection;
    private readonly ReviewResponseParser _responseParser;
    private readonly ReviewPromptBuilder _promptBuilder;
    private readonly ILogger<OpenAiCompatibleReviewClient> _logger;

    public OpenAiCompatibleReviewClient(
        HttpClient httpClient,
        IConfiguration configuration,
        ReviewResponseParser responseParser,
        ReviewPromptBuilder promptBuilder,
        ILogger<OpenAiCompatibleReviewClient> logger,
        string configurationSection = "LlmProvider")
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(responseParser);
        ArgumentNullException.ThrowIfNull(promptBuilder);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationSection);

        _httpClient = httpClient;
        _configuration = configuration;
        _configurationSection = configurationSection;
        _responseParser = responseParser;
        _promptBuilder = promptBuilder;
        _logger = logger;
    }

    public async Task<ReviewResult> ReviewAsync(string diffContext, CancellationToken ct = default)
    {
        var providerConfiguration = _configuration.GetSection(_configurationSection);
        var baseUrl = providerConfiguration["BaseUrl"];
        var apiKey = providerConfiguration["ApiKey"];
        var model = providerConfiguration["Model"];
        if (string.IsNullOrWhiteSpace(baseUrl) ||
            string.IsNullOrWhiteSpace(apiKey) ||
            string.IsNullOrWhiteSpace(model))
        {
            _logger.LogError("{ProviderSection}:BaseUrl, ApiKey, and Model must be configured.", _configurationSection);
            return FailureResult("LLM provider is not configured");
        }

        var reasoningEffort = providerConfiguration["ReasoningEffort"];
        var maxTokens = int.TryParse(providerConfiguration["MaxTokens"], out var configuredMaxTokens)
            ? configuredMaxTokens
            : 2000;
        var useJsonMode = bool.TryParse(providerConfiguration["JsonMode"], out var configuredJsonMode) &&
                          configuredJsonMode;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = JsonContent.Create(new ChatCompletionRequest(
                model,
                [
                    new ChatMessageRequest("system", _promptBuilder.GetSystemPrompt()),
                    new ChatMessageRequest("user", diffContext)
                ],
                maxTokens,
                0.2,
                string.IsNullOrWhiteSpace(reasoningEffort) ? null : reasoningEffort,
                useJsonMode ? new JsonResponseFormat("json_object") : null));

            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError(
                    "LLM review request failed with status {StatusCode}: {ErrorBody}",
                    (int)response.StatusCode,
                    errorBody);
                return FailureResult(GetHttpFailureReason(response.StatusCode));
            }

            var completion = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(cancellationToken: ct);
            var content = completion?.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogError("LLM response did not contain choices[0].message.content.");
                return FailureResult("response could not be parsed");
            }

            return _responseParser.Parse(content);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            _logger.LogError(exception, "LLM review request timed out.");
            return FailureResult("request timed out");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "LLM review request failed unexpectedly.");
            return FailureResult("LLM request failed");
        }
    }

    private static ReviewResult FailureResult(string reason) => new([], Success: false, FailureReason: reason);

    private static string GetHttpFailureReason(System.Net.HttpStatusCode statusCode) =>
        statusCode == System.Net.HttpStatusCode.TooManyRequests
            ? "rate_limit_exceeded"
            : (int)statusCode >= 500
                ? "LLM provider is temporarily unavailable"
                : "LLM request failed";

    private sealed record ChatCompletionResponse(
        [property: JsonPropertyName("choices")] List<ChatChoice>? Choices);

    private sealed record ChatChoice(
        [property: JsonPropertyName("message")] ChatMessage? Message);

    private sealed record ChatMessage(
        [property: JsonPropertyName("content")] string? Content);

    private sealed record ChatCompletionRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] List<ChatMessageRequest> Messages,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("reasoning_effort"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ReasoningEffort,
        [property: JsonPropertyName("response_format"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonResponseFormat? ResponseFormat);

    private sealed record ChatMessageRequest(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record JsonResponseFormat(
        [property: JsonPropertyName("type")] string Type);
}
