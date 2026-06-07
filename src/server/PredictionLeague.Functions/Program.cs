using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PredictionLeague.Infrastructure;

// Isolated-worker Functions host. Thin shell over IFixtureIngestService — reuses the same
// AddInfrastructure + AddFootballIngest DI as the Api so there is one ingest implementation.
var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        services.AddInfrastructure(context.Configuration);
        services.AddFootballIngest(context.Configuration);
    })
    .Build();

host.Run();
