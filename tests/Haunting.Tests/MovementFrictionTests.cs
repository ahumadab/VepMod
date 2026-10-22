using VepMod.Enemies.Whispral;
using Xunit;

namespace Haunting.Tests;

public class MovementFrictionTests
{
    private const float SprintSpeed = 5f;

    // Runs until an ease settles, or 2 s.
    private static MovementFriction.Ease? EaseTo(MovementFriction friction, float target, float frameRate)
    {
        for (var elapsed = 0f; elapsed < 2f; elapsed += 1f / frameRate)
        {
            if (friction.Update(target, 1f / frameRate) is { } ease) return ease;
        }

        return null;
    }

    [Theory]
    [InlineData(30f)]
    [InlineData(60f)]
    [InlineData(144f)]
    public void AStartFromRestTakesAPlayersTime(float frameRate)
    {
        var ease = EaseTo(new MovementFriction(), SprintSpeed, frameRate);

        Assert.NotNull(ease);
        Assert.Equal(0f, ease.Value.From);
        Assert.Equal(SprintSpeed, ease.Value.To);
        Assert.InRange(ease.Value.Duration, 0.3f, 0.5f);
    }

    [Theory]
    [InlineData(30f)]
    [InlineData(60f)]
    [InlineData(144f)]
    public void AStopFromFullSpeedTakesAPlayersTime(float frameRate)
    {
        var friction = new MovementFriction();
        EaseTo(friction, SprintSpeed, frameRate);

        var ease = EaseTo(friction, 0f, frameRate);

        Assert.NotNull(ease);
        Assert.InRange(ease.Value.From, SprintSpeed * 0.95f, SprintSpeed);
        Assert.InRange(ease.Value.Duration, 0.3f, 0.5f);
    }

    [Fact]
    public void ItKeepsMovingWhileItSlowsDown()
    {
        var friction = new MovementFriction();
        EaseTo(friction, SprintSpeed, 60f);

        friction.Update(0f, 1f / 60f);

        Assert.True(friction.IsMoving);
        Assert.True(friction.Speed > 1f);
    }

    [Fact]
    public void ItStopsWithinItsStoppingDistance()
    {
        var friction = new MovementFriction();
        EaseTo(friction, SprintSpeed, 60f);
        var stoppingDistance = friction.StoppingDistance;

        var covered = 0f;
        for (var frame = 0; frame < 120; frame++)
        {
            friction.Update(0f, 1f / 60f);
            covered += friction.Speed / 60f;
        }

        Assert.False(friction.IsMoving);
        Assert.InRange(covered, stoppingDistance * 0.75f, stoppingDistance);
    }

    [Fact]
    public void AStopEndsAtRest()
    {
        var friction = new MovementFriction();
        EaseTo(friction, SprintSpeed, 60f);

        for (var frame = 0; frame < 60; frame++) friction.Update(0f, 1f / 60f);

        Assert.Equal(0f, friction.Speed);
    }

    [Fact]
    public void AnEaseInterruptedByAnotherReportsOnlyTheLastOne()
    {
        var friction = new MovementFriction();
        friction.Update(SprintSpeed, 0.1f);
        var interruptedAt = friction.Speed;

        var ease = EaseTo(friction, 0f, 60f);

        Assert.NotNull(ease);
        Assert.Equal(interruptedAt, ease.Value.From);
        Assert.Equal(0f, ease.Value.To);
    }
}
