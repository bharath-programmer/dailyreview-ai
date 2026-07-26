using DailyReview.Core.GitHub;

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



var app = builder.Build();

// Configure the HTTP request pipeline.

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
