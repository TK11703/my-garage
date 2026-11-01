using FluentValidation;
using MyGarage.Api.Common;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Vehicles.JournalNotes;

public sealed class JournalNoteRequestValidator : AbstractValidator<JournalNoteRequest>
{
    public JournalNoteRequestValidator()
    {
        RuleFor(request => request.Body).TrimmedText("Note", 4000, required: true, allowLineBreaks: true);
        RuleFor(request => request.Category).TrimmedText("Category", 40, required: false);
    }
}
