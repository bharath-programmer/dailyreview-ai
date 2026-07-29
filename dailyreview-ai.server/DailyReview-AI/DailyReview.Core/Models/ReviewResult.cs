namespace DailyReview.Core.Models;

public record ReviewResult(
    List<Finding> Findings,
    bool Success = true,
    string? FailureReason = null,
    bool UsedFallback = false);
