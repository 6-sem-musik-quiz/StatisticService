using StatisticService.Domain.Entities;
using StatisticService.Repository.Data;
using StatisticService.Repository.Interfaces;

namespace StatisticService.Repository.Repositories;

public class ExampleRepository(AppDbContext context) : Repository<Example>(context), IExampleRepository
{
}
