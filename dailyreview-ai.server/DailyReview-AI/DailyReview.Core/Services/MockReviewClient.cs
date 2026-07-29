using DailyReview.Core.Models;
using System.Text.RegularExpressions;

namespace DailyReview.Core.Services;

public sealed class MockReviewClient : IReviewModelClient
{
    private static readonly Regex HunkHeaderPattern = new(
        "^@@ -[^ ]+ \\+(\\d+)(?:,\\d+)? @@",
        RegexOptions.Compiled);

    public async Task<ReviewResult> ReviewAsync(string diffContext, CancellationToken ct = default)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(200), ct);

        if (string.IsNullOrWhiteSpace(diffContext))
        {
            return new ReviewResult([]);
        }

        var lines = diffContext.Replace("\r\n", "\n").Split('\n');
        var findings = new List<Finding>();

        for (var index = 0; index < lines.Length && findings.Count < 2; index++)
        {
            if (!lines[index].StartsWith("### File: ", StringComparison.Ordinal))
            {
                continue;
            }

            var filePath = lines[index]["### File: ".Length..];
            var languageMarkerIndex = filePath.IndexOf(" (Language:", StringComparison.Ordinal);
            if (languageMarkerIndex >= 0)
            {
                filePath = filePath[..languageMarkerIndex];
            }

            for (var hunkIndex = index + 1; hunkIndex < lines.Length; hunkIndex++)
            {
                if (lines[hunkIndex].StartsWith("### File: ", StringComparison.Ordinal))
                {
                    break;
                }

                var hunkMatch = HunkHeaderPattern.Match(lines[hunkIndex]);
                if (!hunkMatch.Success || !int.TryParse(hunkMatch.Groups[1].Value, out var startLine))
                {
                    continue;
                }

                findings.Add(new Finding(
                    filePath,
                    startLine + 1,
                    "medium",
                    "style",
                    "Mock finding for pipeline testing — replace with real AI review",
                    0.8));
                break;
            }
        }

        return new ReviewResult(findings);
    }
}
