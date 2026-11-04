using Xunit;

namespace MyGarage.Web.Tests.Common;

public class RehearsalFailingTest
{
    [Fact]
    public void Rehearsal_fails_on_purpose() => Assert.Equal(1, 2);
}

