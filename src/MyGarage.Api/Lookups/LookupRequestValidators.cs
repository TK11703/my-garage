using FluentValidation;
using MyGarage.Api.Common;
using MyGarage.Shared.Lookups;

namespace MyGarage.Api.Lookups;

public sealed class AddLookupItemRequestValidator : AbstractValidator<AddLookupItemRequest>
{
    public AddLookupItemRequestValidator() =>
        RuleFor(request => request.Name).TrimmedText("Name", 30, required: true);
}

public sealed class SetLookupVisibilityRequestValidator : AbstractValidator<SetLookupVisibilityRequest>
{
    public SetLookupVisibilityRequestValidator() =>
        RuleFor(request => request.IsHidden).NotNull().WithMessage("IsHidden is required.");
}
