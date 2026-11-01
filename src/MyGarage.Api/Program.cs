using MyGarage.Api.Common;
using MyGarage.Api.SampleData;
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
builder.Services.AddVehicles();
builder.Services.AddVehicleTags();
builder.Services.AddJournalNotes();
builder.Services.AddVehicleImages();

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
app.MapVehiclesEndpoints();
app.MapVehicleTagsEndpoints();
app.MapJournalNotesEndpoints();
app.MapVehicleImagesEndpoints();
app.MapSampleDataEndpoints();
app.MapDefaultEndpoints();

app.Run();

public partial class Program;
