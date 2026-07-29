using DailyReview.Core.GitHub;
using DailyReview.Core.Models;
using DailyReview.Core.Review;
using DailyReview.Core.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DailyReview_AI.Controllers;

[ApiController]
[Route("api/webhook")]
public sealed class WebhookController : ControllerBase
{
    private readonly GitHubClient _gitHubClient;
    private readonly DiffContextBuilder _diffContextBuilder;
    private readonly ReviewResponseParser _reviewResponseParser;
    private readonly IReviewModelClient _reviewModelClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(
        GitHubClient gitHubClient,
        DiffContextBuilder diffContextBuilder,
        ReviewResponseParser reviewResponseParser,
        IReviewModelClient reviewModelClient,
        IConfiguration configuration,
        ILogger<WebhookController> logger)
    {
        ArgumentNullException.ThrowIfNull(gitHubClient);
        ArgumentNullException.ThrowIfNull(diffContextBuilder);
        ArgumentNullException.ThrowIfNull(reviewResponseParser);
        ArgumentNullException.ThrowIfNull(reviewModelClient);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        _gitHubClient = gitHubClient;
        _diffContextBuilder = diffContextBuilder;
        _reviewResponseParser = reviewResponseParser;
        _reviewModelClient = reviewModelClient;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> ReceiveAsync(CancellationToken ct)
    {
        Request.EnableBuffering();
        string rawBody;
        using (var reader = new StreamReader(
                   Request.Body,
                   Encoding.UTF8,
                   detectEncodingFromByteOrderMarks: false,
                   bufferSize: 1024,
                   leaveOpen: true))
        {
            rawBody = await reader.ReadToEndAsync(ct);
        }

        Request.Body.Position = 0;

        if (!HasValidSignature(rawBody, Request.Headers["X-Hub-Signature-256"].ToString()))
        {
            return Unauthorized();
        }

        if (!string.Equals(Request.Headers["X-GitHub-Event"], "pull_request", StringComparison.Ordinal))
        {
            return Ok();
        }

        try
        {
            var payload = JsonSerializer.Deserialize<WebhookPayload>(rawBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (payload is null ||
                !IsReviewAction(payload.Action) ||
                !TryGetPullRequestDetails(payload, out var details))
            {
                return Ok();
            }

            var pullRequestDetails = details!;
            var statusCommentId = await _gitHubClient.PostReviewStartedCommentAsync(
                pullRequestDetails.InstallationId,
                pullRequestDetails.Owner,
                pullRequestDetails.Repository,
                pullRequestDetails.PullRequestNumber,
                ct);

            var contextResult = new ContextBuildResult(string.Empty, 0, 0);
            var validatedReviewResult = new ReviewResult([], Success: false, FailureReason: "review processing failed");
            try
            {
                var pullRequestInfo = await _gitHubClient.GetPullRequestFilesAsync(
                    pullRequestDetails.InstallationId,
                    pullRequestDetails.Owner,
                    pullRequestDetails.Repository,
                    pullRequestDetails.PullRequestNumber,
                    ct);
                contextResult = _diffContextBuilder.BuildContext(pullRequestInfo);
                var validLines = _diffContextBuilder.GetValidLinesPerFile(pullRequestInfo);
                var reviewResult = await _reviewModelClient.ReviewAsync(contextResult.Context, ct);
                validatedReviewResult = _reviewResponseParser.FilterToValidLines(reviewResult, validLines);

                await _gitHubClient.PostReviewCommentsAsync(
                    pullRequestDetails.InstallationId,
                    pullRequestDetails.Owner,
                    pullRequestDetails.Repository,
                    pullRequestDetails.PullRequestNumber,
                    pullRequestDetails.CommitSha,
                    validatedReviewResult.Findings,
                    ct);
            }
            finally
            {
                if (statusCommentId is long commentId)
                {
                    await _gitHubClient.UpdateReviewStatusCommentAsync(
                        pullRequestDetails.InstallationId,
                        pullRequestDetails.Owner,
                        pullRequestDetails.Repository,
                        commentId,
                        BuildStatusMessage(validatedReviewResult, contextResult.SkippedFilesForSize),
                        ct);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation("GitHub webhook processing was cancelled.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "GitHub pull-request webhook processing failed.");
        }

        return Ok();
    }

    private bool HasValidSignature(string rawBody, string signature)
    {
        var secret = _configuration["GitHubApp:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        var payloadBytes = Encoding.UTF8.GetBytes(rawBody);
        var secretBytes = Encoding.UTF8.GetBytes(secret);
        var expectedHash = HMACSHA256.HashData(secretBytes, payloadBytes);
        var expectedSignature = $"sha256={Convert.ToHexString(expectedHash).ToLowerInvariant()}";

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expectedSignature),
            Encoding.ASCII.GetBytes(signature));
    }

    private static string BuildStatusMessage(ReviewResult reviewResult, int skippedFilesForSize)
    {
        if (!reviewResult.Success)
        {
            return $"⚠️ Review could not be completed — {reviewResult.FailureReason ?? "an unknown error occurred"}. " +
                   "This can happen on very large PRs; try again shortly or review manually.";
        }

        var status = reviewResult.Findings.Count == 0
            ? "✅ Review complete — no issues found."
            : $"✅ Review complete — {reviewResult.Findings.Count} finding(s) posted below.";

        if (skippedFilesForSize > 0)
        {
            status += $" (Note: {skippedFilesForSize} large file(s) were skipped due to size limits.)";
        }

        if (reviewResult.UsedFallback)
        {
            status += " (Note: primary AI provider hit its rate limit; fallback provider was used instead.)";
        }

        return status;
    }

    private static bool IsReviewAction(string? action) =>
        string.Equals(action, "opened", StringComparison.Ordinal) ||
        string.Equals(action, "synchronize", StringComparison.Ordinal);

    private static bool TryGetPullRequestDetails(WebhookPayload payload, out PullRequestDetails? details)
    {
        if (payload.Number <= 0 ||
            payload.Installation?.Id <= 0 ||
            string.IsNullOrWhiteSpace(payload.Repository?.Owner?.Login) ||
            string.IsNullOrWhiteSpace(payload.Repository.Name) ||
            string.IsNullOrWhiteSpace(payload.PullRequest?.Head?.Sha))
        {
            details = default;
            return false;
        }

        details = new PullRequestDetails(
            payload.Installation!.Id,
            payload.Repository!.Owner!.Login!,
            payload.Repository.Name!,
            payload.Number,
            payload.PullRequest!.Head!.Sha!);
        return true;
    }

    private sealed record PullRequestDetails(long InstallationId, string Owner, string Repository, int PullRequestNumber, string CommitSha);

    private sealed record WebhookPayload(
     [property: JsonPropertyName("action")] string? Action,
     [property: JsonPropertyName("number")] int Number,
     [property: JsonPropertyName("repository")] WebhookRepository? Repository,
     [property: JsonPropertyName("installation")] WebhookInstallation? Installation,
     [property: JsonPropertyName("pull_request")] WebhookPullRequest? PullRequest);

    private sealed record WebhookRepository(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("owner")] WebhookOwner? Owner);

    private sealed record WebhookOwner(
        [property: JsonPropertyName("login")] string? Login);

    private sealed record WebhookInstallation(
        [property: JsonPropertyName("id")] long Id);

    private sealed record WebhookPullRequest(
        [property: JsonPropertyName("head")] WebhookHead? Head);

    private sealed record WebhookHead(
        [property: JsonPropertyName("sha")] string? Sha);
}
