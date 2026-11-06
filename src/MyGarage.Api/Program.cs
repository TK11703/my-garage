using Azure.Identity;
using MyGarage.Api.Common;
using MyGarage.Api.Drafts;
using MyGarage.Api.EmailIntake;
using MyGarage.Api.FuelEnergyEntries;
using MyGarage.Api.Inspections;
using MyGarage.Api.Insurance;
using MyGarage.Api.Lookups;
using MyGarage.Api.Notifications;
using MyGarage.Api.Registrations;
using MyGarage.Api.Reminders;
using MyGarage.Api.SampleData;
using MyGarage.Api.ServiceRecords;
using MyGarage.Api.Taxes;
using MyGarage.Api.Users;
using MyGarage.Api.VehicleDocuments;
using MyGarage.Api.Vehicles;
using MyGarage.Api.Vehicles.Images;
using MyGarage.Api.Vehicles.JournalNotes;
using MyGarage.Api.Vehicles.StickerImport;
using MyGarage.Api.Vehicles.Tags;
using MyGarage.Database.Common;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddEntraApiAuthentication();
builder.AddNpgsqlDataSource("garagedb", configureDataSourceBuilder: dataSource =>
    EntraDatabaseAuthentication.Configure(dataSource, builder.Configuration, new DefaultAzureCredential()));
builder.AddAzureBlobServiceClient("blobs");

builder.Services.AddGarageProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddSampleData();
builder.Services.AddGarageDatabase();
builder.Services.AddLookups();
builder.Services.AddVehicles();
builder.Services.AddVehicleTags();
builder.Services.AddStickerImport();
builder.Services.AddJournalNotes();
builder.Services.AddVehicleImages();
builder.Services.AddServiceRecords();
builder.Services.AddFuelEnergyEntries();
builder.Services.AddInspections();
builder.Services.AddRegistrations();
builder.Services.AddReminders();
builder.Services.AddInsurance();
builder.Services.AddTaxes();
builder.Services.AddEmailIntake();
builder.Services.AddEmailHistory();
builder.Services.AddDrafts();
builder.Services.AddNotifications();
builder.Services.AddVehicleDocuments();

var app = builder.Build();

app.UseGarageProblemDetails();
app.UseHttpsRedirection();

app.UseAuthentication();

// After authentication so the limiter can partition by the calling app, and before authorization so floods are counted.
app.UseRateLimiter();
app.UseAuthorization();
app.UseMiddleware<UserProfileTracker>();
app.UseBodySizeLimit();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapUsersEndpoints();
app.MapLookupsEndpoints();
app.MapVehiclesEndpoints();
app.MapVehicleTagsEndpoints();
app.MapStickerImportEndpoints();
app.MapJournalNotesEndpoints();
app.MapVehicleImagesEndpoints();
app.MapServiceRecordsEndpoints();
app.MapFuelEnergyEntriesEndpoints();
app.MapInspectionsEndpoints();
app.MapRegistrationsEndpoints();
app.MapRemindersEndpoints();
app.MapInsuranceEndpoints();
app.MapTaxesEndpoints();
app.MapEmailIntakeEndpoints();
app.MapEmailHistoryEndpoints();
app.MapDraftsEndpoints();
app.MapNotificationsEndpoints();
app.MapVehicleDocumentsEndpoints();
app.MapSampleDataEndpoints();
app.MapDefaultEndpoints();

app.Run();

public partial class Program;
