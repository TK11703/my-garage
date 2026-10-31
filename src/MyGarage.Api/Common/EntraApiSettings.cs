namespace MyGarage.Api.Common;

public sealed class EntraApiSettings
{
    public const string SectionName = "AzureAd";

    public string TenantId { get; init; } = string.Empty;

    public ApiSection Api { get; init; } = new();

    public sealed class ApiSection
    {
        public string ClientId { get; init; } = string.Empty;

        public string Scope { get; init; } = string.Empty;
    }

    public static EntraApiSettings Load(IConfiguration configuration)
    {
        var settings = configuration.GetSection(SectionName).Get<EntraApiSettings>() ?? new EntraApiSettings();

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(settings.TenantId)) missing.Add($"{SectionName}:TenantId");
        if (string.IsNullOrWhiteSpace(settings.Api.ClientId)) missing.Add($"{SectionName}:Api:ClientId");
        if (string.IsNullOrWhiteSpace(settings.Api.Scope)) missing.Add($"{SectionName}:Api:Scope");

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Missing required Entra configuration: {string.Join(", ", missing)}. Run infra/entra/Initialize-Entra.ps1 and start via the AppHost.");
        }

        return settings;
    }
}
