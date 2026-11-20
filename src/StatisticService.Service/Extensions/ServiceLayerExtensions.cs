using Microsoft.Extensions.DependencyInjection;

namespace StatisticService.Service.Extensions;

public static class ServiceLayerExtensions
{
    public static IServiceCollection AddServiceLayer(this IServiceCollection services)
    {
        // Register services here, e.g. services.AddScoped<IStatsService, StatsService>();

        return services;
    }
}
