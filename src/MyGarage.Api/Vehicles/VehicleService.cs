using MyGarage.Api.Vehicles.Images;
using MyGarage.Api.Vehicles.StickerImport;
using MyGarage.Api.Vehicles.Tags;
using MyGarage.Database.Tags;
using MyGarage.Database.VehicleDocuments;
using MyGarage.Database.VehicleImages;
using MyGarage.Database.Vehicles;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Vehicles;

// Vehicle is null when nothing was written: Status is NotFound (not the owner's) or FeatureLimit (the sticker features would exceed the per-vehicle limit).
public sealed record VehicleSaveResult(VehicleDetailDto? Vehicle, VehicleFeatureWriteStatus Status);

public sealed class VehicleService(
    VehicleRepository vehicles,
    VehicleTagRepository tags,
    VehicleImageRepository images,
    VehicleDocumentRepository documents,
    FeatureCategorizationService categorization,
    TimeProvider timeProvider)
{
    // PostgreSQL stores microseconds, so the value returned after a write must not carry finer ticks.
    private DateTimeOffset Now()
    {
        var now = timeProvider.GetUtcNow();
        return now.AddTicks(-(now.Ticks % 10));
    }

    public async Task<VehicleSaveResult> CreateAsync(string ownerId, VehicleRequest request, CancellationToken cancellationToken)
    {
        var now = Now();
        var record = VehicleMapper.ToRecord(request, ownerId, Guid.CreateVersion7(), now);
        var features = StickerFeatureMapper.ToWrites(request);
        if (features.Count == 0)
        {
            await vehicles.InsertAsync(record, cancellationToken);
        }
        else if (await vehicles.InsertWithFeaturesAsync(record, features, now, cancellationToken) is var status and not VehicleFeatureWriteStatus.Written)
        {
            return new VehicleSaveResult(null, status);
        }

        if (features.Count > 0)
        {
            await categorization.CategorizePendingAsync(cancellationToken);
        }

        // Read back so the lookup names come from the stored references.
        return new VehicleSaveResult(await GetAsync(ownerId, record.Id, cancellationToken), VehicleFeatureWriteStatus.Written);
    }

    public async Task<VehicleDetailDto?> GetAsync(string ownerId, Guid vehicleId, CancellationToken cancellationToken)
    {
        var record = await vehicles.GetByIdAsync(ownerId, vehicleId, cancellationToken);
        if (record is null)
        {
            return null;
        }

        var vehicleTags = await tags.ListAsync(ownerId, vehicleId, cancellationToken);
        var vehicleImages = await images.ListAsync(ownerId, vehicleId, cancellationToken);
        return VehicleMapper.ToDetailDto(record, VehicleTagMapper.ToDtos(vehicleTags), VehicleImageMapper.ToDtos(vehicleImages));
    }

    public async Task<VehicleSaveResult> UpdateAsync(string ownerId, Guid vehicleId, VehicleRequest request, CancellationToken cancellationToken)
    {
        var now = Now();
        var record = VehicleMapper.ToRecord(request, ownerId, vehicleId, now);
        var features = StickerFeatureMapper.ToWrites(request);
        if (features.Count == 0)
        {
            if (!await vehicles.UpdateAsync(record, cancellationToken))
            {
                return new VehicleSaveResult(null, VehicleFeatureWriteStatus.NotFound);
            }
        }
        else if (await vehicles.UpdateWithFeaturesAsync(record, features, now, cancellationToken) is var status and not VehicleFeatureWriteStatus.Written)
        {
            return new VehicleSaveResult(null, status);
        }

        if (features.Count > 0)
        {
            await categorization.CategorizePendingAsync(cancellationToken);
        }

        return new VehicleSaveResult(await GetAsync(ownerId, vehicleId, cancellationToken), VehicleFeatureWriteStatus.Written);
    }

    // The vehicle and its image and document rows go first (drafts stay, detached); the files are then removed (best effort, a failure leaves private orphans).
    public async Task<bool> DeleteAsync(string ownerId, Guid vehicleId, CancellationToken cancellationToken)
    {
        if (!await vehicles.DeleteAsync(ownerId, vehicleId, cancellationToken))
        {
            return false;
        }

        await images.DeleteBlobsForVehicleAsync(vehicleId);
        await documents.DeleteBlobsForVehicleAsync(vehicleId);
        return true;
    }

    // A page beyond the last is answered with the last page; the returned Page is the one served.
    public async Task<PagedResultDto<VehicleSummaryDto>> ListAsync(
        string ownerId,
        VehicleSearchCriteria criteria,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var result = await vehicles.ListPageAsync(ownerId, criteria, page, pageSize, cancellationToken);

        var effectivePage = Math.Max(1, Math.Min(page, result.TotalPages));
        if (effectivePage != page)
        {
            result = await vehicles.ListPageAsync(ownerId, criteria, effectivePage, pageSize, cancellationToken);
        }

        return VehicleMapper.ToPageDto(result);
    }

    public async Task<VehicleFilterOptionsDto> GetFilterOptionsAsync(string ownerId, string? status, CancellationToken cancellationToken) =>
        VehicleMapper.ToFilterOptionsDto(await vehicles.GetFilterOptionsAsync(ownerId, status ?? VehicleStatusFilter.Active, cancellationToken));

    public async Task<VehicleStateResult> ArchiveAsync(string ownerId, Guid vehicleId, CancellationToken cancellationToken)
    {
        var change = await vehicles.ArchiveAsync(ownerId, vehicleId, Now(), cancellationToken);
        if (change == VehicleStateChange.Changed)
        {
            VehicleMetrics.Archived.Add(1);
        }

        return new VehicleStateResult(change, change == VehicleStateChange.Changed ? await GetAsync(ownerId, vehicleId, cancellationToken) : null);
    }

    public async Task<VehicleStateResult> RestoreAsync(string ownerId, Guid vehicleId, CancellationToken cancellationToken)
    {
        var change = await vehicles.RestoreAsync(ownerId, vehicleId, Now(), cancellationToken);
        if (change == VehicleStateChange.Changed)
        {
            VehicleMetrics.Restored.Add(1);
        }

        return new VehicleStateResult(change, change == VehicleStateChange.Changed ? await GetAsync(ownerId, vehicleId, cancellationToken) : null);
    }
}

// Vehicle is set only when the change was made.
public sealed record VehicleStateResult(VehicleStateChange Change, VehicleDetailDto? Vehicle);
