using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using StatisticService.Api.Middleware;
using StatisticService.Repository.Data;
using StatisticService.Repository.Extensions;
using StatisticService.Service.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
// /health is liveness (the process is up); /health/ready also checks
// that the database answers, so a pod without a DB gets no traffic.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("postgres", tags: ["ready"]);

// Tokens are issued by UserService (HS256). This service only validates them, with the same
// Jwt:SigningKey/Issuer/Audience. Options are read when first used rather than at startup, so a
// missing key makes every token fail with 401 instead of stopping /health from starting.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IConfiguration>((options, config) =>
    {
        options.MapInboundClaims = false; // keep "sub" as "sub"
        var signingKey = config["Jwt:SigningKey"];
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = config["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = config["Jwt:Audience"],
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = string.IsNullOrEmpty(signingKey)
                ? null
                : new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddRepositoryLayer(
    builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Connection string 'Default' is not configured."));
builder.Services.AddServiceLayer();
builder.Services.AddResultConsumer(builder.Configuration);

var app = builder.Build();

// Bring the database schema up to date before taking traffic. Tests turn this off
// with Database:MigrateOnStartup=false when they have no Postgres.
if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
    app.Services.MigrateDatabase();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// No fallback policy: only endpoints marked [Authorize] need a login, so /health stays anonymous.
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

app.Run();

// Exposed so integration tests can use WebApplicationFactory<Program>.
public partial class Program;
