using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DailyReview.Core.Models;

namespace DailyReview.Core.GitHub;

public record ChangedFile(string FilePath, string Patch, int Additions, int Deletions);

public record PullRequestInfo(string Title, string? Description, List<ChangedFile> Files);

public sealed class GitHubClient
{
    private const string GitHubApiBaseUrl = "https://api.github.com";
    private readonly GitHubAppAuthenticator _authenticator;
    private readonly HttpClient _httpClient;

    public GitHubClient(GitHubAppAuthenticator authenticator, HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(authenticator);
        ArgumentNullException.ThrowIfNull(httpClient);

        _authenticator = authenticator;
        _httpClient = httpClient;
    }

    public async Task<PullRequestInfo> GetPullRequestFilesAsync(
        long installationId,
        string owner,
        string repo,
        int prNumber,
        CancellationToken ct = default)
    {
        try
        {
            var token = await _authenticator.GetInstallationTokenAsync(installationId, ct);
            var pullRequest = await GetPullRequestAsync(token, owner, repo, prNumber, ct);
            var files = await GetFilesAsync(token, owner, repo, prNumber, ct);

            return new PullRequestInfo(pullRequest?.Title ?? string.Empty, pullRequest?.Body, files);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Trace.TraceError($"Could not retrieve GitHub pull request {owner}/{repo}#{prNumber}: {exception}");
            return new PullRequestInfo(string.Empty, null, []);
        }
    }

    public async Task PostReviewCommentsAsync(
        long installationId,
        string owner,
        string repo,
        int prNumber,
        string commitSha,
        List<Finding> findings,
        CancellationToken ct = default)
    {
        if (findings is null || findings.Count == 0)
        {
            return;
        }

        try
        {
            var token = await _authenticator.GetInstallationTokenAsync(installationId, ct);
            var endpoint = GetPullRequestEndpoint(owner, repo, prNumber) + "/reviews";
            var review = new
            {
                commit_id = commitSha,
                @event = "COMMENT",
                comments = findings.Select(finding => new
                {
                    path = finding.FilePath,
                    line = finding.Line,
                    body = $"**[{finding.Severity.ToUpperInvariant()} - {finding.Category}]** {finding.Message}"
                })
            };

            using var request = CreateRequest(HttpMethod.Post, endpoint, token);
            request.Content = JsonContent.Create(review);

            using var response = await _httpClient.SendAsync(request, ct);
            var aa = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                await LogFailedResponseAsync("post pull-request review", response, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Trace.TraceError($"Could not post GitHub review for {owner}/{repo}#{prNumber}: {exception}");
        }
    }

    public async Task<long?> PostReviewStartedCommentAsync(
        long installationId,
        string owner,
        string repo,
        int prNumber,
        CancellationToken ct = default)
    {
        try
        {
            var token = await _authenticator.GetInstallationTokenAsync(installationId, ct);
            var endpoint = $"{GetRepositoryEndpoint(owner, repo)}/issues/{prNumber}/comments";
            using var request = CreateRequest(HttpMethod.Post, endpoint, token);
            request.Content = JsonContent.Create(new
            {
                body = "🔍 **DailyReview.ai** is reviewing this pull request..."
            });

            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                await LogFailedResponseAsync("post review-started comment", response, ct);
                return null;
            }

            var comment = await response.Content.ReadFromJsonAsync<IssueCommentResponse>(cancellationToken: ct);
            if (comment is null || comment.Id <= 0)
            {
                Trace.TraceError("GitHub returned an invalid review-started comment response.");
                return null;
            }

            return comment.Id;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Trace.TraceError($"Could not post review-started comment for {owner}/{repo}#{prNumber}: {exception}");
            return null;
        }
    }

    public async Task UpdateReviewStatusCommentAsync(
        long installationId,
        string owner,
        string repo,
        long commentId,
        int findingsCount,
        CancellationToken ct = default)
    {
        try
        {
            var token = await _authenticator.GetInstallationTokenAsync(installationId, ct);
            var endpoint = $"{GetRepositoryEndpoint(owner, repo)}/issues/comments/{commentId}";
            var body = findingsCount == 0
                ? "✅ **DailyReview.ai** review complete — no issues found."
                : $"✅ **DailyReview.ai** review complete — {findingsCount} finding(s) posted below.";

            using var request = CreateRequest(HttpMethod.Patch, endpoint, token);
            request.Content = JsonContent.Create(new { body });

            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                await LogFailedResponseAsync("update review-status comment", response, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Trace.TraceError($"Could not update review-status comment {commentId} for {owner}/{repo}: {exception}");
        }
    }

    private async Task<PullRequestResponse?> GetPullRequestAsync(
        string token,
        string owner,
        string repo,
        int prNumber,
        CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Get, GetPullRequestEndpoint(owner, repo, prNumber), token);
        using var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            await LogFailedResponseAsync("get pull-request details", response, ct);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<PullRequestResponse>(cancellationToken: ct);
    }

    private async Task<List<ChangedFile>> GetFilesAsync(
        string token,
        string owner,
        string repo,
        int prNumber,
        CancellationToken ct)
    {
        var files = new List<ChangedFile>();
        string? nextPageUrl = GetPullRequestEndpoint(owner, repo, prNumber) + "/files?per_page=100&page=1";

        while (nextPageUrl is not null)
        {
            using var request = CreateRequest(HttpMethod.Get, nextPageUrl, token);
            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                await LogFailedResponseAsync("get pull-request files", response, ct);
                break;
            }

            var page = await response.Content.ReadFromJsonAsync<List<PullRequestFileResponse>>(cancellationToken: ct) ?? [];
            files.AddRange(page
                .Where(file => !string.IsNullOrWhiteSpace(file.Patch))
                .Select(file => new ChangedFile(file.FileName, file.Patch!, file.Additions, file.Deletions)));

            nextPageUrl = GetNextPageUrl(response.Headers);
        }

        return files;
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string endpoint, string token)
    {
        var request = new HttpRequestMessage(method, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("DailyReview-AI");
        return request;
    }

    private static string GetPullRequestEndpoint(string owner, string repo, int prNumber) =>
        $"{GetRepositoryEndpoint(owner, repo)}/pulls/{prNumber}";

    private static string GetRepositoryEndpoint(string owner, string repo) =>
        $"{GitHubApiBaseUrl}/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}";

    private static string? GetNextPageUrl(HttpResponseHeaders headers)
    {
        if (!headers.TryGetValues("Link", out var linkHeaders))
        {
            return null;
        }

        foreach (var link in linkHeaders.SelectMany(header => header.Split(',')))
        {
            if (!link.Contains("rel=\"next\"", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var start = link.IndexOf('<') + 1;
            var end = link.IndexOf('>');
            if (start > 0 && end > start)
            {
                return link[start..end];
            }
        }

        return null;
    }

    private static async Task LogFailedResponseAsync(string operation, HttpResponseMessage response, CancellationToken ct)
    {
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        Trace.TraceError($"GitHub failed to {operation}. Status: {(int)response.StatusCode} {response.ReasonPhrase}. Response: {responseBody}");
    }

    private sealed record PullRequestResponse(
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("body")] string? Body);

    private sealed record PullRequestFileResponse(
        [property: JsonPropertyName("filename")] string FileName,
        [property: JsonPropertyName("patch")] string? Patch,
        [property: JsonPropertyName("additions")] int Additions,
        [property: JsonPropertyName("deletions")] int Deletions);

    private sealed record IssueCommentResponse(
        [property: JsonPropertyName("id")] long Id);
}
