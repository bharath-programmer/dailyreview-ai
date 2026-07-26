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
    private readonly ReviewResponseParser _responseParser;
    private readonly ILogger<OpenAiCompatibleReviewClient> _logger;

    public OpenAiCompatibleReviewClient(
        HttpClient httpClient,
        IConfiguration configuration,
        ReviewResponseParser responseParser,
        ILogger<OpenAiCompatibleReviewClient> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(responseParser);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _configuration = configuration;
        _responseParser = responseParser;
        _logger = logger;
    }

    public async Task<ReviewResult> ReviewAsync(string diffContext, CancellationToken ct = default)
    {
        var baseUrl = _configuration["LlmProvider:BaseUrl"];
        var apiKey = _configuration["LlmProvider:ApiKey"];
        var model = _configuration["LlmProvider:Model"];
        if (string.IsNullOrWhiteSpace(baseUrl) ||
            string.IsNullOrWhiteSpace(apiKey) ||
            string.IsNullOrWhiteSpace(model))
        {
            _logger.LogError("LlmProvider:BaseUrl, LlmProvider:ApiKey, and LlmProvider:Model must be configured.");
            return EmptyResult();
        }

        var reasoningEffort = _configuration["LlmProvider:ReasoningEffort"];
        var maxTokens = int.TryParse(_configuration["LlmProvider:MaxTokens"], out var configuredMaxTokens)
            ? configuredMaxTokens
            : 2000;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            if (string.IsNullOrWhiteSpace(reasoningEffort))
            {
                request.Content = JsonContent.Create(new
                {
                    model,
                    messages = new[] { new { role = "user", content = diffContext } },
                    max_tokens = maxTokens
                });
            }
            else
            {
                request.Content = JsonContent.Create(new
                {
                    model,
                    messages = new[] { new { role = "user", content = diffContext } },
                    max_tokens = maxTokens,
                    reasoning_effort = reasoningEffort
                });
            }

            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError(
                    "LLM review request failed with status {StatusCode}: {ErrorBody}",
                    (int)response.StatusCode,
                    errorBody);
                return EmptyResult();
            }

            var completion = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(cancellationToken: ct);
            var content = completion?.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogError("LLM response did not contain choices[0].message.content.");
                return EmptyResult();
            }

            return _responseParser.Parse(content);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "LLM review request failed unexpectedly.");
            return EmptyResult();
        }
    }

    private static ReviewResult EmptyResult() => new([]);

    private sealed record ChatCompletionResponse(
        [property: JsonPropertyName("choices")] List<ChatChoice>? Choices);

    private sealed record ChatChoice(
        [property: JsonPropertyName("message")] ChatMessage? Message);

    private sealed record ChatMessage(
        [property: JsonPropertyName("content")] string? Content);
}
