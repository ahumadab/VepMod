using VepMod.Enemies.Whispral;
using Xunit;

namespace Haunting.Tests;

public class AvoidanceTests
{
    // Heading along +x.
    private const float DirX = 1f;
    private const float DirZ = 0f;

    [Fact]
    public void HeadOnItStepsToItsRight()
    {
        var (x, z) = Avoidance.Deflection(DirX, DirZ, 1f, 0f);

        Assert.Equal(0f, x, 3);
        Assert.True(z < 0f);
    }

    [Fact]
    public void ItStepsAwayFromAHallucinationAheadOnOneSide()
    {
        var (_, z) = Avoidance.Deflection(DirX, DirZ, 1f, -0.5f);

        Assert.True(z > 0f);
    }

    [Fact]
    public void TheCloserTheHarderItSteps()
    {
        var (_, near) = Avoidance.Deflection(DirX, DirZ, 0.6f, 0.3f);
        var (_, far) = Avoidance.Deflection(DirX, DirZ, 1.2f, 0.3f);

        Assert.True(near < far);
    }

    [Theory]
    [InlineData(-1f, 0.2f)] // behind
    [InlineData(3f, 0f)] // out of reach
    public void ItIgnoresAHallucinationBehindOrOutOfReach(float toOtherX, float toOtherZ)
    {
        var (x, z) = Avoidance.Deflection(DirX, DirZ, toOtherX, toOtherZ);

        Assert.Equal(0f, x);
        Assert.Equal(0f, z);
    }
}
