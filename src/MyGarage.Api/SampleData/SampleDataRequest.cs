using FluentValidation;

namespace MyGarage.Api.SampleData;

public sealed record SampleDataRequest(int Count = 3);

public sealed class SampleDataRequestValidator : AbstractValidator<SampleDataRequest>
{
    public SampleDataRequestValidator()
    {
        RuleFor(request => request.Count).InclusiveBetween(1, 10);
    }
}
