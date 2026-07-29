using DailyReview.Core.GitHub;
using DailyReview.Core.Review;
using DailyReview.Core.Services;

var builder = WebApplication.CreateBuilder(args);

// Render supplies PORT at runtime. Respect ASPNETCORE_URLS when it is explicitly configured.
var renderPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(renderPort) &&
    string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{renderPort}");
}

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<GitHubAppAuthenticator>(serviceProvider =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var appId = configuration["GitHubApp:AppId"]
        ?? throw new InvalidOperationException("GitHubApp:AppId is not configured.");
    var privateKeyPath = configuration["GitHubApp:PrivateKeyPath"]
        ?? throw new InvalidOperationException("GitHubApp:PrivateKeyPath is not configured.");
    var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient();

    return new GitHubAppAuthenticator(appId, privateKeyPath, httpClient);
});
builder.Services.AddScoped<GitHubClient>();
builder.Services.AddScoped<ReviewPromptBuilder>();
builder.Services.AddScoped<ReviewResponseParser>();
builder.Services.AddScoped<DiffContextBuilder>();

// For local mock testing, swap OpenAiCompatibleReviewClient with MockReviewClient on this line.
builder.Services.AddScoped<IReviewModelClient, OpenAiCompatibleReviewClient>();

var app = builder.Build();

// Configure the HTTP request pipeline.

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapGet("/", () => Results.Text("DailyReview.ai is running.", "text/plain"));
app.MapControllers();

app.Run();
