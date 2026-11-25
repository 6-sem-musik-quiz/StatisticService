using Microsoft.Extensions.DependencyInjection;
using StatisticService.Service.Weekly;

namespace StatisticService.Service.Extensions;

public static class ServiceLayerExtensions
{
    public static IServiceCollection AddServiceLayer(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IWeeklyLeaderboardService, WeeklyLeaderboardService>();

        return services;
    }
}
