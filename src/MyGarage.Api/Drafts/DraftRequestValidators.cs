using FluentValidation;
using MyGarage.Shared.Drafts;

namespace MyGarage.Api.Drafts;

public sealed class ChangeDraftKindRequestValidator : AbstractValidator<ChangeDraftKindRequest>
{
    public ChangeDraftKindRequestValidator() =>
        RuleFor(request => request.Kind)
            .NotNull().WithMessage("Choose a record type.")
            .IsInEnum().WithMessage("Choose a record type.");
}

// The accept request must name a vehicle and carry a proposal whose section passes the existing validator of its kind.
// Failures are keyed like the other requests (Proposal.ServiceRecord.OdometerReading), so the form can place each message.
public sealed class AcceptDraftRequestValidator : AbstractValidator<AcceptDraftRequest>
{
    public AcceptDraftRequestValidator(CompatibilityChecker checker)
    {
        RuleFor(request => request.VehicleId)
            .NotNull().WithMessage("Choose a vehicle.")
            .NotEqual(Guid.Empty).WithMessage("Choose a vehicle.");
        RuleFor(request => request.Proposal).NotNull().WithMessage("The proposal is required.");
        RuleFor(request => request).Custom((request, context) =>
        {
            if (request.Proposal is null)
            {
                return;
            }

            foreach (var failure in checker.Validate(request.Proposal))
            {
                context.AddFailure($"Proposal.{Pascal(failure.Path)}", failure.Message);
            }
        });
    }

    private static string Pascal(string path) =>
        string.Join('.', path.Split('.').Select(segment => segment.Length == 0 ? segment : char.ToUpperInvariant(segment[0]) + segment[1..]));
}
