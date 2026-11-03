using Microsoft.Extensions.Configuration;
using MyGarage.Web.Common;

namespace MyGarage.Web.Tests.Common;

public class EntraWebSettingsTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAd:TenantId"] = "11111111-1111-1111-1111-111111111111",
                ["AzureAd:Web:ClientId"] = "22222222-2222-2222-2222-222222222222",
                ["AzureAd:Api:ClientId"] = "33333333-3333-3333-3333-333333333333",
                ["AzureAd:Api:Scope"] = "access_as_user",
            }.Concat(values.Select(v => KeyValuePair.Create(v.Key, v.Value))).ToDictionary())
            .Build();

    [Fact]
    public void Load_WithOnlyAManagedIdentityClientId_Succeeds()
    {
        var settings = EntraWebSettings.Load(Config(("AzureAd:Web:ManagedIdentityClientId", "44444444-4444-4444-4444-444444444444")));

        Assert.Equal("44444444-4444-4444-4444-444444444444", settings.Web.ManagedIdentityClientId);
        Assert.Equal(string.Empty, settings.Web.ClientSecret);
    }

    [Fact]
    public void Load_WithOnlyAClientSecret_Succeeds()
    {
        var settings = EntraWebSettings.Load(Config(("AzureAd:Web:ClientSecret", "secret")));

        Assert.Equal("secret", settings.Web.ClientSecret);
        Assert.Equal(string.Empty, settings.Web.ManagedIdentityClientId);
    }

    [Fact]
    public void Load_WithNeitherACredentialNorAManagedIdentity_ListsTheClientSecretAsMissing()
    {
        var error = Assert.Throws<InvalidOperationException>(() => EntraWebSettings.Load(Config()));

        Assert.Contains("AzureAd:Web:ClientSecret", error.Message);
    }
}
