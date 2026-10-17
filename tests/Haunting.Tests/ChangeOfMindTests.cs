using System;
using VepMod.Enemies.Whispral;
using Xunit;

namespace Haunting.Tests;

public class ChangeOfMindTests
{
    private const float Frame = 1f / 60f;

    private static Func<float> Seeded(int seed)
    {
        var random = new Random(seed);
        return () => random.NextSingle();
    }

    private static ChangeOfMind.Change? Run(ChangeOfMind changeOfMind, float seconds)
    {
        for (var elapsed = 0f; elapsed < seconds; elapsed += Frame)
        {
            if (changeOfMind.Update(Frame) is { } change) return change;
        }

        return null;
    }

    [Fact]
    public void ThreeSecondsInAHundredChangeTheirMind()
    {
        var changeOfMind = new ChangeOfMind(Seeded(1));
        var changes = 0;
        const int seconds = 20000;
        for (var i = 0; i < seconds; i++)
        {
            changeOfMind.Start();
            if (changeOfMind.Update(1f) != null) changes++;
        }

        Assert.InRange(changes / (float)seconds, 0.025f, 0.035f);
    }

    [Theory]
    [InlineData(0.02f, true)]
    [InlineData(0.04f, false)]
    public void TheRollDecides(float roll, bool changes)
    {
        var changeOfMind = new ChangeOfMind(() => roll);
        changeOfMind.Start();

        var change = changeOfMind.Update(1f);

        Assert.Equal(changes, change != null);
        if (change is { } changed) Assert.Equal(roll, changed.Roll);
    }

    [Fact]
    public void ARollComesOncePerSecond()
    {
        var rolls = 0;
        var changeOfMind = new ChangeOfMind(() =>
        {
            rolls++;
            return 1f;
        });
        changeOfMind.Start();

        Run(changeOfMind, 3.5f);

        Assert.Equal(3, rolls);
    }

    [Fact]
    public void ATripChangesItsMindOnceAtMost()
    {
        var changeOfMind = new ChangeOfMind(() => 0f);
        changeOfMind.Start();

        Assert.NotNull(changeOfMind.Update(1f));
        Assert.Null(Run(changeOfMind, 60f));
    }

    [Fact]
    public void ANewTripMayChangeItsMindAgain()
    {
        var changeOfMind = new ChangeOfMind(() => 0f);
        changeOfMind.Start();
        changeOfMind.Update(1f);
        changeOfMind.Start();

        Assert.NotNull(changeOfMind.Update(1f));
    }

    [Fact]
    public void AChangeAskedForComesOnTheNextUpdate()
    {
        var changeOfMind = new ChangeOfMind(() => 1f);
        changeOfMind.Start();
        changeOfMind.ChangeNow();

        var change = changeOfMind.Update(Frame);

        Assert.NotNull(change);
        Assert.True(change.Value.Asked);
        Assert.False(change.Value.Capped);
        Assert.Null(change.Value.Roll);
    }

    [Fact]
    public void AChangeAskedForAfterTheTripChangedItsMindWaitsForTheNextTrip()
    {
        var changeOfMind = new ChangeOfMind(() => 1f);
        changeOfMind.Start();
        changeOfMind.ChangeNow();
        changeOfMind.ChangeNow();

        var first = changeOfMind.Update(Frame);
        var refused = changeOfMind.Update(Frame);
        var later = Run(changeOfMind, 60f);
        changeOfMind.Start();
        var next = changeOfMind.Update(Frame);

        Assert.False(first!.Value.Capped);
        Assert.True(refused!.Value.Capped);
        Assert.True(refused.Value.Asked);
        Assert.Null(later);
        Assert.True(next!.Value.Asked);
        Assert.False(next.Value.Capped);
    }
}
