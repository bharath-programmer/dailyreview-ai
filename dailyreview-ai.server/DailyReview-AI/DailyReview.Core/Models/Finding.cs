namespace DailyReview.Core.Models;

public record Finding(
    string FilePath,
    int Line,
    string Severity,
    string Category,
    string Message,
    double Confidence);
