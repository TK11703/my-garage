using MyGarage.Api.Vehicles;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Tests.Vehicles;

public class VehicleRequestValidatorTests
{
    // 2026-11-01 (UTC): next calendar year is 2027.
    private static readonly DateTimeOffset Today = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly VehicleRequestValidator validator = new(new FixedTimeProvider(Today));

    private static VehicleRequest Valid(
        string? make = "Ford",
        string? model = "F-150",
        string? trim = null,
        string? color = null,
        int? mileage = null,
        int? doors = null,
        int? year = 2020) =>
        new() { Make = make, Model = model, Trim = trim, Color = color, Mileage = mileage, Doors = doors, Year = year };

    private bool IsValid(VehicleRequest request) => validator.Validate(request).IsValid;

    private string[] ErrorFields(VehicleRequest request) =>
        [.. validator.Validate(request).Errors.Select(error => error.PropertyName).Distinct()];

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public void MinimalRequest_IsValid() => Assert.True(IsValid(Valid()));

    [Fact]
    public void FullRequest_IsValid() =>
        Assert.True(IsValid(Valid(trim: "XLT", color: "Blue", mileage: 100_000, doors: 4)));

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void MakeAndModel_Required(string? value)
    {
        Assert.False(IsValid(Valid(make: value)));
        Assert.False(IsValid(Valid(model: value)));
    }

    [Fact]
    public void MakeAndModel_MaxLength50()
    {
        Assert.True(IsValid(Valid(make: new string('a', 50), model: new string('a', 50))));
        Assert.False(IsValid(Valid(make: new string('a', 51))));
        Assert.False(IsValid(Valid(model: new string('a', 51))));
    }

    [Fact]
    public void Trim_OptionalMaxLength50()
    {
        Assert.True(IsValid(Valid(trim: new string('a', 50))));
        Assert.False(IsValid(Valid(trim: new string('a', 51))));
    }

    [Fact]
    public void Color_OptionalMaxLength30()
    {
        Assert.True(IsValid(Valid(color: new string('a', 30))));
        Assert.False(IsValid(Valid(color: new string('a', 31))));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(9_999_999, true)]
    [InlineData(-1, false)]
    [InlineData(10_000_000, false)]
    public void Mileage_Range(int mileage, bool valid) => Assert.Equal(valid, IsValid(Valid(mileage: mileage)));

    [Theory]
    [InlineData(1, true)]
    [InlineData(6, true)]
    [InlineData(0, false)]
    [InlineData(7, false)]
    public void Doors_Range(int doors, bool valid) => Assert.Equal(valid, IsValid(Valid(doors: doors)));

    [Fact]
    public void ControlCharacters_Rejected()
    {
        Assert.False(IsValid(Valid(make: "bad\nmake")));
        Assert.False(IsValid(Valid(model: "bad\tmodel")));
        Assert.False(IsValid(Valid(trim: "bad\u0000trim")));
        Assert.False(IsValid(Valid(color: "bad\rcolor")));
    }

    [Fact]
    public void QuotesAndAngleBrackets_Allowed() =>
        Assert.True(IsValid(Valid(model: "Bob's \"<b>truck</b>\"")));

    [Theory]
    [InlineData(1899, false)]
    [InlineData(1900, true)]
    [InlineData(2027, true)]
    [InlineData(2028, false)]
    public void Year_BetweenNineteenHundredAndNextCalendarYear(int year, bool valid)
    {
        Assert.Equal(valid, IsValid(Valid(year: year)));
        if (!valid)
        {
            Assert.Contains("Year", ErrorFields(Valid(year: year)));
        }
    }

    [Fact]
    public void Year_Required() => Assert.Contains("Year", ErrorFields(Valid(year: null)));

    [Theory]
    [InlineData("1HGCM82633A004352", true)]
    [InlineData("  1hgcm82633a004352  ", true)]
    [InlineData("1HGCM82633A00435", false)]
    [InlineData("1HGCM82633A0043522", false)]
    [InlineData("1HGCM82633A00435I", false)]
    [InlineData("1HGCM82633A00435O", false)]
    [InlineData("1HGCM82633A00435Q", false)]
    [InlineData("1HGCM82633A00435-", false)]
    public void Vin_Is17AllowedCharactersAfterTrimmingAndUpperCasing(string vin, bool valid)
    {
        var request = new VehicleRequest { Make = "m", Model = "x", Year = 2020, Vin = vin };

        Assert.Equal(valid, IsValid(request));
        if (!valid)
        {
            Assert.Contains("Vin", ErrorFields(request));
        }
    }

