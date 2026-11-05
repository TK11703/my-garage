using System.Text;

namespace MyGarage.Api.Vehicles.StickerImport;

// Two features with the same key are the same equipment worded with different case, spacing, or punctuation.
public static class FeatureKey
{
    public static string Of(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        var key = new StringBuilder(name.Length);
        foreach (var character in name)
        {
            if (char.IsLetterOrDigit(character))
            {
                key.Append(char.ToLowerInvariant(character));
            }
        }

        return key.ToString();
    }
}
