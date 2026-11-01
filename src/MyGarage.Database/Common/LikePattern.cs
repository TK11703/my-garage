namespace MyGarage.Database.Common;

public static class LikePattern
{
    // Builds a literal "contains" pattern for ILIKE ... ESCAPE '\'; returns null when there is nothing to search for.
    public static string? Contains(string? text)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        var escaped = trimmed
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

        return $"%{escaped}%";
    }
}
