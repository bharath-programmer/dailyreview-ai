using DailyReview.Core.Models;
using Microsoft.Extensions.Logging;

namespace DailyReview.Core.Services;

public sealed class FallbackReviewClient : IReviewModelClient
{
    private readonly IReviewModelClient _primary;
    private readonly IReviewModelClient _fallback;
    private readonly ILogger<FallbackReviewClient> _logger;

    public FallbackReviewClient(
        IReviewModelClient primary,
        IReviewModelClient fallback,
        ILogger<FallbackReviewClient> logger)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(fallback);
        ArgumentNullException.ThrowIfNull(logger);

        _primary = primary;
        _fallback = fallback;
        _logger = logger;
    }

    public async Task<ReviewResult> ReviewAsync(string diffContext, CancellationToken ct = default)
    {
        ReviewResult primaryResult;
        try
        {
            primaryResult = await _primary.ReviewAsync(diffContext, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Primary LLM provider failed unexpectedly.");
            return new ReviewResult([], Success: false, FailureReason: "primary provider failed");
        }

        if (primaryResult.Success || primaryResult.FailureReason != "rate_limit_exceeded")
        {
            return primaryResult;
        }

        _logger.LogWarning("Primary provider rate limited, falling back to secondary provider");
        var fallbackResult = await _fallback.ReviewAsync(diffContext, ct);
        return fallbackResult with { UsedFallback = true };
    }
}
