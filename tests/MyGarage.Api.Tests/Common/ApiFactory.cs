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

    // Seeds a tag (finding or creating the shared feature) directly so search tests do not depend on the tag endpoints.
    public async Task InsertTagAsync(Guid vehicleId, string name)
    {
        _ = Services;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "WITH f AS (INSERT INTO features (id, name, created_at, created_by) VALUES (gen_random_uuid(), @name, now(), 'test') " +
            "ON CONFLICT (name_key) DO UPDATE SET name = features.name RETURNING id) " +
            "INSERT INTO vehicle_tags (id, vehicle_id, feature_id, created_at) SELECT gen_random_uuid(), @vehicleId, id, now() FROM f", connection);
        command.Parameters.AddWithValue("vehicleId", vehicleId);
        command.Parameters.AddWithValue("name", name);
        await command.ExecuteNonQueryAsync();
    }

    // Number of entries in the shared feature catalog.
    public async Task<long> CountFeaturesAsync()
    {
        _ = Services;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM features", connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    // Seeds a vehicle in the shape of one saved before the year existed; returns its id.
    public async Task<Guid> InsertVehicleWithoutYearAsync(string ownerId, string make = "Ford", string model = "F-150")
    {
        _ = Services;

        var id = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "INSERT INTO vehicles (id, owner_id, make, model, mileage, color, doors, created_at, updated_at, created_by, updated_by) " +
            "VALUES (@id, @ownerId, @make, @model, 1200, 'Red', 4, now(), now(), @ownerId, @ownerId)", connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("ownerId", ownerId);
        command.Parameters.AddWithValue("make", make);
        command.Parameters.AddWithValue("model", model);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    // Seeds a custom lookup item for an owner directly; returns its id.
    public async Task<Guid> InsertLookupItemAsync(string ownerId, string kind, string name)
    {
        _ = Services;

        var id = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "INSERT INTO lookup_items (id, kind, owner_id, name, created_at, created_by) VALUES (@id, @kind, @ownerId, @name, now(), @ownerId)", connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("kind", kind);
        command.Parameters.AddWithValue("ownerId", ownerId);
        command.Parameters.AddWithValue("name", name);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    // Hides an item for an owner directly.
    public async Task HideLookupItemAsync(string ownerId, Guid itemId)
    {
        _ = Services;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "INSERT INTO lookup_item_hidden (owner_id, item_id, hidden_at) VALUES (@ownerId, @itemId, now())", connection);
        command.Parameters.AddWithValue("ownerId", ownerId);
        command.Parameters.AddWithValue("itemId", itemId);
        await command.ExecuteNonQueryAsync();
    }

    // Removes every vehicle (and, by cascade, its tags, notes, and image rows), the shared feature catalog, every user's lookup items and choices (never the defaults), and every stored image file.
    public async Task ResetDataAsync()
    {
        // Starting the host applies the migrations and creates the blob container, so both exist before they are cleared.
        _ = Services;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "TRUNCATE TABLE vehicles, features CASCADE; DELETE FROM lookup_item_hidden; DELETE FROM lookup_items WHERE owner_id IS NOT NULL;", connection);
        await command.ExecuteNonQueryAsync();

        await foreach (var blob in ImageContainer.GetBlobsAsync())
        {
            await ImageContainer.DeleteBlobIfExistsAsync(blob.Name);
        }
    }
}
