using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MyGarage.Api.Tests.Common;

public static class TestJwt
{
    public const string TenantId = "11111111-1111-1111-1111-111111111111";
    public const string ApiClientId = "33333333-3333-3333-3333-333333333333";
    public const string Issuer = $"https://login.test/{TenantId}/v2.0";
    public const string Scope = "access_as_user";

    public static readonly SymmetricSecurityKey SigningKey = new(Encoding.UTF8.GetBytes("unit-test-signing-key-0123456789-abcdef"));

    private static readonly SymmetricSecurityKey OtherKey = new(Encoding.UTF8.GetBytes("a-completely-different-signing-key-9876543210"));

    public static string Create(
        string audience = ApiClientId,
        string issuer = Issuer,
        string? scope = Scope,
        TimeSpan? expiresIn = null,
        bool signWithOtherKey = false,
        IDictionary<string, object>? claims = null)
    {
        var now = DateTime.UtcNow;
        var lifetime = expiresIn ?? TimeSpan.FromHours(1);
        var notBefore = lifetime < TimeSpan.Zero ? now + lifetime - TimeSpan.FromHours(1) : now;

        var payload = new Dictionary<string, object>
        {
            ["oid"] = "oid-1234",
            ["sub"] = "sub-5678",
            ["name"] = "Alice Garage",
            ["preferred_username"] = "alice@contoso.test",
            ["tid"] = TenantId,
        };
        if (scope is not null)
        {
            payload["scp"] = scope;
        }

        foreach (var (key, value) in claims ?? new Dictionary<string, object>())
        {
            payload[key] = value;
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            NotBefore = notBefore,
            Expires = now + lifetime,
            Claims = payload,
            SigningCredentials = new SigningCredentials(signWithOtherKey ? OtherKey : SigningKey, SecurityAlgorithms.HmacSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
