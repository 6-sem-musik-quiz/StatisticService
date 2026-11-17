using StatisticService.Service.Interfaces;
using StatisticService.Service.Services;
using Microsoft.Extensions.DependencyInjection;

namespace StatisticService.Service.Extensions;

public static class ServiceLayerExtensions
{
    public static IServiceCollection AddServiceLayer(this IServiceCollection services)
    {
        services.AddScoped<IExampleService, ExampleService>();

        return services;
    }
}
