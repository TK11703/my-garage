using FluentValidation;

namespace MyGarage.Api.Common;

public static class TextRules
{
    // Length is measured after trimming because stored values are trimmed.
    public static IRuleBuilder<T, string?> TrimmedText<T>(
        this IRuleBuilder<T, string?> rule,
        string label,
        int maxLength,
        bool required,
        bool allowLineBreaks = false)
    {
        if (required)
        {
            rule = rule.Must(value => !string.IsNullOrWhiteSpace(value)).WithMessage($"{label} is required.");
        }

        return rule
            .Must(value => value is null || value.Trim().Length <= maxLength)
            .WithMessage($"{label} must be at most {maxLength} characters.")
            .Must(value => value is null || !ContainsControlCharacters(value, allowLineBreaks))
            .WithMessage($"{label} must not contain control characters.");
    }

    private static bool ContainsControlCharacters(string value, bool allowLineBreaks) =>
        value.Any(character => char.IsControl(character)
                               && !(allowLineBreaks && character is '\n' or '\r' or '\t'));
}
