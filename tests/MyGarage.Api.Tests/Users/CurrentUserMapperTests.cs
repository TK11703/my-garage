using System.Security.Claims;
using MyGarage.Api.Users;

namespace MyGarage.Api.Tests.Users;

public class CurrentUserMapperTests
{
    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "test"));

    [Fact]
    public void Map_PrefersObjectIdOverSubject()
    {
        var user = CurrentUserMapper.Map(Principal(("oid", "oid-1"), ("sub", "sub-1"), ("name", "Alice")));

        Assert.Equal("oid-1", user!.UserId);
    }

    [Fact]
    public void Map_FallsBackToSubject()
    {
        var user = CurrentUserMapper.Map(Principal(("sub", "sub-1"), ("name", "Alice")));

        Assert.Equal("sub-1", user!.UserId);
    }

    [Fact]
    public void Map_WithoutAnyIdentifier_ReturnsNull()
    {
        Assert.Null(CurrentUserMapper.Map(Principal(("name", "Alice"))));
    }

    [Fact]
    public void Map_DisplayNameFallsBackToPreferredUsernameThenUnknown()
    {
        Assert.Equal("Alice", CurrentUserMapper.Map(Principal(("oid", "1"), ("name", "Alice"), ("preferred_username", "a@x.test")))!.DisplayName);
        Assert.Equal("a@x.test", CurrentUserMapper.Map(Principal(("oid", "1"), ("preferred_username", "a@x.test")))!.DisplayName);
        Assert.Equal("Unknown", CurrentUserMapper.Map(Principal(("oid", "1")))!.DisplayName);
    }

    [Fact]
    public void Map_EmailIsOptional()
    {
        Assert.Equal("alice@x.test", CurrentUserMapper.Map(Principal(("oid", "1"), ("email", "alice@x.test")))!.Email);
        Assert.Equal("a@x.test", CurrentUserMapper.Map(Principal(("oid", "1"), ("preferred_username", "a@x.test")))!.Email);
        Assert.Null(CurrentUserMapper.Map(Principal(("oid", "1")))!.Email);
    }
}
