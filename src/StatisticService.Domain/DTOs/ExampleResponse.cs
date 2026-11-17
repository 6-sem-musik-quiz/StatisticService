namespace StatisticService.Domain.DTOs;

public record ExampleResponse(Guid Id, string Name, string? Description, DateTime CreatedAt);
