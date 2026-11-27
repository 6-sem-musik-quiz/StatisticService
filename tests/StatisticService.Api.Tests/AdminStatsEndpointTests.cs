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
using NSubstitute.ExceptionExtensions;
using StatisticService.Service.Interfaces;

namespace StatisticService.Api.Tests;

// SCRUM-197: DELETE /api/admin/stats?prefix=loadbot_ removes the load-test bots' stats. Admin only.
public class AdminStatsEndpointTests
{
    private const string Key = "test-signing-key-at-least-32-bytes-long!!";
    private readonly IAdminStatsService _service = Substitute.For<IAdminStatsService>();
    private readonly WebApplicationFactory<Program> _factory;

    public AdminStatsEndpointTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Database:MigrateOnStartup", "false");
            b.UseSetting("Jwt:SigningKey", Key);
            b.UseSetting("Jwt:Issuer", "slopster-userservice");
            b.UseSetting("Jwt:Audience", "slopster");
            b.ConfigureServices(s => s.AddScoped(_ => _service));
        });
    }

    [Fact]
    public async Task Delete_AsAdmin_Returns200_WithCount()
    {
        _service.DeleteLoadBotStatsAsync("loadbot_", Arg.Any<CancellationToken>()).Returns(7);

        var response = await Delete("loadbot_", Token("Admin"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(7, json.GetProperty("deleted").GetInt32());
    }

    [Fact]
    public async Task Delete_AsPlayer_Returns403()
    {
        var response = await Delete("loadbot_", Token("Player"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await _service.DidNotReceiveWithAnyArgs().DeleteLoadBotStatsAsync(default!, default);
    }

    [Fact]
    public async Task Delete_WithoutToken_Returns401()
    {
        var response = await Delete("loadbot_", token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_WithOtherPrefix_Returns400()
    {
        _service.DeleteLoadBotStatsAsync("a", Arg.Any<CancellationToken>())
            .Throws(new ArgumentException("Only the load-test prefix 'loadbot_' can be deleted.", "prefix"));

        var response = await Delete("a", Token("Admin"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private Task<HttpResponseMessage> Delete(string prefix, string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/stats?prefix={prefix}");
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _factory.CreateClient().SendAsync(request);
    }

    private static string Token(string role)
    {
        var jwt = new JwtSecurityToken(
            issuer: "slopster-userservice", audience: "slopster",
            claims: [new Claim("sub", "999"), new Claim("role", role)],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}
