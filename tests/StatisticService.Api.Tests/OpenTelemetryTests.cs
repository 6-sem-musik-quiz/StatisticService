using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using StatisticService.Service.Metrics;

namespace StatisticService.Api.Tests;

// SCRUM-173/190: the app starts with OpenTelemetry traces and metrics, and the statistics meter.
public class OpenTelemetryTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void App_StartsWithTracesAndMetrics()
    {
        using var app = factory.WithWebHostBuilder(b => b.UseSetting("Database:MigrateOnStartup", "false"));
        using var client = app.CreateClient(); // starts Program, including the OpenTelemetry setup

        Assert.NotNull(app.Services.GetService<TracerProvider>());
        Assert.NotNull(app.Services.GetService<MeterProvider>());
        Assert.NotNull(app.Services.GetService<StatisticsMetrics>());
    }
}
