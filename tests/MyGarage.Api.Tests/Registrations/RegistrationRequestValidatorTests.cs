using System.Globalization;
using MyGarage.Api.Registrations;
using MyGarage.Shared.Registrations;

namespace MyGarage.Api.Tests.Registrations;

public class RegistrationRequestValidatorTests
{
    private readonly RegistrationRequestValidator validator = new();

    private static decimal D(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    private static RegistrationRequest Valid(string? state = "WA", decimal? cost = 78.75m, DateOnly? expiration = null) =>
        new() { State = state, Cost = cost, ExpirationDate = expiration ?? new DateOnly(2027, 5, 1) };

    private bool IsValid(RegistrationRequest request) => validator.Validate(request).IsValid;

    private string[] ErrorFields(RegistrationRequest request) =>
        [.. validator.Validate(request).Errors.Select(error => error.PropertyName).Distinct()];

    private string[] Messages(RegistrationRequest request) =>
        [.. validator.Validate(request).Errors.Select(error => error.ErrorMessage)];

    [Fact]
    public void FullRequest_IsValid() => Assert.True(IsValid(Valid()));

    [Fact]
    public void MissingValues_AreReportedOnEachRequiredField()
    {
        var request = new RegistrationRequest();

        Assert.Equal(["State", "Cost", "ExpirationDate"], ErrorFields(request));
        Assert.Equal(["State is required.", "Cost is required.", "Expiration date is required."], Messages(request));
    }

    [Theory]
    [InlineData("WA", true)]
    [InlineData("DC", true)]
    [InlineData("wa", false)]
    [InlineData("ZZ", false)]
    [InlineData("", false)]
    public void State_MustBeAUsStateCodeOrDc(string state, bool valid) => Assert.Equal(valid, IsValid(Valid(state: state)));

    [Fact]
    public void State_MessagesNameTheProblem()
    {
        Assert.Equal(["Choose one of the 50 states or DC."], Messages(Valid(state: "ZZ")));
        Assert.Equal(["State is required."], Messages(Valid(state: "")));
        Assert.Equal(["State"], ErrorFields(Valid(state: "wa")));
    }

    [Theory]
    [InlineData("-0.01", false)]
    [InlineData("0", true)]
    [InlineData("9999999.99", true)]
    [InlineData("10000000", false)]
    [InlineData("1.005", false)]
    public void Cost_RangeAndDecimals(string cost, bool valid) =>
        Assert.Equal(valid, IsValid(Valid(cost: D(cost))));

    [Fact]
    public void Cost_MessagesNameTheProblem()
    {
        Assert.Equal(["Cost must be between 0.00 and 9,999,999.99."], Messages(Valid(cost: -0.01m)));
        Assert.Equal(["Cost can have at most 2 decimal places."], Messages(Valid(cost: 1.005m)));
    }

    [Theory]
    [InlineData(1899, 12, 31, false)]
    [InlineData(1900, 1, 1, true)]
    [InlineData(2020, 6, 15, true)]
    [InlineData(2099, 6, 15, true)]
    [InlineData(2100, 12, 31, true)]
    [InlineData(2101, 1, 1, false)]
    public void ExpirationDate_MustBeBetween1900And2100(int year, int month, int day, bool valid)
    {
        var request = Valid(expiration: new DateOnly(year, month, day));

        Assert.Equal(valid, IsValid(request));
        if (!valid)
        {
            Assert.Equal(["ExpirationDate"], ErrorFields(request));
            Assert.Equal(["Expiration date must be between 1900-01-01 and 2100-12-31."], Messages(request));
        }
    }
}
