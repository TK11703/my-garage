using MyGarage.Api.Common;
using MyGarage.Api.FuelEnergyEntries;
using MyGarage.Api.Inspections;
using MyGarage.Api.Insurance;
using MyGarage.Api.Lookups;
using MyGarage.Api.Registrations;
using MyGarage.Api.SampleData;
using MyGarage.Api.ServiceRecords;
using MyGarage.Api.Taxes;
using MyGarage.Api.Users;
using MyGarage.Api.Vehicles;
using MyGarage.Api.Vehicles.Images;
using MyGarage.Api.Vehicles.JournalNotes;
using MyGarage.Api.Vehicles.Tags;
using MyGarage.Database.Common;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddEntraApiAuthentication();
builder.AddNpgsqlDataSource("garagedb");
builder.AddAzureBlobServiceClient("blobs");

builder.Services.AddGarageProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddSampleData();
builder.Services.AddGarageDatabase();
builder.Services.AddLookups();
builder.Services.AddVehicles();
builder.Services.AddVehicleTags();
builder.Services.AddJournalNotes();
builder.Services.AddVehicleImages();
builder.Services.AddServiceRecords();
builder.Services.AddFuelEnergyEntries();
builder.Services.AddInspections();
builder.Services.AddRegistrations();
builder.Services.AddInsurance();
builder.Services.AddTaxes();

var app = builder.Build();

app.UseGarageProblemDetails();
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapUsersEndpoints();
app.MapLookupsEndpoints();
app.MapVehiclesEndpoints();
app.MapVehicleTagsEndpoints();
app.MapJournalNotesEndpoints();
app.MapVehicleImagesEndpoints();
app.MapServiceRecordsEndpoints();
app.MapFuelEnergyEntriesEndpoints();
app.MapInspectionsEndpoints();
app.MapRegistrationsEndpoints();
app.MapInsuranceEndpoints();
app.MapTaxesEndpoints();
app.MapSampleDataEndpoints();
app.MapDefaultEndpoints();

app.Run();

public partial class Program;
