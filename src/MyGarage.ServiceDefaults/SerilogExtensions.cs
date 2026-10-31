using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Serilog;

namespace Microsoft.Extensions.Hosting;

public static class SerilogExtensions
{
    // Serilog drives ILogger; the OpenTelemetry provider still receives events through writeToProviders.
    public static TBuilder AddSerilogLogging<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        foreach (var descriptor in builder.Services
                     .Where(d => d.ServiceType == typeof(ILoggerProvider) && d.ImplementationType == typeof(ConsoleLoggerProvider))
                     .ToList())
        {
            builder.Services.Remove(descriptor);
        }

        builder.Services.AddSerilog(
            (services, configuration) => configuration
                .ReadFrom.Configuration(builder.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .WriteTo.Console(),
            writeToProviders: true);

        return builder;
    }
}
