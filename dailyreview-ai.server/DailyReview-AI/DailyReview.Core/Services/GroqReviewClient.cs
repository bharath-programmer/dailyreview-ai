using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DailyReview.Core.Models;
using DailyReview.Core.Review;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DailyReview.Core.Services;

public sealed class GroqReviewClient : IReviewModelClient
{
    private const string ChatCompletionsEndpoint = "https://api.groq.com/openai/v1/chat/completions";
    private const string DefaultModel = "openai/gpt-oss-120b";
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ReviewResponseParser _responseParser;
    private readonly ILogger<GroqReviewClient> _logger;

    public GroqReviewClient(
        HttpClient httpClient,
        IConfiguration configuration,
        ReviewResponseParser responseParser,
        ILogger<GroqReviewClient> logger)
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
        var apiKey = _configuration["Groq:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Groq:ApiKey is not configured; skipping the AI review.");
            return EmptyResult();
        }

        var model = _configuration["Groq:Model"];
        if (string.IsNullOrWhiteSpace(model))
        {
            model = DefaultModel;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, ChatCompletionsEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = JsonContent.Create(new
            {
                model,
                messages = new[]
                {
                    new { role = "user", content = diffContext }
                }
            });

            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError(
                    "Groq review request failed with status {StatusCode}: {ErrorBody}",
                    (int)response.StatusCode,
                    errorBody);
                return EmptyResult();
            }

            var completion = await response.Content.ReadFromJsonAsync<GroqCompletionResponse>(cancellationToken: ct);
            var content = completion?.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogWarning("Groq review response did not contain choices[0].message.content.");
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
            _logger.LogError(exception, "Groq review request failed unexpectedly.");
            return EmptyResult();
        }
    }

    private static ReviewResult EmptyResult() => new([]);

    private sealed record GroqCompletionResponse(
        [property: JsonPropertyName("choices")] List<GroqChoice>? Choices);

    private sealed record GroqChoice(
        [property: JsonPropertyName("message")] GroqMessage? Message);

    private sealed record GroqMessage(
        [property: JsonPropertyName("content")] string? Content);
}
