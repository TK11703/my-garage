using System.Globalization;
using MyGarage.Api.Taxes;
using MyGarage.Shared.Taxes;

namespace MyGarage.Api.Tests.Taxes;

public class TaxRequestValidatorTests
{
    private readonly TaxRequestValidator validator = new();

    private static decimal D(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    private static TaxRequest Valid(
        TaxType? type = TaxType.PersonalProperty,
        decimal? amount = 312.40m,
        TaxRecurrence? recurrence = TaxRecurrence.Annual) =>
        new() { Type = type, Amount = amount, Recurrence = recurrence };

    private bool IsValid(TaxRequest request) => validator.Validate(request).IsValid;

    private string[] ErrorFields(TaxRequest request) =>
        [.. validator.Validate(request).Errors.Select(error => error.PropertyName).Distinct()];

    private string[] Messages(TaxRequest request) =>
        [.. validator.Validate(request).Errors.Select(error => error.ErrorMessage)];

    [Fact]
    public void FullRequest_IsValid() => Assert.True(IsValid(Valid()));

    [Fact]
    public void MissingValues_AreReportedOnEachRequiredField()
    {
        var request = new TaxRequest();

        Assert.Equal(["Type", "Amount", "Recurrence"], ErrorFields(request));
        Assert.Equal(["Choose a tax type.", "Amount is required.", "Choose how often the tax recurs."], Messages(request));
    }

    [Theory]
    [InlineData(TaxType.PersonalProperty)]
    [InlineData(TaxType.Lease)]
    [InlineData(TaxType.Purchase)]
    public void Type_AcceptsEachDefinedValue(TaxType type) => Assert.True(IsValid(Valid(type: type)));

    [Fact]
    public void Type_MustBeGivenAndDefined()
    {
        Assert.Equal(["Type"], ErrorFields(Valid(type: null)));
        Assert.Equal(["Choose a tax type."], Messages(Valid(type: (TaxType)99)));
        Assert.Equal(["Type"], ErrorFields(Valid(type: (TaxType)99)));
    }

    [Theory]
    [InlineData(TaxRecurrence.Once)]
    [InlineData(TaxRecurrence.Monthly)]
    [InlineData(TaxRecurrence.BiAnnual)]
    [InlineData(TaxRecurrence.Annual)]
    public void Recurrence_AcceptsEachDefinedValue(TaxRecurrence recurrence) => Assert.True(IsValid(Valid(recurrence: recurrence)));

    [Fact]
    public void Recurrence_MustBeGivenAndDefined()
    {
        Assert.Equal(["Recurrence"], ErrorFields(Valid(recurrence: null)));
        Assert.Equal(["Choose how often the tax recurs."], Messages(Valid(recurrence: (TaxRecurrence)99)));
        Assert.Equal(["Recurrence"], ErrorFields(Valid(recurrence: (TaxRecurrence)99)));
    }

    [Theory]
    [InlineData("0", false)]
    [InlineData("0.01", true)]
    [InlineData("9999999.99", true)]
    [InlineData("10000000", false)]
    [InlineData("1.005", false)]
    public void Amount_RangeAndDecimals(string amount, bool valid)
    {
        var request = Valid(amount: D(amount));

        Assert.Equal(valid, IsValid(request));
        if (!valid)
        {
            Assert.Equal(["Amount"], ErrorFields(request));
        }
    }

    [Fact]
    public void Amount_MessagesNameTheProblem()
    {
        Assert.Equal(["Amount must be between 0.01 and 9,999,999.99."], Messages(Valid(amount: 0m)));
        Assert.Equal(["Amount must be between 0.01 and 9,999,999.99."], Messages(Valid(amount: 10_000_000m)));
        Assert.Equal(["Amount can have at most 2 decimal places."], Messages(Valid(amount: 1.005m)));
    }
}
