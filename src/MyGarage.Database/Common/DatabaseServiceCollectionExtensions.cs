using Microsoft.Extensions.DependencyInjection;
using MyGarage.Database.JournalNotes;
using MyGarage.Database.Tags;
using MyGarage.Database.VehicleImages;
using MyGarage.Database.Vehicles;

namespace MyGarage.Database.Common;

public static class DatabaseServiceCollectionExtensions
{
    // Requires an NpgsqlDataSource to be registered by the host.
    public static IServiceCollection AddGarageDatabase(this IServiceCollection services)
    {
        services.AddSingleton<SqlScriptStore>();
        services.AddSingleton<MigrationRunner>();
        services.AddHostedService<MigrationHostedService>();
        services.AddSingleton<VehicleRepository>();
        services.AddSingleton<VehicleTagRepository>();
        services.AddSingleton<JournalNoteRepository>();
        services.AddSingleton<IVehicleImageBlobStore, AzureBlobVehicleImageStore>();
        services.AddSingleton<VehicleImageRepository>();

        return services;
    }
}
