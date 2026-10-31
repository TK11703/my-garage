using MyGarage.Api.SampleData;

namespace MyGarage.Api.Tests.SampleData;

public class SampleDataRequestValidatorTests
{
    private readonly SampleDataRequestValidator validator = new();

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(10)]
    public void Count_InRange_IsValid(int count)
    {
        Assert.True(validator.Validate(new SampleDataRequest(count)).IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(11)]
    public void Count_OutOfRange_IsInvalid(int count)
    {
        var result = validator.Validate(new SampleDataRequest(count));

        Assert.False(result.IsValid);
        Assert.Equal(nameof(SampleDataRequest.Count), Assert.Single(result.Errors).PropertyName);
    }
}
