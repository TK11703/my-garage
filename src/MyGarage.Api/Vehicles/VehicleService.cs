using MyGarage.Api.Vehicles.Images;
using MyGarage.Api.Vehicles.Tags;
using MyGarage.Database.Tags;
using MyGarage.Database.VehicleImages;
using MyGarage.Database.Vehicles;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Vehicles;

public sealed class VehicleService(
    VehicleRepository vehicles,
    VehicleTagRepository tags,
    VehicleImageRepository images,
    TimeProvider timeProvider)
{
    // PostgreSQL stores microseconds, so the value returned after a write must not carry finer ticks.
    private DateTimeOffset Now()
    {
        var now = timeProvider.GetUtcNow();
        return now.AddTicks(-(now.Ticks % 10));
    }

    public async Task<VehicleDetailDto> CreateAsync(string ownerId, VehicleRequest request, CancellationToken cancellationToken)
    {
        var record = VehicleMapper.ToRecord(request, ownerId, Guid.CreateVersion7(), Now());
        await vehicles.InsertAsync(record, cancellationToken);

        return VehicleMapper.ToDetailDto(record, [], []);
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

    public async Task<VehicleDetailDto?> UpdateAsync(string ownerId, Guid vehicleId, VehicleRequest request, CancellationToken cancellationToken)
    {
        var record = VehicleMapper.ToRecord(request, ownerId, vehicleId, Now());
        if (!await vehicles.UpdateAsync(record, cancellationToken))
        {
            return null;
        }

        return await GetAsync(ownerId, vehicleId, cancellationToken);
    }

    // The vehicle and its image rows go first; the image files are then removed (best effort, a failure leaves private orphans).
    public async Task<bool> DeleteAsync(string ownerId, Guid vehicleId, CancellationToken cancellationToken)
    {
        if (!await vehicles.DeleteAsync(ownerId, vehicleId, cancellationToken))
        {
            return false;
        }

        await images.DeleteBlobsForVehicleAsync(vehicleId);
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

    public async Task<VehicleFilterOptionsDto> GetFilterOptionsAsync(string ownerId, CancellationToken cancellationToken) =>
        VehicleMapper.ToFilterOptionsDto(await vehicles.GetFilterOptionsAsync(ownerId, cancellationToken));
}