    [Fact]
    public void Vin_BlankIsTreatedAsNotSet() =>
        Assert.True(IsValid(new VehicleRequest { Make = "m", Model = "x", Year = 2020, Vin = "  " }));

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(3000, true)]
    [InlineData(3001, false)]
    public void Horsepower_Range(int horsepower, bool valid) =>
        Assert.Equal(valid, IsValid(new VehicleRequest { Make = "m", Model = "x", Year = 2020, Horsepower = horsepower }));

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(20, true)]
    [InlineData(21, false)]
    public void Seats_Range(int seats, bool valid) =>
        Assert.Equal(valid, IsValid(new VehicleRequest { Make = "m", Model = "x", Year = 2020, Seats = seats }));

    [Theory]
    [InlineData("9.9", false)]
    [InlineData("10", true)]
    [InlineData("17.5", true)]
    [InlineData("30", true)]
    [InlineData("30.1", false)]
    [InlineData("17.55", false)]
    public void WheelSize_BetweenTenAndThirtyWithOneDecimal(string size, bool valid) =>
        Assert.Equal(valid, IsValid(new VehicleRequest
        {
            Make = "m", Model = "x", Year = 2020, WheelSize = decimal.Parse(size, System.Globalization.CultureInfo.InvariantCulture),
        }));

    [Fact]
    public void EngineAndTireSize_LengthLimitsAndControlCharacters()
    {
        VehicleRequest With(string? engine = null, string? tire = null) =>
            new() { Make = "m", Model = "x", Year = 2020, Engine = engine, TireSize = tire };

        Assert.True(IsValid(With(engine: new string('a', 100), tire: new string('a', 20))));
        Assert.False(IsValid(With(engine: new string('a', 101))));
        Assert.False(IsValid(With(tire: new string('a', 21))));
        Assert.False(IsValid(With(engine: "bad\nengine")));
        Assert.False(IsValid(With(tire: "bad\ttire")));
    }

    [Fact]
    public void PurchasedOn_NotInTheFuture()
    {
        VehicleRequest With(DateOnly date) => new() { Make = "m", Model = "x", Year = 2020, PurchasedOn = date };

        Assert.True(IsValid(With(new DateOnly(2026, 11, 1))));
        Assert.True(IsValid(With(new DateOnly(2020, 1, 1))));
        Assert.False(IsValid(With(new DateOnly(2026, 11, 2))));
        Assert.Contains("PurchasedOn", ErrorFields(With(new DateOnly(2026, 11, 2))));
    }

    [Fact]
    public void LeaseEndsOn_OnlyForLeasedAndNotBeforePurchase()
    {
        VehicleRequest With(OwnershipType? ownership, DateOnly? purchased, DateOnly? ends) =>
            new() { Make = "m", Model = "x", Year = 2020, Ownership = ownership, PurchasedOn = purchased, LeaseEndsOn = ends };

        Assert.True(IsValid(With(OwnershipType.Leased, new DateOnly(2025, 5, 1), new DateOnly(2028, 5, 1))));
        Assert.True(IsValid(With(OwnershipType.Leased, null, new DateOnly(2028, 5, 1))));
        Assert.True(IsValid(With(OwnershipType.Leased, new DateOnly(2025, 5, 1), new DateOnly(2025, 5, 1))));
        Assert.True(IsValid(With(OwnershipType.Purchased, new DateOnly(2025, 5, 1), null)));

        Assert.Contains("LeaseEndsOn", ErrorFields(With(OwnershipType.Purchased, null, new DateOnly(2028, 5, 1))));
        Assert.Contains("LeaseEndsOn", ErrorFields(With(null, null, new DateOnly(2028, 5, 1))));
        Assert.Contains("LeaseEndsOn", ErrorFields(With(OwnershipType.Leased, new DateOnly(2025, 5, 1), new DateOnly(2025, 4, 30))));
    }

    [Fact]
    public void ErrorsAreKeyedByThePropertyName() =>
        Assert.Equal(
            ["Horsepower", "Make", "Year"],
            ErrorFields(new VehicleRequest { Make = "", Model = "x", Year = null, Horsepower = 0 }).Order());
}
