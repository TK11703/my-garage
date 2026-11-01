using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Serilog;
using Serilog.Events;

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
                // Request-start events carry the full URL, including search text in the query string, so ASP.NET Core stays at Warning.
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .ReadFrom.Configuration(builder.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .WriteTo.Console(),
            writeToProviders: true);

        return builder;
    }
}
