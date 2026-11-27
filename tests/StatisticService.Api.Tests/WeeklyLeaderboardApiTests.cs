using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using StatisticService.Domain.DTOs;
using StatisticService.Repository.Interfaces;

namespace StatisticService.Api.Tests;

public class WeeklyLeaderboardApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    // HS256 needs a key of at least 32 bytes. This one only exists in tests.
    private const string SigningKey = "test-signing-key-test-signing-key-0123456789";
    private const string Issuer = "test-issuer";
    private const string Audience = "test-audience";

    private sealed class StubRepository(IReadOnlyList<PlayerGameResult> results) : IWeeklyStatsRepository
    {
        public List<(DateTime From, DateTime To)> Queries { get; } = [];

        public Task<IReadOnlyList<PlayerGameResult>> GetPlayerResultsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
        {
            Queries.Add((fromUtc, toUtc));
            return Task.FromResult(results);
        }

        // Only used to rebuild the Redis cache, and these tests run without Redis.
        public Task<IReadOnlyList<GamePlayerResult>> GetPlayerResultsWithGameAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private HttpClient CreateClient(StubRepository repository, string signingKey = SigningKey)
        => factory.WithWebHostBuilder(builder =>
        {
            // There is no Postgres in CI, so don't run migrations on startup.
            builder.UseSetting("Database:MigrateOnStartup", "false");
            builder.UseSetting("Jwt:SigningKey", signingKey);
            builder.UseSetting("Jwt:Issuer", Issuer);
            builder.UseSetting("Jwt:Audience", Audience);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IWeeklyStatsRepository>();
                services.AddSingleton<IWeeklyStatsRepository>(repository);
            });
        }).CreateClient();

    private static string CreateToken(string key = SigningKey, string issuer = Issuer, string audience = Audience, DateTime? expires = null)
        => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim("sub", "1")]),
            Issuer = issuer,
            Audience = audience,
            Expires = expires ?? DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256)
        });

    private static HttpRequestMessage Get(string url, string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static IReadOnlyList<PlayerGameResult> FiveGamesFor(int userId, string name, int wins)
        => Enumerable.Range(0, 5)
            .Select(i => new PlayerGameResult(userId, name, i < wins ? 1 : 2, 6, 8, new DateTime(2026, 11, 17, 18, i, 0, DateTimeKind.Utc)))
            .ToList();

    [Fact]
    public async Task Weekly_WithValidToken_Returns200_WithTheContractShape()
    {
        var repository = new StubRepository(FiveGamesFor(7, "ann", wins: 3));
        var client = CreateClient(repository);

        var response = await client.SendAsync(Get("/api/statistics/weekly?week=2026-W47", CreateToken()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;

        Assert.Equal("2026-11-16T00:00:00+01:00", root.GetProperty("weekStart").GetString());
        Assert.Equal("2026-11-22T23:59:59+01:00", root.GetProperty("weekEnd").GetString());

        var mostWins = root.GetProperty("mostWins")[0];
        Assert.Equal(7, mostWins.GetProperty("userId").GetInt32());
        Assert.Equal("ann", mostWins.GetProperty("userName").GetString());
        Assert.Equal(3, mostWins.GetProperty("wins").GetInt32());
        Assert.Equal(5, mostWins.GetProperty("gamesPlayed").GetInt32());

        var winRate = root.GetProperty("winRate")[0];
        Assert.Equal(0.6, winRate.GetProperty("winRate").GetDouble(), 10);
        Assert.Equal(5, winRate.GetProperty("gamesPlayed").GetInt32());

        var accuracy = root.GetProperty("accuracy")[0];
        Assert.Equal(0.75, accuracy.GetProperty("accuracy").GetDouble(), 10);
        Assert.Equal("ann", accuracy.GetProperty("userName").GetString());

        // The week 2026-W47 maps to this UTC interval.
        var query = Assert.Single(repository.Queries);
        Assert.Equal(new DateTime(2026, 11, 15, 23, 0, 0, DateTimeKind.Utc), query.From);
        Assert.Equal(new DateTime(2026, 11, 22, 23, 0, 0, DateTimeKind.Utc), query.To);
    }

    [Theory]
    [InlineData("/api/statistics/weekly")]
    [InlineData("/api/statistics/weekly?week=current")]
    public async Task Weekly_CurrentWeek_IsTheDefault_AndReturns200(string url)
    {
        var client = CreateClient(new StubRepository([]));

        var response = await client.SendAsync(Get(url, CreateToken()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, json.RootElement.GetProperty("mostWins").GetArrayLength());
        Assert.Equal(0, json.RootElement.GetProperty("winRate").GetArrayLength());
        Assert.Equal(0, json.RootElement.GetProperty("accuracy").GetArrayLength());
    }

    [Theory]
    [InlineData("2026-W54")]
    [InlineData("2025-W53")]
    [InlineData("garbage")]
    [InlineData("2026-47")]
    [InlineData("")]
    public async Task Weekly_InvalidWeek_Returns400_WithoutQueryingTheDatabase(string week)
    {
        var repository = new StubRepository([]);
        var client = CreateClient(repository);

        var response = await client.SendAsync(Get($"/api/statistics/weekly?week={week}", CreateToken()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(400, json.RootElement.GetProperty("status").GetInt32());
        Assert.Empty(repository.Queries);
    }

    [Fact]
    public async Task Weekly_WithoutToken_Returns401()
    {
        var client = CreateClient(new StubRepository([]));

        var response = await client.SendAsync(Get("/api/statistics/weekly?week=current", token: null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Weekly_WithoutToken_AndInvalidWeek_Returns401_NotTheValidationError()
    {
        var client = CreateClient(new StubRepository([]));

        var response = await client.SendAsync(Get("/api/statistics/weekly?week=garbage", token: null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("wrong-key")]
    [InlineData("wrong-issuer")]
    [InlineData("wrong-audience")]
    [InlineData("expired")]
    [InlineData("not-a-jwt")]
    public async Task Weekly_WithInvalidToken_Returns401(string problem)
    {
        var client = CreateClient(new StubRepository([]));
        var token = problem switch
        {
            "wrong-key" => CreateToken(key: "another-signing-key-another-signing-key-987654"),
            "wrong-issuer" => CreateToken(issuer: "someone-else"),
            "wrong-audience" => CreateToken(audience: "someone-else"),
            "expired" => CreateToken(expires: DateTime.UtcNow.AddHours(-1)),
            _ => "not-a-jwt"
        };

        var response = await client.SendAsync(Get("/api/statistics/weekly?week=current", token));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Weekly_WhenNoSigningKeyIsConfigured_Returns401_InsteadOfFailingToStart()
    {
        var client = CreateClient(new StubRepository([]), signingKey: "");

        var response = await client.SendAsync(Get("/api/statistics/weekly?week=current", CreateToken()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_StaysAnonymous()
    {
        var client = CreateClient(new StubRepository([]));

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
