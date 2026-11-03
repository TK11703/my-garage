using Azure.Core;
using Npgsql;

namespace MyGarage.Api.Common;

// In Azure the API signs in to PostgreSQL with a Microsoft Entra access token instead of a password.
public static class EntraDatabaseAuthentication
{
    public const string SettingName = "Database:UseEntraAuthentication";

    private const string TokenScope = "https://ossrdbms-aad.database.windows.net/.default";

    // Tokens last at least an hour, so refreshing every 55 minutes keeps new connections valid.
    private static readonly TimeSpan SuccessRefreshInterval = TimeSpan.FromMinutes(55);
    private static readonly TimeSpan FailureRefreshInterval = TimeSpan.FromSeconds(5);

    // Returns whether the token provider was registered.
    public static bool Configure(NpgsqlDataSourceBuilder builder, IConfiguration configuration, TokenCredential credential)
    {
        if (!configuration.GetValue<bool>(SettingName))
        {
            return false;
        }

        builder.UsePeriodicPasswordProvider(CreatePasswordProvider(credential), SuccessRefreshInterval, FailureRefreshInterval);
        return true;
    }

    public static Func<NpgsqlConnectionStringBuilder, CancellationToken, ValueTask<string>> CreatePasswordProvider(TokenCredential credential) =>
        async (_, cancellationToken) =>
        {
            var token = await credential.GetTokenAsync(new TokenRequestContext([TokenScope]), cancellationToken);
            return token.Token;
        };
}
