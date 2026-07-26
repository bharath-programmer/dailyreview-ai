using DailyReview.Core.Models;

namespace DailyReview.Core.Services;

public interface IReviewModelClient
{
    Task<ReviewResult> ReviewAsync(string diffContext, CancellationToken ct = default);
}
