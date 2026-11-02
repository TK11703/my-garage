using MyGarage.Api.ServiceRecords;
using MyGarage.Database.Common;
using MyGarage.Database.ServiceRecords;
using MyGarage.Shared.ServiceRecords;

namespace MyGarage.Api.Tests.ServiceRecords;

public class ServiceRecordMapperTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private static ServiceRecordRecord Record(Guid? id = null, string? shop = "Joe's", int items = 0, decimal total = 0m) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            VehicleId = Guid.NewGuid(),
            DateServiced = new DateOnly(2026, 5, 1),
            OdometerReading = 45000,
            ShopOrPerson = shop,
            LineItemCount = items,
            TotalCost = total,
            CreatedAt = Now,
            UpdatedAt = Now.AddHours(1),
            CreatedBy = "owner-1",
            UpdatedBy = "owner-1",
        };

    [Theory]
    [InlineData("  Joe's Garage  ", "Joe's Garage")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void CleanShop_TrimsAndTurnsBlankIntoNull(string? input, string? expected) =>
        Assert.Equal(expected, ServiceRecordMapper.CleanShop(input));

    [Fact]
    public void ToSummaryDto_MapsEveryField()
    {
        var record = Record(items: 2, total: 229.25m);

        var dto = ServiceRecordMapper.ToSummaryDto(record);

        Assert.Equal(record.Id, dto.Id);
        Assert.Equal(new DateOnly(2026, 5, 1), dto.DateServiced);
        Assert.Equal(45000, dto.OdometerReading);
        Assert.Equal("Joe's", dto.ShopOrPerson);
        Assert.Equal(2, dto.LineItemCount);
        Assert.Equal(229.25m, dto.TotalCost);
    }

    [Fact]
    public void ToDto_ForANewRecord_HasNoLineItemsAndZeroTotal()
    {
        var record = Record();

        var dto = ServiceRecordMapper.ToDto(new ServiceRecordDetailRecord(record, []));

        Assert.Equal(record.Id, dto.Id);
        Assert.Equal(record.VehicleId, dto.VehicleId);
        Assert.Equal(new DateOnly(2026, 5, 1), dto.DateServiced);
        Assert.Equal(45000, dto.OdometerReading);
        Assert.Equal("Joe's", dto.ShopOrPerson);
        Assert.Empty(dto.LineItems);
        Assert.Equal(0m, dto.TotalCost);
        Assert.Equal(Now, dto.CreatedAt);
        Assert.Equal(Now.AddHours(1), dto.UpdatedAt);
    }

    [Fact]
    public void ToPageDto_CarriesPagingAndMapsItems()
    {
        var page = new PagedResult<ServiceRecordRecord>([Record(), Record()], 2, 2, 5);

        var dto = ServiceRecordMapper.ToPageDto(page);

        Assert.Equal(2, dto.Items.Count);
        Assert.Equal(2, dto.Page);
        Assert.Equal(2, dto.PageSize);
        Assert.Equal(5, dto.TotalCount);
        Assert.Equal(3, dto.TotalPages);
    }

    [Fact]
    public void ToDto_MapsAFlatLineWithNoLaborCostAndNoParts()
    {
        var record = Record(items: 1, total: 40m);
        var line = new ServiceLineItemRecord
        {
            Id = Guid.NewGuid(), ServiceRecordId = record.Id, Description = "Tire rotation", FlatCost = 40m, TotalCost = 40m,
        };

        var dto = ServiceRecordMapper.ToDto(new ServiceRecordDetailRecord(record, [line]));

        var mapped = Assert.Single(dto.LineItems);
        Assert.Equal(line.Id, mapped.Id);
        Assert.Equal("Tire rotation", mapped.Description);
        Assert.Equal(40m, mapped.FlatCost);
        Assert.Null(mapped.LaborHours);
        Assert.Null(mapped.LaborRate);
        Assert.Null(mapped.LaborCost);
        Assert.Empty(mapped.Parts);
        Assert.Equal(40m, mapped.TotalCost);
        Assert.Equal(40m, dto.TotalCost);
    }

    [Fact]
    public void ToDto_MapsABreakdownLineWithLaborCostAndPartTotals()
    {
        var record = Record(items: 1, total: 188.75m);
        var line = new ServiceLineItemRecord
        {
            Id = Guid.NewGuid(),
            ServiceRecordId = record.Id,
            Description = "Oil change",
            LaborHours = 1.5m,
            LaborRate = 90m,
            TotalCost = 188.75m,
            Parts =
            [
                new ServiceLinePartRecord { Position = 1, Name = "Oil filter", Quantity = 1m, UnitPrice = 12.50m },
                new ServiceLinePartRecord { Position = 2, Name = "Oil", Quantity = 5m, UnitPrice = 8.25m },
            ],
        };

        var mapped = Assert.Single(ServiceRecordMapper.ToDto(new ServiceRecordDetailRecord(record, [line])).LineItems);

        Assert.Null(mapped.FlatCost);
        Assert.Equal(135m, mapped.LaborCost);
        Assert.Equal(["Oil filter", "Oil"], mapped.Parts.Select(part => part.Name));
        Assert.Equal([12.50m, 41.25m], mapped.Parts.Select(part => part.TotalPrice));
        Assert.Equal(188.75m, mapped.TotalCost);
    }

    [Fact]
    public void ToInput_TrimsTheDescriptionAndPartNamesAndKeepsThePartOrder()
    {
        var request = new ServiceLineItemRequest
        {
            Description = "  Brakes  ",
            LaborHours = 2m,
            LaborRate = 80m,
            Parts =
            [
                new ServicePartRequest { Name = " Pads ", Quantity = 2m, UnitPrice = 30m },
                new ServicePartRequest { Name = "Rotor", Quantity = 1m, UnitPrice = 90m },
            ],
        };

        var input = ServiceRecordMapper.ToInput(request);

        Assert.Equal("Brakes", input.Description);
        Assert.Null(input.FlatCost);
        Assert.Equal(2m, input.LaborHours);
        Assert.Equal(80m, input.LaborRate);
        Assert.Equal(["Pads", "Rotor"], input.Parts.Select(part => part.Name));
        Assert.Equal([2m, 1m], input.Parts.Select(part => part.Quantity));
        Assert.Equal([30m, 90m], input.Parts.Select(part => part.UnitPrice));
    }

    [Fact]
    public void Dtos_ExposeNoOwnerOrUserIdentifiers()
    {
        var names = typeof(ServiceRecordDto).GetProperties().Concat(typeof(ServiceRecordSummaryDto).GetProperties())
            .Select(property => property.Name)
            .ToList();

        Assert.DoesNotContain(names, name => name.Contains("Owner", StringComparison.OrdinalIgnoreCase)
                                             || name.Contains("User", StringComparison.OrdinalIgnoreCase)
                                             || name.Contains("By", StringComparison.Ordinal));
    }
}
