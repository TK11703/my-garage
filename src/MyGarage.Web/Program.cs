using MyGarage.Web.Authentication;
using MyGarage.Web.Common;
using MyGarage.Web.Components;
using MyGarage.Web.Drafts;
using MyGarage.Web.FuelEnergyEntries;
using MyGarage.Web.Inspections;
using MyGarage.Web.Insurance;
using MyGarage.Web.Registrations;
using MyGarage.Web.Reminders;
using MyGarage.Web.Lookups;
using MyGarage.Web.Notifications;
using MyGarage.Web.ServiceRecords;
using MyGarage.Web.Taxes;
using MyGarage.Web.VehicleDocuments;
using MyGarage.Web.Vehicles;
using MyGarage.Web.Vehicles.StickerImport;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddEntraAuthentication();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddScoped<IApiTokenProvider, MsalApiTokenProvider>();
builder.Services.AddTransient<ApiAccessTokenHandler>();
// "https+http://api" is resolved by service discovery to the API resource started by the AppHost.
builder.Services.AddHttpClient<GarageApiClient>(client => client.BaseAddress = new Uri("https+http://api"))
    .AddHttpMessageHandler<ApiAccessTokenHandler>();
builder.Services.AddHttpClient<VehiclesApiClient>(client => client.BaseAddress = new Uri("https+http://api"))
    .AddHttpMessageHandler<ApiAccessTokenHandler>();
// Reading a sticker takes the model up to 90 s and is billed per call, so it gets its own client: a 105 s limit and no automatic retry.
// RemoveAllResilienceHandlers is experimental, but it is the only way to replace the default handler that ServiceDefaults adds to every client.
#pragma warning disable EXTEXP0001
builder.Services.AddHttpClient<StickerImportApiClient>(client =>
    {
        client.BaseAddress = new Uri("https+http://api");
        client.Timeout = TimeSpan.FromSeconds(105);
    })
    .AddHttpMessageHandler<ApiAccessTokenHandler>()
    .RemoveAllResilienceHandlers();
// Uploading up to 15 MB may take a while on a slow link and a retry would queue the files twice, so it has a 2 minute limit and no retry.
builder.Services.AddHttpClient<DraftUploadApiClient>(client =>
    {
        client.BaseAddress = new Uri("https+http://api");
        client.Timeout = TimeSpan.FromMinutes(2);
    })
    .AddHttpMessageHandler<ApiAccessTokenHandler>()
    .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
builder.Services.AddHttpClient<LookupsApiClient>(client => client.BaseAddress = new Uri("https+http://api"))
    .AddHttpMessageHandler<ApiAccessTokenHandler>();
builder.Services.AddHttpClient<ServiceRecordsApiClient>(client => client.BaseAddress = new Uri("https+http://api"))
    .AddHttpMessageHandler<ApiAccessTokenHandler>();
builder.Services.AddHttpClient<FuelEnergyEntriesApiClient>(client => client.BaseAddress = new Uri("https+http://api"))
    .AddHttpMessageHandler<ApiAccessTokenHandler>();
builder.Services.AddHttpClient<InspectionsApiClient>(client => client.BaseAddress = new Uri("https+http://api"))
    .AddHttpMessageHandler<ApiAccessTokenHandler>();
builder.Services.AddHttpClient<RegistrationsApiClient>(client => client.BaseAddress = new Uri("https+http://api"))
    .AddHttpMessageHandler<ApiAccessTokenHandler>();
builder.Services.AddHttpClient<RemindersApiClient>(client => client.BaseAddress = new Uri("https+http://api"))
    .AddHttpMessageHandler<ApiAccessTokenHandler>();
builder.Services.AddHttpClient<InsuranceApiClient>(client => client.BaseAddress = new Uri("https+http://api"))
    .AddHttpMessageHandler<ApiAccessTokenHandler>();
builder.Services.AddHttpClient<TaxesApiClient>(client => client.BaseAddress = new Uri("https+http://api"))
    .AddHttpMessageHandler<ApiAccessTokenHandler>();
builder.Services.AddHttpClient<DraftsApiClient>(client => client.BaseAddress = new Uri("https+http://api"))
    .AddHttpMessageHandler<ApiAccessTokenHandler>();
builder.Services.AddHttpClient<NotificationsApiClient>(client => client.BaseAddress = new Uri("https+http://api"))
    .AddHttpMessageHandler<ApiAccessTokenHandler>();
builder.Services.AddHttpClient<VehicleDocumentsApiClient>(client => client.BaseAddress = new Uri("https+http://api"))
    .AddHttpMessageHandler<ApiAccessTokenHandler>();
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Static assets carry no user data and must load on the anonymous sign-in, error and signed-out pages.
app.MapStaticAssets().AllowAnonymous();
app.MapAuthenticationEndpoints();
app.MapVehicleImageEndpoints();
app.MapDraftAttachmentEndpoints();
app.MapDocumentContentEndpoints();
app.MapFeatureSuggestionEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();

public partial class Program;
