using DailyReview.Core.GitHub;
using DailyReview.Core.Review;
using DailyReview.Core.Services;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Render supplies PORT at runtime. Respect ASPNETCORE_URLS when it is explicitly configured.
var renderPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(renderPort) &&
    string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{renderPort}");
}

var configuredGitHubApp = builder.Configuration
    .GetSection(GitHubAppOptions.SectionName)
    .Get<GitHubAppOptions>();
if (configuredGitHubApp is null ||
    (string.IsNullOrWhiteSpace(configuredGitHubApp.PrivateKey) &&
     string.IsNullOrWhiteSpace(configuredGitHubApp.PrivateKeyPath)))
{
    throw new InvalidOperationException(
        "Either GitHubApp:PrivateKey or GitHubApp:PrivateKeyPath must be configured.");
}

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.Configure<GitHubAppOptions>(
    builder.Configuration.GetSection(GitHubAppOptions.SectionName));
builder.Services.AddSingleton<GitHubAppAuthenticator>(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<IOptions<GitHubAppOptions>>().Value;
    var appId = options.AppId
        ?? throw new InvalidOperationException("GitHubApp:AppId is not configured.");
    var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient();

    return new GitHubAppAuthenticator(appId, options.PrivateKey, options.PrivateKeyPath, httpClient);
});
builder.Services.AddScoped<GitHubClient>();
builder.Services.AddScoped<ReviewPromptBuilder>();
builder.Services.AddScoped<ReviewResponseParser>();
builder.Services.AddScoped<DiffContextBuilder>();

builder.Services.AddKeyedScoped<IReviewModelClient>("primary", (serviceProvider, _) =>
    new OpenAiCompatibleReviewClient(
        serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(),
        serviceProvider.GetRequiredService<IConfiguration>(),
        serviceProvider.GetRequiredService<ReviewResponseParser>(),
        serviceProvider.GetRequiredService<ILogger<OpenAiCompatibleReviewClient>>(),
        "LlmProvider"));
builder.Services.AddKeyedScoped<IReviewModelClient>("fallback", (serviceProvider, _) =>
    new OpenAiCompatibleReviewClient(
        serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(),
        serviceProvider.GetRequiredService<IConfiguration>(),
        serviceProvider.GetRequiredService<ReviewResponseParser>(),
        serviceProvider.GetRequiredService<ILogger<OpenAiCompatibleReviewClient>>(),
        "LlmProvider2"));
builder.Services.AddScoped<IReviewModelClient>(serviceProvider =>
    new FallbackReviewClient(
        serviceProvider.GetRequiredKeyedService<IReviewModelClient>("primary"),
        serviceProvider.GetRequiredKeyedService<IReviewModelClient>("fallback"),
        serviceProvider.GetRequiredService<ILogger<FallbackReviewClient>>()));

var app = builder.Build();

// Configure the HTTP request pipeline.

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapGet("/", () => Results.Text("DailyReview.ai is running.", "text/plain"));
app.MapControllers();

app.Run();
