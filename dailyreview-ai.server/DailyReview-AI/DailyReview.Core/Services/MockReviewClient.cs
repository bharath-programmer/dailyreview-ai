using DailyReview.Core.Models;

namespace DailyReview.Core.Services;

public sealed class MockReviewClient : IReviewModelClient
{
    public async Task<ReviewResult> ReviewAsync(string diffContext, CancellationToken ct = default)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(200), ct);

        return new ReviewResult(
        [
            new Finding(
                "src/Services/OrderService.cs",
                42,
                "high",
                "bug",
                "Potential null reference: customer may be null before accessing its Email property.",
                0.91),
            new Finding(
                "src/Controllers/ReportController.cs",
                68,
                "medium",
                "performance",
                "The asynchronous export operation is started without await, so failures may be lost.",
                0.84),
            new Finding(
                "src/Models/userprofile.cs",
                7,
                "low",
                "style",
                "Type name should use PascalCase to follow the project's naming conventions.",
                0.97)
        ]);
    }
}
