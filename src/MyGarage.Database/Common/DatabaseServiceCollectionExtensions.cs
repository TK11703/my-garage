using Microsoft.Extensions.DependencyInjection;
using MyGarage.Database.Dashboard;
using MyGarage.Database.Drafts;
using MyGarage.Database.EmailIntake;
using MyGarage.Database.FuelEnergyEntries;
using MyGarage.Database.Inspections;
using MyGarage.Database.Insurance;
using MyGarage.Database.JournalNotes;
using MyGarage.Database.Lookups;
using MyGarage.Database.Notifications;
using MyGarage.Database.Registrations;
using MyGarage.Database.Reminders;
using MyGarage.Database.ServiceRecords;
using MyGarage.Database.Tags;
using MyGarage.Database.Taxes;
using MyGarage.Database.Users;
using MyGarage.Database.VehicleDocuments;
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
        services.AddSingleton<LookupRepository>();
        services.AddSingleton<VehicleTagRepository>();
        services.AddSingleton<JournalNoteRepository>();
        services.AddSingleton<ServiceRecordRepository>();
        services.AddSingleton<FuelEnergyEntryRepository>();
        services.AddSingleton<InspectionRepository>();
        services.AddSingleton<RegistrationRepository>();
        services.AddSingleton<ReminderRepository>();
        services.AddSingleton<DashboardRepository>();
        services.AddSingleton<InsurancePolicyRepository>();
        services.AddSingleton<TaxRepository>();
        services.AddSingleton<IVehicleImageBlobStore, AzureBlobVehicleImageStore>();
        services.AddSingleton<VehicleImageRepository>();
        services.AddSingleton<UserProfileRepository>();
        services.AddSingleton<IIntakeBlobStore, AzureBlobIntakeStore>();
        services.AddSingleton<EmailSubmissionRepository>();
        services.AddSingleton<MaintenanceDraftRepository>();
        services.AddSingleton<DraftAcceptanceRepository>();
        services.AddSingleton<NotificationRepository>();
        services.AddSingleton<IVehicleDocumentBlobStore, AzureBlobVehicleDocumentStore>();
        services.AddSingleton<VehicleDocumentRepository>();

        return services;
    }
}
