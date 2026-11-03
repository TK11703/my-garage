using MyGarage.Api.Insurance;
using MyGarage.Database.Common;
using MyGarage.Database.Insurance;
using MyGarage.Shared.Insurance;

namespace MyGarage.Api.Tests.Insurance;

public class InsurancePolicyMapperTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private static InsurancePolicyRecord Record(string duration = "annual") =>
        new()
        {
            Id = Guid.NewGuid(),
            VehicleId = Guid.NewGuid(),
            StartDate = new DateOnly(2026, 5, 1),
            EndDate = new DateOnly(2027, 5, 1),
            Duration = duration,
            Cost = 120.50m,
            CreatedAt = At,
            UpdatedAt = At,
            CreatedBy = "owner-1",
            UpdatedBy = "owner-1",
        };

    [Theory]
    [InlineData(InsuranceDuration.Monthly, "monthly")]
    [InlineData(InsuranceDuration.BiAnnual, "bi_annual")]
    [InlineData(InsuranceDuration.Annual, "annual")]
    public void Duration_ConvertsBothWays(InsuranceDuration duration, string stored)
    {
        Assert.Equal(stored, InsurancePolicyMapper.ToStoredDuration(duration));
        Assert.Equal(duration, InsurancePolicyMapper.FromStoredDuration(stored));
    }

    [Fact]
    public void UnknownDurations_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => InsurancePolicyMapper.ToStoredDuration((InsuranceDuration)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => InsurancePolicyMapper.FromStoredDuration("weekly"));
    }

    [Fact]
    public void Record_MapsToTheDtoWithoutOwnerInformation()
    {
        var record = Record("bi_annual");

        var dto = InsurancePolicyMapper.ToDto(record);

        Assert.Equal(record.Id, dto.Id);
        Assert.Equal(record.VehicleId, dto.VehicleId);
        Assert.Equal(new DateOnly(2026, 5, 1), dto.StartDate);
        Assert.Equal(new DateOnly(2027, 5, 1), dto.EndDate);
        Assert.Equal(InsuranceDuration.BiAnnual, dto.Duration);
        Assert.Equal(120.50m, dto.Cost);
        Assert.Equal(At, dto.CreatedAt);
        Assert.Equal(At, dto.UpdatedAt);
        Assert.DoesNotContain(typeof(InsurancePolicyDto).GetProperties(), property => property.Name.Contains("By", StringComparison.Ordinal) || property.Name.Contains("Owner", StringComparison.Ordinal));
    }

    [Fact]
    public void Page_MapsItemsAndCounts()
    {
        var page = new PagedResult<InsurancePolicyRecord>([Record(), Record("monthly")], 2, 10, 12);

        var dto = InsurancePolicyMapper.ToPageDto(page);

        Assert.Equal(2, dto.Items.Count);
        Assert.Equal(2, dto.Page);
        Assert.Equal(10, dto.PageSize);
        Assert.Equal(12, dto.TotalCount);
        Assert.Equal(2, dto.TotalPages);
    }
}
