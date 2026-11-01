namespace MyGarage.Web.Components.Layout;

public sealed record Crumb
{
    public Crumb(string label, string? href = null)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException("A crumb needs a label.", nameof(label));
        }

        // Only app-relative paths: a crumb must never send the user to another site.
        if (href is not null && (!href.StartsWith('/') || href.StartsWith("//", StringComparison.Ordinal) || href.StartsWith("/\\", StringComparison.Ordinal)))
        {
            throw new ArgumentException("A crumb link must be an app-relative path.", nameof(href));
        }

        Label = label;
        Href = href;
    }

    public string Label { get; }

    public string? Href { get; }
}
