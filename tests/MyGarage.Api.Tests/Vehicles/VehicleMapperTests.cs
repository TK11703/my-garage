using MyGarage.Api.Vehicles;
using MyGarage.Database.Common;
using MyGarage.Database.Vehicles;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Tests.Vehicles;

public class VehicleMapperTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ToRecord_TrimsValuesAndStampsOwnerAndTimes()
    {
        var id = Guid.NewGuid();
        var request = new VehicleRequest
        {
            Nickname = "  Daily  ",
            Make = " Ford ",
            Model = " F-150 ",
            Trim = " XLT ",
            Color = "  ",
            Mileage = 1234,
            Doors = 4,
        };

        var record = VehicleMapper.ToRecord(request, "owner-1", id, Now);

        Assert.Equal(id, record.Id);
        Assert.Equal("owner-1", record.OwnerId);
        Assert.Equal("Daily", record.Nickname);
        Assert.Equal("Ford", record.Make);
        Assert.Equal("F-150", record.Model);
        Assert.Equal("XLT", record.Trim);
        Assert.Null(record.Color);
        Assert.Equal(1234, record.Mileage);
        Assert.Equal(4, record.Doors);
        Assert.Equal(Now, record.CreatedAt);
        Assert.Equal(Now, record.UpdatedAt);
        Assert.Equal("owner-1", record.CreatedBy);
        Assert.Equal("owner-1", record.UpdatedBy);
    }

    [Fact]
    public void ToDetailDto_MapsEveryFieldWithEmptyChildCollections()
    {
        var record = new VehicleRecord
        {
            Id = Guid.NewGuid(),
            OwnerId = "owner-1",
            Nickname = "Daily",
            Make = "Ford",
            Model = "F-150",
            Trim = "XLT",
            Color = "Blue",
            Mileage = 10,
            Doors = 2,
            CreatedAt = Now,
            UpdatedAt = Now.AddHours(1),
            CreatedBy = "owner-1",
            UpdatedBy = "owner-1",
        };

        var dto = VehicleMapper.ToDetailDto(record, [], []);

        Assert.Equal(record.Id, dto.Id);
        Assert.Equal("Daily", dto.Nickname);
        Assert.Equal("Ford", dto.Make);
        Assert.Equal("F-150", dto.Model);
        Assert.Equal("XLT", dto.Trim);
        Assert.Equal("Blue", dto.Color);
        Assert.Equal(10, dto.Mileage);
        Assert.Equal(2, dto.Doors);
        Assert.Equal(Now, dto.CreatedAt);
        Assert.Equal(Now.AddHours(1), dto.UpdatedAt);
        Assert.Empty(dto.Tags);
        Assert.Empty(dto.Images);
    }

    [Fact]
    public void ToPageDto_MapsSummariesAndPaging()
    {
        var imageId = Guid.NewGuid();
        var summary = new VehicleSummaryRecord { Id = Guid.NewGuid(), Nickname = "A", Make = "B", Model = "C", Mileage = 5, MainImageId = imageId };
        var page = new PagedResult<VehicleSummaryRecord>([summary], 2, 12, 13);

        var dto = VehicleMapper.ToPageDto(page);

        Assert.Equal(2, dto.Page);
        Assert.Equal(12, dto.PageSize);
        Assert.Equal(13, dto.TotalCount);
        Assert.Equal(2, dto.TotalPages);
        var item = Assert.Single(dto.Items);
        Assert.Equal(summary.Id, item.Id);
        Assert.Equal("A", item.Nickname);
        Assert.Equal("B", item.Make);
        Assert.Equal("C", item.Model);
        Assert.Equal(5, item.Mileage);
        Assert.Equal(imageId, item.MainImageId);
    }

    [Fact]
    public void Dtos_HaveNoOwnerOrUserIdentifier()
    {
        var propertyNames = new[] { typeof(VehicleSummaryDto), typeof(VehicleDetailDto), typeof(VehicleRequest) }
            .SelectMany(type => type.GetProperties())
            .Select(property => property.Name);

        Assert.DoesNotContain(propertyNames, name => name.Contains("Owner", StringComparison.OrdinalIgnoreCase)
                                                    || name.Contains("UserId", StringComparison.OrdinalIgnoreCase));
    }
}
