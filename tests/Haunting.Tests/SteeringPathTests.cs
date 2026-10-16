using System;
using VepMod.Enemies.Whispral;
using Xunit;

namespace Haunting.Tests;

public class SteeringPathTests
{
    private const float Lookahead = 1.5f;

    // A right-angle corner at (10, 0), the path going on to (10, 10).
    private static SteeringPath RightAngle()
    {
        var path = new SteeringPath(8);
        path.Add(0f, 0f);
        path.Add(10f, 0f);
        path.Add(10f, 10f);
        return path;
    }

    [Fact]
    public void OnAStraightSegmentItSteersTheLookaheadAhead()
    {
        var path = RightAngle();

        var (x, z) = path.Steer(2f, 0f, Lookahead);

        Assert.Equal(2f + Lookahead, x, 3);
        Assert.Equal(0f, z, 3);
        Assert.Null(path.RoundedCorner);
    }

    [Fact]
    public void NearACornerItSteersPastIt()
    {
        var path = RightAngle();
        path.Steer(2f, 0f, Lookahead);

        var (x, z) = path.Steer(9f, 0f, Lookahead);

        Assert.Equal(1, path.RoundedCorner);
        Assert.Equal(10f, x, 3);
        Assert.True(z > 0f);
        Assert.Equal(Lookahead, MathF.Sqrt((x - 9f) * (x - 9f) + z * z), 3);
    }

    [Fact]
    public void ItReportsACornerOnlyOnTheUpdateItRoundsIt()
    {
        var path = RightAngle();
        path.Steer(9f, 0f, Lookahead);

        path.Steer(9.5f, 0.5f, Lookahead);

        Assert.Null(path.RoundedCorner);
    }

    [Fact]
    public void ItSteersAtTheLastCornerOnceWithinReach()
    {
        var path = RightAngle();
        path.Steer(9f, 0f, Lookahead);

        var (x, z) = path.Steer(10f, 9f, Lookahead);

        Assert.Equal(10f, x, 3);
        Assert.Equal(10f, z, 3);
    }

    [Fact]
    public void FarFromItsPathItSteersAtTheNextCorner()
    {
        var path = RightAngle();

        var (x, z) = path.Steer(5f, -5f, Lookahead);

        Assert.Equal(10f, x, 3);
        Assert.Equal(0f, z, 3);
    }

    [Fact]
    public void ANewPathHasItsWholeLengthLeft()
    {
        Assert.Equal(20f, RightAngle().RemainingDistance, 3);
    }

    [Fact]
    public void TheRemainingDistanceFollowsThePath()
    {
        var path = RightAngle();

        path.Steer(2f, 0f, Lookahead);

        Assert.Equal(18f, path.RemainingDistance, 3);
    }

    [Fact]
    public void AnEmptyPathHasNothingLeft()
    {
        var path = new SteeringPath(8);

        Assert.True(path.IsEmpty);
        Assert.Equal(0f, path.RemainingDistance);
    }
}
