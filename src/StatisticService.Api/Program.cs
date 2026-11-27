using StatisticService.Service.Messaging;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using StatisticService.Api.Middleware;
using StatisticService.Repository.Data;
using StatisticService.Repository.Extensions;
using StatisticService.Service.Extensions;
using StatisticService.Service.Metrics;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

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
            ClockSkew = TimeSpan.FromSeconds(30),
            RoleClaimType = "role" // UserService writes the role as "role" (SCRUM-197, admin cleanup)
        };
    });
builder.Services.AddAuthorization(options =>
    options.AddPolicy(StatisticService.Api.Controllers.AuthPolicies.AdminOnly, policy => policy.RequireRole("Admin")));

builder.Services.AddRepositoryLayer(
    builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Connection string 'Default' is not configured."));
builder.Services.AddServiceLayer();
builder.Services.AddResultConsumer(builder.Configuration);

// SCRUM-173/190: traces, metrics and logs over OTLP. The collector address comes from
// OTEL_EXPORTER_OTLP_ENDPOINT (default http://localhost:4317) and the service name from OTEL_SERVICE_NAME
// (both set by the gitops chart). A GameFinished message continues GameService's trace: RabbitMQ.Client
// reads the trace context from the message headers. Logs carry the trace id.
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation(o => o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health"))
        .AddSource(MessageTracing.SourceName) // GameFinished, as a child of the game's publish (not RabbitMQ.Client's linked "deliver" trace)
        .AddSource("Npgsql"))                    // the SQL each request and message runs
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation() // http.server.request.duration for /weekly and /me
        .AddMeter("System.Runtime", "Npgsql")
        .AddMeter(StatisticsMetrics.MeterName))
    .WithLogging(logging => { }, options =>
    {
        options.IncludeFormattedMessage = true;
        options.IncludeScopes = true;
    })
    .UseOtlpExporter();

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
