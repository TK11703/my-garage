using Azure.Core;
using Azure.Identity;

namespace MyGarage.Api.Common;

public static class AzureCredentials
{
    // Locally there is no managed identity, and probing for one retries for about 40 s before the developer's sign-in is tried.
    public static TokenCredential Create(IHostEnvironment environment) =>
        new DefaultAzureCredential(new DefaultAzureCredentialOptions { ExcludeManagedIdentityCredential = environment.IsDevelopment() });
}
