using MyGarage.Api.Common;

namespace MyGarage.Api.Tests.Common;

public class PagingQueryValidatorTests
{
    private readonly PagingQueryValidator validator = new();

    [Theory]
    [InlineData(1, null)]
    [InlineData(3, 1)]
    [InlineData(1, 50)]
    public void Valid(int page, int? pageSize) =>
        Assert.True(validator.Validate(new PagingQuery(page, pageSize)).IsValid);

    [Theory]
    [InlineData(0, null)]
    [InlineData(-1, null)]
    [InlineData(1, 0)]
    [InlineData(1, 51)]
    public void Invalid(int page, int? pageSize) =>
        Assert.False(validator.Validate(new PagingQuery(page, pageSize)).IsValid);
}
