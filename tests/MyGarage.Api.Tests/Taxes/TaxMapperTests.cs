using MyGarage.Api.Taxes;
using MyGarage.Database.Common;
using MyGarage.Database.Taxes;
using MyGarage.Shared.Taxes;

namespace MyGarage.Api.Tests.Taxes;

public class TaxMapperTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private static TaxRecord Record(string type = "personal_property", string recurrence = "annual") =>
        new()
        {
            Id = Guid.NewGuid(),
            VehicleId = Guid.NewGuid(),
            TaxType = type,
            Amount = 312.40m,
            Recurrence = recurrence,
            CreatedAt = At,
            UpdatedAt = At,
            CreatedBy = "owner-1",
            UpdatedBy = "owner-1",
        };

    [Theory]
    [InlineData(TaxType.PersonalProperty, "personal_property")]
    [InlineData(TaxType.Lease, "lease")]
    [InlineData(TaxType.Purchase, "purchase")]
    public void Type_ConvertsBothWays(TaxType type, string stored)
    {
        Assert.Equal(stored, TaxMapper.ToStoredType(type));
        Assert.Equal(type, TaxMapper.FromStoredType(stored));
    }

    [Theory]
    [InlineData(TaxRecurrence.Once, "once")]
    [InlineData(TaxRecurrence.Monthly, "monthly")]
    [InlineData(TaxRecurrence.BiAnnual, "bi_annual")]
    [InlineData(TaxRecurrence.Annual, "annual")]
    public void Recurrence_ConvertsBothWays(TaxRecurrence recurrence, string stored)
    {
        Assert.Equal(stored, TaxMapper.ToStoredRecurrence(recurrence));
        Assert.Equal(recurrence, TaxMapper.FromStoredRecurrence(stored));
    }

    [Fact]
    public void UnknownValues_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TaxMapper.ToStoredType((TaxType)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => TaxMapper.FromStoredType("sales"));
        Assert.Throws<ArgumentOutOfRangeException>(() => TaxMapper.ToStoredRecurrence((TaxRecurrence)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => TaxMapper.FromStoredRecurrence("weekly"));
    }

    [Fact]
    public void Record_MapsToTheDtoWithoutOwnerInformation()
    {
        var record = Record("purchase", "bi_annual");

        var dto = TaxMapper.ToDto(record);

        Assert.Equal(record.Id, dto.Id);
        Assert.Equal(record.VehicleId, dto.VehicleId);
        Assert.Equal(TaxType.Purchase, dto.Type);
        Assert.Equal(312.40m, dto.Amount);
        Assert.Equal(TaxRecurrence.BiAnnual, dto.Recurrence);
        Assert.Equal(At, dto.CreatedAt);
        Assert.Equal(At, dto.UpdatedAt);
        Assert.DoesNotContain(typeof(TaxDto).GetProperties(), property => property.Name.Contains("By", StringComparison.Ordinal) || property.Name.Contains("Owner", StringComparison.Ordinal));
    }

    [Fact]
    public void Page_MapsItemsAndCounts()
    {
        var page = new PagedResult<TaxRecord>([Record(), Record("lease", "monthly")], 2, 10, 12);

        var dto = TaxMapper.ToPageDto(page);

        Assert.Equal(2, dto.Items.Count);
        Assert.Equal(2, dto.Page);
        Assert.Equal(10, dto.PageSize);
        Assert.Equal(12, dto.TotalCount);
        Assert.Equal(2, dto.TotalPages);
    }
}
