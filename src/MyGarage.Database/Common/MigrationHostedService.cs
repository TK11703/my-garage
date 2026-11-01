using Microsoft.Extensions.Hosting;

namespace MyGarage.Database.Common;

// Runs pending migrations before the host finishes starting.
internal sealed class MigrationHostedService(MigrationRunner runner) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => runner.RunAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
