using MyGarage.Api.Inspections;
using MyGarage.Database.Common;
using MyGarage.Database.Inspections;
using MyGarage.Shared.Inspections;

namespace MyGarage.Api.Tests.Inspections;

public class InspectionMapperTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private static InspectionRecord Record(string type = "safety", DateOnly? nextDue = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            VehicleId = Guid.NewGuid(),
            InspectionType = type,
            InspectionDate = new DateOnly(2026, 5, 1),
            Cost = 25.50m,
            NextDueDate = nextDue,
            CreatedAt = At,
            UpdatedAt = At,
            CreatedBy = "owner-1",
            UpdatedBy = "owner-1",
        };

    [Theory]
    [InlineData(InspectionType.Safety, "safety")]
    [InlineData(InspectionType.Emissions, "emissions")]
    public void Type_ConvertsBothWays(InspectionType type, string stored)
    {
        Assert.Equal(stored, InspectionMapper.ToStoredType(type));
        Assert.Equal(type, InspectionMapper.FromStoredType(stored));
    }

    [Fact]
    public void UnknownTypes_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => InspectionMapper.ToStoredType((InspectionType)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => InspectionMapper.FromStoredType("visual"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("2027-05-01")]
    public void Record_MapsToTheDtoWithoutOwnerInformation(string? nextDue)
    {
        DateOnly? nextDueDate = nextDue is null ? null : DateOnly.Parse(nextDue);
        var record = Record("emissions", nextDueDate);

        var dto = InspectionMapper.ToDto(record);

        Assert.Equal(record.Id, dto.Id);
        Assert.Equal(record.VehicleId, dto.VehicleId);
        Assert.Equal(InspectionType.Emissions, dto.Type);
        Assert.Equal(new DateOnly(2026, 5, 1), dto.InspectionDate);
        Assert.Equal(25.50m, dto.Cost);
        Assert.Equal(nextDueDate, dto.NextDueDate);
        Assert.Equal(At, dto.CreatedAt);
        Assert.Equal(At, dto.UpdatedAt);
        Assert.DoesNotContain(typeof(InspectionDto).GetProperties(), property => property.Name.Contains("By", StringComparison.Ordinal) || property.Name.Contains("Owner", StringComparison.Ordinal));
    }

    [Fact]
    public void Page_MapsItemsAndCounts()
    {
        var page = new PagedResult<InspectionRecord>([Record(), Record("emissions")], 2, 10, 12);

        var dto = InspectionMapper.ToPageDto(page);

        Assert.Equal(2, dto.Items.Count);
        Assert.Equal(2, dto.Page);
        Assert.Equal(10, dto.PageSize);
        Assert.Equal(12, dto.TotalCount);
        Assert.Equal(2, dto.TotalPages);
    }
}
