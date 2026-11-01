using MyGarage.Web.Authentication;
using MyGarage.Web.Common;
using MyGarage.Web.Components;
using MyGarage.Web.Vehicles;

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
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();

public partial class Program;
