using NSubstitute;
using StatisticService.Repository.Interfaces;
using StatisticService.Service.Services;

namespace StatisticService.Service.Tests;

// SCRUM-197: only the load-test prefix may be deleted.
public class AdminStatsServiceTests
{
    private readonly IAdminStatsRepository _repository = Substitute.For<IAdminStatsRepository>();

    [Fact]
    public async Task DeleteLoadBotStatsAsync_WithLoadBotPrefix_ReturnsCount()
    {
        _repository.DeleteUserStatsByUserNamePrefixAsync("loadbot_", Arg.Any<CancellationToken>()).Returns(4);

        var deleted = await new AdminStatsService(_repository).DeleteLoadBotStatsAsync("loadbot_", CancellationToken.None);

        Assert.Equal(4, deleted);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("loadbot")]
    [InlineData("LOADBOT_")]
    public async Task DeleteLoadBotStatsAsync_WithOtherPrefix_ThrowsAndDeletesNothing(string prefix)
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => new AdminStatsService(_repository).DeleteLoadBotStatsAsync(prefix, CancellationToken.None));

        await _repository.DidNotReceiveWithAnyArgs().DeleteUserStatsByUserNamePrefixAsync(default!, default);
    }
}
