using MyGarage.Api.ServiceRecords;
using MyGarage.Shared.ServiceRecords;

namespace MyGarage.Api.Tests.ServiceRecords;

public class ServiceRecordRequestValidatorTests
{
    // 2026-11-02 (UTC).
    private static readonly DateTimeOffset Today = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private readonly ServiceRecordRequestValidator validator = new(new FixedTimeProvider(Today));

    private static ServiceRecordRequest Valid(
        DateOnly? date = null, int? odometer = 45000, string? shop = "Joe's Garage", bool useDefaultDate = true) =>
        new()
        {
            DateServiced = useDefaultDate ? date ?? new DateOnly(2026, 5, 1) : date,
            OdometerReading = odometer,
            ShopOrPerson = shop,
        };

    private bool IsValid(ServiceRecordRequest request) => validator.Validate(request).IsValid;

    private string[] ErrorFields(ServiceRecordRequest request) =>
        [.. validator.Validate(request).Errors.Select(error => error.PropertyName).Distinct()];

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public void MinimalRequest_IsValid() => Assert.True(IsValid(Valid(shop: null)));

    [Theory]
    [InlineData(null, true)]
    [InlineData("0", true)]
    [InlineData("8.25", true)]
    [InlineData("9999999.99", true)]
    [InlineData("-0.01", false)]
    [InlineData("10000000", false)]
    [InlineData("8.255", false)]
    public void Tax_IsOptionalWithARangeAndTwoDecimals(string? tax, bool valid) =>
        Assert.Equal(
            valid,
            IsValid(new ServiceRecordRequest
            {
                DateServiced = new DateOnly(2026, 5, 1),
                OdometerReading = 45000,
                Tax = tax is null ? null : decimal.Parse(tax, System.Globalization.CultureInfo.InvariantCulture),
            }));

    [Fact]
    public void ATaxOutOfRange_IsAnErrorOnTax_WithTheRangeInTheMessage()
    {
        var request = new ServiceRecordRequest { DateServiced = new DateOnly(2026, 5, 1), OdometerReading = 45000, Tax = -1m };

        var errors = validator.Validate(request).Errors;

        Assert.Equal(["Tax"], errors.Select(error => error.PropertyName).Distinct());
        Assert.Equal("Tax must be between 0.00 and 9,999,999.99.", errors.Single().ErrorMessage);
    }

    [Fact]
    public void DateServiced_IsRequired() =>
        Assert.Equal(["DateServiced"], ErrorFields(Valid(date: null, useDefaultDate: false)));

    [Theory]
    [InlineData(1899, 12, 31, false)]
    [InlineData(1900, 1, 1, true)]
    [InlineData(2026, 11, 2, true)]
    [InlineData(2026, 11, 3, false)]
    public void DateServiced_MustBeBetween1900AndToday(int year, int month, int day, bool valid) =>
        Assert.Equal(valid, IsValid(Valid(date: new DateOnly(year, month, day))));

    [Fact]
    public void DateServiced_InTheFuture_HasASpecificMessage()
    {
        var message = validator.Validate(Valid(date: new DateOnly(2026, 11, 3))).Errors.Single().ErrorMessage;

        Assert.Equal("Date serviced cannot be in the future.", message);
    }

    [Fact]
    public void OdometerReading_IsRequired() => Assert.Equal(["OdometerReading"], ErrorFields(Valid(odometer: null)));

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(9_999_999, true)]
    [InlineData(10_000_000, false)]
    public void OdometerReading_MustBeBetween0And9999999(int value, bool valid) =>
        Assert.Equal(valid, IsValid(Valid(odometer: value)));

    [Theory]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void ShopOrPerson_MaxLength100AfterTrimming(int length, bool valid) =>
        Assert.Equal(valid, IsValid(Valid(shop: new string('a', length))));

    [Fact]
    public void ShopOrPerson_IsTrimmedBeforeTheLengthCheck() =>
        Assert.True(IsValid(Valid(shop: "  " + new string('a', 100) + "  ")));

    [Theory]
    [InlineData("   ")]
    [InlineData("")]
    public void ShopOrPerson_BlankMeansNone(string shop) => Assert.True(IsValid(Valid(shop: shop)));

    [Fact]
    public void ShopOrPerson_RejectsControlCharacters() =>
        Assert.Equal(["ShopOrPerson"], ErrorFields(Valid(shop: "bad\u0007name")));
}
