using MyGarage.Api.Common;
using MyGarage.Api.SampleData;
using MyGarage.Api.Users;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddEntraApiAuthentication();

builder.Services.AddGarageProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddSampleData();

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
app.MapSampleDataEndpoints();
app.MapDefaultEndpoints();

app.Run();

public partial class Program;
