using Microsoft.Extensions.DependencyInjection;
using StatisticService.Service.Weekly;
using StatisticService.Service.Interfaces;
using StatisticService.Service.Services;

namespace StatisticService.Service.Extensions;

public static class ServiceLayerExtensions
{
    public static IServiceCollection AddServiceLayer(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IWeeklyLeaderboardService, WeeklyLeaderboardService>();
        services.AddScoped<IMyStatsService, MyStatsService>();

        return services;
    }
}
