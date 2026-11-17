using StatisticService.Domain.DTOs;
using StatisticService.Domain.Entities;

namespace StatisticService.Service.Mappings;

public static class ExampleMappings
{
    public static ExampleResponse ToResponse(this Example entity) =>
        new(entity.Id, entity.Name, entity.Description, entity.CreatedAt);

    public static Example ToEntity(this ExampleRequest request) =>
        new() { Name = request.Name, Description = request.Description };
}
