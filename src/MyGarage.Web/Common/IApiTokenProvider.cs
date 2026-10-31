using Microsoft.Identity.Web;

namespace MyGarage.Web.Common;

public interface IApiTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken);
}

// Acquires an access token for the My Garage API on behalf of the signed-in user.
public sealed class MsalApiTokenProvider(ITokenAcquisition tokenAcquisition, EntraWebSettings settings) : IApiTokenProvider
{
    public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) =>
        tokenAcquisition.GetAccessTokenForUserAsync([settings.ApiScope]);
}
