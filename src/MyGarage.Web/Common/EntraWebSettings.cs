namespace MyGarage.Web.Common;

public sealed class EntraWebSettings
{
    public const string SectionName = "AzureAd";

    public string TenantId { get; init; } = string.Empty;

    public WebSection Web { get; init; } = new();

    public ApiSection Api { get; init; } = new();

    public string ApiScope => $"api://{Api.ClientId}/{Api.Scope}";

    public sealed class WebSection
    {
        public string ClientId { get; init; } = string.Empty;

        public string ClientSecret { get; init; } = string.Empty;
    }

    public sealed class ApiSection
    {
        public string ClientId { get; init; } = string.Empty;

        public string Scope { get; init; } = string.Empty;
    }

    public static EntraWebSettings Load(IConfiguration configuration)
    {
        var settings = configuration.GetSection(SectionName).Get<EntraWebSettings>() ?? new EntraWebSettings();

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(settings.TenantId)) missing.Add($"{SectionName}:TenantId");
        if (string.IsNullOrWhiteSpace(settings.Web.ClientId)) missing.Add($"{SectionName}:Web:ClientId");
        if (string.IsNullOrWhiteSpace(settings.Web.ClientSecret)) missing.Add($"{SectionName}:Web:ClientSecret");
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
