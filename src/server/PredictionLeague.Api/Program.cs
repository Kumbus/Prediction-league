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

// DB connectivity probe — proves the persistence stack end-to-end at /health/db.
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();

var app = builder.Build();

// Dev-only auto-migrate: apply pending migrations on startup in local dev.
// Prod stays forward-only + human-gated (infra-v2) — never auto-migrate outside Development.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/health/db");

app.Run();
