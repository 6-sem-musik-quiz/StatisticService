using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using StatisticService.Domain.DTOs;
using StatisticService.Service.Interfaces;

namespace StatisticService.Api.Tests;

public class MyStatsEndpointTests
{
    private const string Key = "test-signing-key-at-least-32-bytes-long!!";
    private readonly IMyStatsService _service = Substitute.For<IMyStatsService>();
    private readonly WebApplicationFactory<Program> _factory;

    public MyStatsEndpointTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Database:MigrateOnStartup", "false");
            b.UseSetting("Jwt:SigningKey", Key);
            b.ConfigureServices(s => s.AddScoped(_ => _service));
        });
    }

    private static string Token(string sub, string key = Key)
    {
        var jwt = new JwtSecurityToken(
            issuer: "slopster-userservice", audience: "slopster",
            claims: [new Claim("sub", sub)],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private HttpClient ClientWith(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Me_WithToken_Returns200_AndUsesSubAsUserId()
    {
        _service.GetMyStatsAsync(42, Arg.Any<CancellationToken>()).Returns(new MyStatsResponse(
            3, 1, 1.0 / 3, 0.5, 30, 2100,
            new MostPlayedCategoryDto(7, "Rock", 2), new MostCommonFriendDto(2, "Alice", 2)));

        var response = await ClientWith(Token("42")).GetAsync("/api/statistics/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, json.GetProperty("gamesPlayed").GetInt32());
        Assert.Equal(2100, json.GetProperty("timePlayedSeconds").GetInt64());
        Assert.Equal("Rock", json.GetProperty("mostPlayedCategory").GetProperty("categoryName").GetString());
        Assert.Equal("Alice", json.GetProperty("mostCommonFriend").GetProperty("userName").GetString());
    }

    [Fact]
    public async Task Me_WithoutToken_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync("/api/statistics/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_WithWrongSigningKey_Returns401()
    {
        var client = ClientWith(Token("42", "another-signing-key-also-32-bytes-long!!"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/statistics/me")).StatusCode);
    }

    [Fact]
    public async Task Me_WithNonNumericSub_Returns401()
    {
        var client = ClientWith(Token("not-a-number"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/statistics/me")).StatusCode);
    }
}
