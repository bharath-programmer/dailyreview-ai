using DailyReview.Core.GitHub;
using DailyReview.Core.Review;
using DailyReview.Core.Services;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddScoped<GroqReviewClient>();

// Active review provider. Replace MockReviewClient with GroqReviewClient to enable live Groq reviews.
builder.Services.AddScoped<IReviewModelClient, MockReviewClient>();



var app = builder.Build();

// Configure the HTTP request pipeline.

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapGet("/", () => Results.Text("DailyReview.ai is running.", "text/plain"));
app.MapControllers();

app.Run();
