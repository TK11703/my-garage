using MyGarage.Api.Drafts;
using MyGarage.Shared.Drafts;

namespace MyGarage.Api.Tests.Drafts;

public class DraftKindsTests
{
    public static TheoryData<VehicleMatch> Matches => [.. Enum.GetValues<VehicleMatch>()];

    [Theory]
    [MemberData(nameof(Matches))]
    public void EveryVehicleMatch_RoundTripsThroughItsStoredText(VehicleMatch match) =>
        Assert.Equal(match, DraftKinds.MatchFromText(DraftKinds.ToText(match)));

    [Fact]
    public void Selected_IsStoredAsSelected()
    {
        Assert.Equal("selected", DraftKinds.ToText(VehicleMatch.Selected));
        Assert.Equal(VehicleMatch.Selected, DraftKinds.MatchFromText("selected"));
    }
}
