using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StatisticService.Service.Weekly;
using StatisticService.Service.Interfaces;
using StatisticService.Service.Messaging;
using StatisticService.Service.Results;
using StatisticService.Service.Services;

namespace StatisticService.Service.Extensions;

public static class ServiceLayerExtensions
{
    public static IServiceCollection AddServiceLayer(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IWeeklyLeaderboardService, WeeklyLeaderboardService>();
        services.AddScoped<IMyStatsService, MyStatsService>();
        services.AddScoped<IGameResultService, GameResultService>();

        return services;
    }

    // SCRUM-85: the RabbitMQ consumer that saves GameFinished messages. Settings from the
    // "RabbitMQ" section; RabbitMQ:Enabled=false registers it but makes it do nothing.
    public static IServiceCollection AddResultConsumer(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RabbitMqOptions>().Bind(configuration.GetSection(RabbitMqOptions.SectionName));
        services.AddSingleton<GameFinishedHandler>();
        services.AddHostedService<ResultConsumer>();

        return services;
    }
}
