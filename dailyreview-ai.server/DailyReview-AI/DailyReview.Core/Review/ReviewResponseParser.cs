using System.Text.Json;
using DailyReview.Core.Models;
using Microsoft.Extensions.Logging;

namespace DailyReview.Core.Review;

public sealed class ReviewResponseParser
{
    private readonly ILogger<ReviewResponseParser> _logger;

    public ReviewResponseParser(ILogger<ReviewResponseParser> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public ReviewResult Parse(string modelOutput)
    {
        try
        {
            var json = ExtractJson(modelOutput ?? string.Empty);
            var result = JsonSerializer.Deserialize<ReviewResult>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return new ReviewResult(result?.Findings
                ?.Where(finding => finding.Confidence >= 0.5)
                .ToList() ?? []);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not parse review model output: {ModelOutput}", modelOutput);
            return new ReviewResult([]);
        }
    }

    private static string ExtractJson(string modelOutput)
    {
        var output = modelOutput.Trim();
        if (output.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = output.IndexOf('\n');
            output = firstLineEnd >= 0 ? output[(firstLineEnd + 1)..] : string.Empty;

            var closingFence = output.LastIndexOf("```", StringComparison.Ordinal);
            if (closingFence >= 0)
            {
                output = output[..closingFence];
            }
        }

        var firstBrace = output.IndexOf('{');
        var lastBrace = output.LastIndexOf('}');
        return firstBrace >= 0 && lastBrace > firstBrace
            ? output[firstBrace..(lastBrace + 1)]
            : output;
    }
}
