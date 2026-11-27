using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StatisticService.Repository.Data;

namespace StatisticService.Api.Tests;

// SCRUM-202: reads go to the read replicas (connection string "Read"), writes to the primary ("Default").
public class ReadReplicaTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Primary = "Host=quizdb-postgresql-primary;Database=statisticservice;Username=statisticservice;Password=x";
    private const string Replicas = "Host=quizdb-postgresql-read;Database=statisticservice;Username=statisticservice;Password=x";

    private WebApplicationFactory<Program> App(string? read) => factory.WithWebHostBuilder(b =>
    {
        b.UseSetting("Database:MigrateOnStartup", "false");
        b.UseSetting("ConnectionStrings:Default", Primary);
        if (read is not null) b.UseSetting("ConnectionStrings:Read", read);
    });

    private static string Host(DbContext db) =>
        new Npgsql.NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()).Host!;

    [Fact]
    public void WithARead_ConnectionString_ReadsGoToTheReplicas_AndWritesToThePrimary()
    {
        using var app = App(Replicas);
        using var scope = app.Services.CreateScope();

        Assert.Equal("quizdb-postgresql-primary", Host(scope.ServiceProvider.GetRequiredService<AppDbContext>()));
        Assert.Equal("quizdb-postgresql-read", Host(scope.ServiceProvider.GetRequiredService<StatsReadDbContext>()));
    }

    [Fact]
    public void WithoutOne_ReadsGoToThePrimary()
    {
        using var app = App(read: null);
        using var scope = app.Services.CreateScope();

        Assert.Equal("quizdb-postgresql-primary", Host(scope.ServiceProvider.GetRequiredService<StatsReadDbContext>()));
    }
}
