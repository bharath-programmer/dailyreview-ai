using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace DailyReview.Core.GitHub;

public sealed class GitHubAppAuthenticator
{
    private const string GitHubApiBaseUrl = "https://api.github.com";
    private readonly ConcurrentDictionary<long, CachedInstallationToken> _tokenCache = new();
    private readonly HttpClient _httpClient;
    private readonly string _appId;
    private readonly SigningCredentials _signingCredentials;

    public GitHubAppAuthenticator(string appId, string privateKeyPath, HttpClient httpClient)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPath);
        ArgumentNullException.ThrowIfNull(httpClient);

        _appId = appId;
        _httpClient = httpClient;

        var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(privateKeyPath));
        _signingCredentials = new SigningCredentials(
            new RsaSecurityKey(rsa),
            SecurityAlgorithms.RsaSha256);
    }

    public async Task<string> GetInstallationTokenAsync(long installationId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        if (_tokenCache.TryGetValue(installationId, out var cachedToken) &&
            cachedToken.ExpiresAt > now.AddSeconds(60))
        {
            return cachedToken.Token;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{GitHubApiBaseUrl}/app/installations/{installationId}/access_tokens");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwt());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("DailyReview-AI");

        using var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"GitHub error response: {errorBody}");
        }
        response.EnsureSuccessStatusCode();

        var tokenResponse = await response.Content.ReadFromJsonAsync<InstallationTokenResponse>(ct)
            ?? throw new InvalidOperationException("GitHub returned an empty installation-token response.");

        if (string.IsNullOrWhiteSpace(tokenResponse.Token))
        {
            throw new InvalidOperationException("GitHub installation-token response did not contain a token.");
        }

        var freshToken = new CachedInstallationToken(tokenResponse.Token, tokenResponse.ExpiresAt);
        _tokenCache[installationId] = freshToken;

        return freshToken.Token;
    }

    private string GenerateJwt()
    {
        var now = DateTimeOffset.UtcNow;
        var issuedAt = now.AddSeconds(-60);
        var token = new JwtSecurityToken(
            issuer: _appId,
            claims:
            [
                new Claim(
                JwtRegisteredClaimNames.Iat,
                issuedAt.ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64)
            ],
            notBefore: issuedAt.UtcDateTime,
            expires: issuedAt.AddMinutes(9).UtcDateTime,  // relative to issuedAt, not now
            signingCredentials: _signingCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed record CachedInstallationToken(string Token, DateTimeOffset ExpiresAt);

    private sealed record InstallationTokenResponse(string Token, DateTimeOffset ExpiresAt);
}
