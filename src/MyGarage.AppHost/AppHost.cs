using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

string Required(string key) =>
    builder.Configuration[key] is { Length: > 0 } value
        ? value
        : throw new InvalidOperationException(
            $"Missing configuration '{key}'. Run infra/entra/Initialize-Entra.ps1 to populate the AppHost user-secrets (see infra/entra/README.md).");

var tenantId = Required("AzureAd:TenantId");
var webClientId = Required("AzureAd:Web:ClientId");
var webClientSecret = builder.AddParameter("web-client-secret", Required("AzureAd:Web:ClientSecret"), secret: true);
var apiClientId = Required("AzureAd:Api:ClientId");
var apiScope = Required("AzureAd:Api:Scope");

// The web redirect URI registered in Entra includes this port, so it must stay fixed (see infra/entra/README.md).
var webHttpsPort = builder.Configuration.GetValue("Web:HttpsPort", 7080);

var postgres = builder.AddPostgres("postgres").WithDataVolume();
var garageDb = postgres.AddDatabase("garagedb");

// Vehicle pictures live in private blob storage; the emulator keeps them in a volume between runs.
var storage = builder.AddAzureStorage("storage").RunAsEmulator(emulator => emulator.WithDataVolume());
var blobs = storage.AddBlobs("blobs");

var api = builder.AddProject<Projects.MyGarage_Api>("api")
    .WithReference(garageDb)
    .WaitFor(garageDb)
    .WithReference(blobs)
    .WaitFor(blobs)
    .WithEnvironment("AzureAd__TenantId", tenantId)
    .WithEnvironment("AzureAd__Api__ClientId", apiClientId)
    .WithEnvironment("AzureAd__Api__Scope", apiScope)
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.MyGarage_Web>("web")
    .WithReference(api)
    .WaitFor(api)
    .WithExternalHttpEndpoints()
    .WithEndpoint("https", endpoint => endpoint.Port = webHttpsPort)
    .WithEnvironment("AzureAd__TenantId", tenantId)
    .WithEnvironment("AzureAd__Web__ClientId", webClientId)
    .WithEnvironment("AzureAd__Web__ClientSecret", webClientSecret)
    .WithEnvironment("AzureAd__Api__ClientId", apiClientId)
    .WithEnvironment("AzureAd__Api__Scope", apiScope)
    .WithHttpHealthCheck("/health");

builder.Build().Run();
