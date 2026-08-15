using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PredictionLeague.Api.Configuration;
using PredictionLeague.Infrastructure;
using PredictionLeague.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// Enums cross the wire as their member names, not ordinals. The client's types are string unions
// ("ExactScore", "Scheduled", "Saved"), so without this a POST carrying "ExactScore" is a 400 and
// every enum a response returns arrives as a number the UI cannot label.
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// EF Core context + repositories (connection string from configuration / user-secrets).
builder.Services.AddInfrastructure(builder.Configuration);

// Football ingest: typed API-Football client + ingest service (shared with the Functions host).
builder.Services.AddFootballIngest(builder.Configuration);

// Authentication: Identity cookie + Google external login + AdminOnly policy.
builder.Services.AddAuthenticationAndIdentity(builder.Configuration);

// CORS for the split-stack SPA — credentials (cookie) require explicit origins, not AllowAnyOrigin.
// Bind the SPA origins once; the open-redirect guard in AuthController reads the same options.
const string SpaCorsPolicy = "SpaCors";
builder.Services.Configure<SpaCorsOptions>(builder.Configuration.GetSection(SpaCorsOptions.SectionName));
var corsOptions = builder.Configuration.GetSection(SpaCorsOptions.SectionName).Get<SpaCorsOptions>() ?? new SpaCorsOptions();
builder.Services.AddCors(o => o.AddPolicy(SpaCorsPolicy, p =>
    p.WithOrigins(corsOptions.AllowedOrigins)
        .AllowCredentials()
        .AllowAnyHeader()
        .AllowAnyMethod()));

// DB connectivity probe — proves the persistence stack end-to-end at /health/db.
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();

var app = builder.Build();

// Dev-only auto-migrate: apply pending migrations on startup in local dev.
// Prod stays forward-only + human-gated (infra-v2) — never auto-migrate outside Development.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        db.Database.Migrate();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Startup migration failed — is the SQL Server container up? Run 'docker compose up -d'.");
        throw;
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Pipeline order is load-bearing: CORS → Authentication → Authorization → MapControllers.
app.UseCors(SpaCorsPolicy);

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/health/db");

app.Run();
