using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using MyGarage.Web.Authentication;

namespace MyGarage.Web.Tests.Authentication;

public class AuthFailureMapperTests
{
    private static OpenIdConnectProtocolException Protocol(string error, string description) =>
        new($"Message contains error: '{error}', error_description: '{description}', error_uri: 'error_uri is null'.");

    [Fact]
    public void Map_UserDeclinedConsent_IsCancelled()
    {
        var reason = AuthFailureMapper.Map(Protocol("access_denied", "AADSTS65004: The user declined to consent to access the app."));

        Assert.Equal(AuthFailureReason.Cancelled, reason);
    }

    [Fact]
    public void Map_AccessDeniedWithoutConsentDecline_IsDenied()
    {
        var reason = AuthFailureMapper.Map(Protocol("access_denied", "AADSTS50020: User account does not exist in tenant."));

        Assert.Equal(AuthFailureReason.Denied, reason);
    }

    [Fact]
    public void Map_TemporarilyUnavailable_IsUnavailable()
    {
        var reason = AuthFailureMapper.Map(Protocol("temporarily_unavailable", "The server is temporarily too busy."));

        Assert.Equal(AuthFailureReason.Unavailable, reason);
    }

    [Fact]
    public void Map_NetworkFailure_IsUnavailable()
    {
        Assert.Equal(AuthFailureReason.Unavailable, AuthFailureMapper.Map(new HttpRequestException("connection refused")));
        Assert.Equal(
            AuthFailureReason.Unavailable,
            AuthFailureMapper.Map(new InvalidOperationException("metadata failed", new HttpRequestException("timeout"))));
    }

    [Fact]
    public void Map_AnythingElse_IsUnknown()
    {
        Assert.Equal(AuthFailureReason.Unknown, AuthFailureMapper.Map(new InvalidOperationException("boom")));
        Assert.Equal(AuthFailureReason.Unknown, AuthFailureMapper.Map(null));
    }
}
