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

            if (result?.Findings is null)
            {
                throw new JsonException("The response did not contain a findings array.");
            }

            return new ReviewResult(
                result.Findings.Where(finding => finding.Confidence >= 0.5).ToList(),
                Success: true,
                FailureReason: null);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not parse review model output: {ModelOutput}", modelOutput);
            return new ReviewResult([], Success: false, FailureReason: "response could not be parsed");
        }
    }

    public ReviewResult FilterToValidLines(
        ReviewResult result,
        Dictionary<string, HashSet<int>> validLines)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(validLines);

        var validFindings = new List<Finding>();
        foreach (var finding in result.Findings)
        {
            if (validLines.TryGetValue(finding.FilePath, out var lines) && lines.Contains(finding.Line))
            {
                validFindings.Add(finding);
                continue;
            }

            _logger.LogWarning(
                "Dropped finding with unresolved diff position: {FilePath}:{Line} - {Message}",
                finding.FilePath,
                finding.Line,
                finding.Message);
        }

        return new ReviewResult(validFindings, result.Success, result.FailureReason, result.UsedFallback);
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
