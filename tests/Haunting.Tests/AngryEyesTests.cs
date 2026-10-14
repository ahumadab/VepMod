using VepMod.Enemies.Whispral;
using Xunit;

namespace Haunting.Tests;

public class AngryEyesTests
{
    private const float Frame = 0.1f;

    private readonly AngryEyes eyes = new();

    [Fact]
    public void AngryWithoutTheGaze()
    {
        eyes.Update(false, false, Frame);

        Assert.True(eyes.IsAngry);
    }

    [Fact]
    public void StillAngryJustAfterTheGazeLands()
    {
        eyes.Update(true, false, Frame);
        eyes.Update(true, false, Frame);

        Assert.True(eyes.IsAngry);
    }

    [Fact]
    public void CalmOnceTheGazeHolds()
    {
        for (var i = 0; i < 4; i++) eyes.Update(true, false, Frame);

        Assert.False(eyes.IsAngry);
    }

    [Fact]
    public void AngryAgainWhenTheGazeLeaves()
    {
        for (var i = 0; i < 4; i++) eyes.Update(true, false, Frame);
        eyes.Update(false, false, Frame);

        Assert.True(eyes.IsAngry);
    }

    [Fact]
    public void TheDelayStartsOverAtEachGaze()
    {
        eyes.Update(true, false, Frame);
        eyes.Update(true, false, Frame);
        eyes.Update(false, false, Frame);
        eyes.Update(true, false, Frame);
        eyes.Update(true, false, Frame);

        Assert.True(eyes.IsAngry);
    }

    [Fact]
    public void StalkingHoldsTheAngerUnderTheGaze()
    {
        for (var i = 0; i < 100; i++) eyes.Update(true, true, Frame);

        Assert.True(eyes.IsAngry);
    }

    [Fact]
    public void CalmAtTheFleeUnderAHeldGaze()
    {
        for (var i = 0; i < 20; i++) eyes.Update(true, true, Frame);
        eyes.Update(true, false, Frame);

        Assert.False(eyes.IsAngry);
    }
}
