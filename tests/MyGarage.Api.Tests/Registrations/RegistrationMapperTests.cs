using MyGarage.Api.Registrations;
using MyGarage.Database.Common;
using MyGarage.Database.Registrations;
using MyGarage.Shared.Registrations;

namespace MyGarage.Api.Tests.Registrations;

public class RegistrationMapperTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private static RegistrationRecord Record(string state = "WA") =>
        new()
        {
            Id = Guid.NewGuid(),
            VehicleId = Guid.NewGuid(),
            State = state,
            Cost = 78.75m,
            ExpirationDate = new DateOnly(2027, 5, 1),
            CreatedAt = At,
            UpdatedAt = At,
            CreatedBy = "owner-1",
            UpdatedBy = "owner-1",
        };

    [Fact]
    public void Record_MapsToTheDtoWithoutOwnerInformation()
    {
        var record = Record("DC");

        var dto = RegistrationMapper.ToDto(record);

        Assert.Equal(record.Id, dto.Id);
        Assert.Equal(record.VehicleId, dto.VehicleId);
        Assert.Equal("DC", dto.State);
        Assert.Equal(78.75m, dto.Cost);
        Assert.Equal(new DateOnly(2027, 5, 1), dto.ExpirationDate);
        Assert.Equal(At, dto.CreatedAt);
        Assert.Equal(At, dto.UpdatedAt);
        Assert.DoesNotContain(typeof(RegistrationDto).GetProperties(), property => property.Name.Contains("By", StringComparison.Ordinal) || property.Name.Contains("Owner", StringComparison.Ordinal));
    }

    [Fact]
    public void Page_MapsItemsAndCounts()
    {
        var page = new PagedResult<RegistrationRecord>([Record(), Record("OR")], 2, 10, 12);

        var dto = RegistrationMapper.ToPageDto(page);

        Assert.Equal(2, dto.Items.Count);
        Assert.Equal(2, dto.Page);
        Assert.Equal(10, dto.PageSize);
        Assert.Equal(12, dto.TotalCount);
        Assert.Equal(2, dto.TotalPages);
    }
}
