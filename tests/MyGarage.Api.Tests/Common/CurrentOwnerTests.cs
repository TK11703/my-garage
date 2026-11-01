using System.Security.Claims;
using MyGarage.Api.Common;

namespace MyGarage.Api.Tests.Common;

public class CurrentOwnerTests
{
    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(claim => new Claim(claim.Type, claim.Value)), "test"));

    [Fact]
    public void Get_PrefersObjectId() =>
        Assert.Equal("oid-1", CurrentOwner.Get(Principal(("oid", "oid-1"), ("sub", "sub-1"))));

    [Fact]
    public void Get_FallsBackToSubject() =>
        Assert.Equal("sub-1", CurrentOwner.Get(Principal(("sub", "sub-1"))));

    [Fact]
    public void Get_NoIdentifyingClaim_ReturnsNull() =>
        Assert.Null(CurrentOwner.Get(Principal(("name", "Alice"))));

    [Fact]
    public void Get_IgnoresOwnerLookingClaimsWithOtherNames() =>
        Assert.Null(CurrentOwner.Get(Principal(("ownerId", "x"), ("userId", "y"))));
}
