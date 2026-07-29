namespace DailyReview.Core.GitHub;

public sealed class GitHubAppOptions
{
    public const string SectionName = "GitHubApp";

    public string? AppId { get; set; }

    public string? PrivateKey { get; set; }

    public string? PrivateKeyPath { get; set; }

    public string? WebhookSecret { get; set; }
}
