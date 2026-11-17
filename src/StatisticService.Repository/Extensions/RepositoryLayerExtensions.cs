using StatisticService.Repository.Data;
using StatisticService.Repository.Interfaces;
using StatisticService.Repository.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace StatisticService.Repository.Extensions;

public static class RepositoryLayerExtensions
{
    public static IServiceCollection AddRepositoryLayer(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

        services.AddScoped<IExampleRepository, ExampleRepository>();

        return services;
    }
}
