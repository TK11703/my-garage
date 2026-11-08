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
using MyGarage.Api.EmailIntake.Extraction;
using MyGarage.Api.Vehicles.StickerImport;
using MyGarage.Api.Vehicles.Tags;
using MyGarage.Database.EmailIntake;
using MyGarage.Database.VehicleDocuments;
using MyGarage.Database.VehicleImages;
using Npgsql;

namespace MyGarage.Api.Tests.Common;

public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private string connectionString = string.Empty;
    private BlobServiceClient blobService = null!;
    private readonly string blobContainerName = $"api-{Guid.NewGuid():N}";
    private readonly string intakeContainerName = $"intake-{Guid.NewGuid():N}";
    private readonly string documentContainerName = $"documents-{Guid.NewGuid():N}";

    // Runs before the host is built, so the configuration below can use the database and storage created here.
    async Task IAsyncLifetime.InitializeAsync()
    {
        connectionString = await SharedPostgres.CreateDatabaseAsync();
        blobService = await SharedAzurite.CreateServiceClientAsync();
    }

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;

    // The blob container this factory's API writes images to.
    public BlobContainerClient ImageContainer => blobService.GetBlobContainerClient(blobContainerName);

    // This factory's own containers for email attachments and kept documents, so parallel test classes never share one.
    public string IntakeContainerName => intakeContainerName;

    public string DocumentContainerName => documentContainerName;

    public BlobContainerClient IntakeContainer => blobService.GetBlobContainerClient(intakeContainerName);

    public BlobContainerClient DocumentContainer => blobService.GetBlobContainerClient(documentContainerName);

    // Replaces the AI extractor for every request this factory serves.
    public FakeDraftExtractor Extractor { get; } = new();

    // Replaces the window-sticker reader for every request this factory serves.
    public FakeStickerExtractor StickerExtractor { get; } = new();

    // Replaces the feature categorizer for every request this factory serves.
    public FakeFeatureCategorizer FeatureCategorizer { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAd:TenantId"] = TestJwt.TenantId,
                ["AzureAd:Api:ClientId"] = TestJwt.ApiClientId,
                ["AzureAd:Api:Scope"] = TestJwt.Scope,
                // The polling loop would race the tests; they call SubmissionProcessor.ProcessOnceAsync themselves.
                ["EmailIntake:Processing:Enabled"] = "false",
                // Tests call DraftExpiryService.RunOnceAsync themselves so a background run never expires their seeded drafts.
                ["Drafts:Expiry:Enabled"] = "false",
                // Tests call ReminderSweepService.RunOnceAsync themselves so a background run never raises notifications they did not expect.
                ["Reminders:Sweep:Enabled"] = "false",
                // Many tests read stickers as the same user inside one minute; the rate-limit tests set their own limit.
                ["StickerImport:RequestsPerMinute"] = "1000",
                ["DraftUploads:RequestsPerMinute"] = "1000",
                // Tests call FeatureCategorizationService.CategorizePendingAsync themselves so a background run never categorizes their seeded features.
                ["FeatureCategories:SweepEnabled"] = "false",
            }));

        builder.ConfigureServices(services =>
        {
            // Aspire reads connection strings when the clients are registered, which is before test configuration exists.
            services.RemoveAll<NpgsqlDataSource>();
            services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
            services.RemoveAll<IVehicleImageBlobStore>();
            services.AddSingleton<IVehicleImageBlobStore>(_ => new AzureBlobVehicleImageStore(blobService, blobContainerName));
            services.RemoveAll<IIntakeBlobStore>();
            services.AddSingleton<IIntakeBlobStore>(_ => new AzureBlobIntakeStore(blobService, intakeContainerName));
            services.RemoveAll<IVehicleDocumentBlobStore>();
            services.AddSingleton<IVehicleDocumentBlobStore>(_ => new AzureBlobVehicleDocumentStore(blobService, documentContainerName, intakeContainerName));
            services.RemoveAll<BlobServiceClient>();
            services.AddSingleton(_ => blobService);
            services.RemoveAll<IDraftExtractor>();
            services.AddSingleton<IDraftExtractor>(Extractor);
            services.RemoveAll<IStickerExtractor>();
            services.AddSingleton<IStickerExtractor>(StickerExtractor);
            services.RemoveAll<IFeatureCategorizer>();
            services.AddSingleton<IFeatureCategorizer>(FeatureCategorizer);

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

    // A client authenticated as the user with the given object id (and, when given, the email claim).
    public HttpClient CreateClientForUser(string oid, string? email = null)
    {
        var claims = new Dictionary<string, object> { ["oid"] = oid };
        if (email is not null)
        {
            claims["email"] = email;
        }

        return CreateClientWithToken(TestJwt.Create(claims: claims));
    }

    // A client authenticated as an app (no user, no scp) holding the given app roles; like Entra, an empty set omits the claim.
    public HttpClient CreateClientForApp(IEnumerable<string> roles, string appId = TestJwt.WorkflowAppId)
    {
        var claims = new Dictionary<string, object> { ["oid"] = appId, ["azp"] = appId };
        var roleArray = roles.ToArray();
        if (roleArray.Length > 0)
        {
            claims["roles"] = roleArray;
        }

        return CreateClientWithToken(TestJwt.Create(scope: null, includeUserClaims: false, claims: claims));
    }

    // A client authenticated as the trusted email workflow: roles claim Email.Ingest and no scp.
    public HttpClient CreateClientForWorkflow() => CreateClientForApp([TestJwt.EmailIngestRole]);

    // A valid token that carries neither the Email.Ingest role nor the access_as_user scope.
    public HttpClient CreateClientWithNeitherRoleNorScope() => CreateClientForApp([]);

    // Runs one parameterized statement directly against this factory's database (test seeding and assertions).
    public async Task<object?> ExecuteScalarAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        _ = Services;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        var result = await command.ExecuteScalarAsync();
        return result is DBNull ? null : result;
    }

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

    // Removes every vehicle (and, by cascade, its tags, notes, image rows, and detached drafts), the shared feature catalog, every user's lookup items and choices (never the defaults), every user profile and email submission (and, by cascade, their attachments, drafts, and notifications), and every stored image, attachment, and document file.
    public async Task ResetDataAsync()
    {
        // Starting the host applies the migrations and creates the blob container, so both exist before they are cleared.
        _ = Services;
        Extractor.Reset();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "TRUNCATE TABLE vehicles, features, user_profiles, email_submissions CASCADE; DELETE FROM lookup_item_hidden; DELETE FROM lookup_items WHERE owner_id IS NOT NULL;", connection);
        await command.ExecuteNonQueryAsync();

        foreach (var container in new[] { ImageContainer, IntakeContainer, DocumentContainer })
        {
            // The email containers are created by hosted services, so one may not exist yet.
            if (!await container.ExistsAsync())
            {
                continue;
            }

            await foreach (var blob in container.GetBlobsAsync())
            {
                await container.DeleteBlobIfExistsAsync(blob.Name);
            }
        }
    }
}
