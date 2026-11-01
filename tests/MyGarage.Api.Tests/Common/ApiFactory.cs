using System.Net.Http.Headers;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using MyGarage.Database.VehicleImages;
using Npgsql;

namespace MyGarage.Api.Tests.Common;

public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private string connectionString = string.Empty;
    private BlobServiceClient blobService = null!;
    private readonly string blobContainerName = $"api-{Guid.NewGuid():N}";

    // Runs before the host is built, so the configuration below can use the database and storage created here.
    async Task IAsyncLifetime.InitializeAsync()
    {
        connectionString = await SharedPostgres.CreateDatabaseAsync();
        blobService = await SharedAzurite.CreateServiceClientAsync();
    }

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;

    // The blob container this factory's API writes images to.
    public BlobContainerClient ImageContainer => blobService.GetBlobContainerClient(blobContainerName);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAd:TenantId"] = TestJwt.TenantId,
                ["AzureAd:Api:ClientId"] = TestJwt.ApiClientId,
                ["AzureAd:Api:Scope"] = TestJwt.Scope,
            }));

        builder.ConfigureServices(services =>
        {
            // Aspire reads connection strings when the clients are registered, which is before test configuration exists.
            services.RemoveAll<NpgsqlDataSource>();
            services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
            services.RemoveAll<IVehicleImageBlobStore>();
            services.AddSingleton<IVehicleImageBlobStore>(_ => new AzureBlobVehicleImageStore(blobService, blobContainerName));
            services.RemoveAll<BlobServiceClient>();
            services.AddSingleton(_ => blobService);

            // Real JWT validation runs; only the key source is swapped so no call reaches the real identity provider.
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var configuration = new OpenIdConnectConfiguration { Issuer = TestJwt.Issuer };
                configuration.SigningKeys.Add(TestJwt.SigningKey);
                options.Configuration = configuration;
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                options.TokenValidationParameters.IssuerSigningKey = TestJwt.SigningKey;
                options.TokenValidationParameters.ValidIssuer = TestJwt.Issuer;
                options.TokenValidationParameters.IssuerValidator = null;
            });
        });
    }

    public HttpClient CreateClientWithToken(string? token)
    {
        var client = CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    // A client authenticated as the user with the given object id.
    public HttpClient CreateClientForUser(string oid) =>
        CreateClientWithToken(TestJwt.Create(claims: new Dictionary<string, object> { ["oid"] = oid }));

    // Seeds a tag directly so search tests do not depend on the tag endpoints.
    public async Task InsertTagAsync(Guid vehicleId, string name)
    {
        _ = Services;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "INSERT INTO vehicle_tags (id, vehicle_id, name, created_at) VALUES (gen_random_uuid(), @vehicleId, @name, now())", connection);
        command.Parameters.AddWithValue("vehicleId", vehicleId);
        command.Parameters.AddWithValue("name", name);
        await command.ExecuteNonQueryAsync();
    }

    // Removes every vehicle (and, by cascade, its tags, notes, and image rows) and every stored image file.
    public async Task ResetDataAsync()
    {
        // Starting the host applies the migrations and creates the blob container, so both exist before they are cleared.
        _ = Services;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("TRUNCATE TABLE vehicles CASCADE", connection);
        await command.ExecuteNonQueryAsync();

        await foreach (var blob in ImageContainer.GetBlobsAsync())
        {
            await ImageContainer.DeleteBlobIfExistsAsync(blob.Name);
        }
    }
}
