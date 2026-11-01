using Microsoft.Extensions.Logging.Abstractions;
using MyGarage.Database.Common;
using MyGarage.Database.VehicleImages;
using Npgsql;

namespace MyGarage.Database.Tests.Common;

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<PostgresFixture>, ICollectionFixture<AzuriteFixture>
{
    public const string Name = "database";
}

public static class PostgresFixtureExtensions
{
    // A fresh database with every migration applied.
    public static async Task<NpgsqlDataSource> CreateMigratedDatabaseAsync(this PostgresFixture fixture)
    {
        var dataSource = await fixture.CreateDatabaseAsync();
        await new MigrationRunner(dataSource, new SqlScriptStore(), NullLogger<MigrationRunner>.Instance).RunAsync();
        return dataSource;
    }
}

// Wraps a blob store to count calls and to inject failures.
public sealed class RecordingBlobStore(IVehicleImageBlobStore inner) : IVehicleImageBlobStore
{
    public int Uploads { get; private set; }

    public int Reads { get; private set; }

    public int Deletes { get; private set; }

    public bool FailUploads { get; set; }

    public bool FailDeletes { get; set; }

    public Task EnsureContainerAsync(CancellationToken cancellationToken) => inner.EnsureContainerAsync(cancellationToken);

    public Task UploadAsync(Guid vehicleId, Guid imageId, Stream content, string contentType, CancellationToken cancellationToken)
    {
        Uploads++;
        return FailUploads ? throw new ImageStorageUnavailableException("injected upload failure") : inner.UploadAsync(vehicleId, imageId, content, contentType, cancellationToken);
    }

    public Task<Stream?> OpenReadAsync(Guid vehicleId, Guid imageId, CancellationToken cancellationToken)
    {
        Reads++;
        return inner.OpenReadAsync(vehicleId, imageId, cancellationToken);
    }

    public Task DeleteAsync(Guid vehicleId, Guid imageId, CancellationToken cancellationToken)
    {
        Deletes++;
        return FailDeletes ? throw new ImageStorageUnavailableException("injected delete failure") : inner.DeleteAsync(vehicleId, imageId, cancellationToken);
    }

    public Task DeleteVehicleAsync(Guid vehicleId, CancellationToken cancellationToken)
    {
        Deletes++;
        return FailDeletes ? throw new ImageStorageUnavailableException("injected delete failure") : inner.DeleteVehicleAsync(vehicleId, cancellationToken);
    }
}
