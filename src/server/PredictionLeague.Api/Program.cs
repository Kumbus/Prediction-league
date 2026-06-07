using Microsoft.EntityFrameworkCore;
using PredictionLeague.Infrastructure;
using PredictionLeague.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// EF Core context + repositories (connection string from configuration / user-secrets).
builder.Services.AddInfrastructure(builder.Configuration);

// Football ingest: typed API-Football client + ingest service (shared with the Functions host).
builder.Services.AddFootballIngest(builder.Configuration);

// Authentication: Identity cookie + Google external login + AdminOnly policy.
builder.Services.AddAuthenticationAndIdentity(builder.Configuration);

// CORS for the split-stack SPA — credentials (cookie) require explicit origins, not AllowAnyOrigin.
const string SpaCorsPolicy = "SpaCors";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddPolicy(SpaCorsPolicy, p =>
    p.WithOrigins(allowedOrigins)
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
