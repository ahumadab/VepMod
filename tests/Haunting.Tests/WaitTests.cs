using System;
using System.Collections.Generic;
using VepMod.Enemies.Whispral;
using Xunit;

namespace Haunting.Tests;

public class WaitTests
{
    private const float FrameTime = 1f / 60f;

    private static Func<float> Seeded(int seed)
    {
        var random = new Random(seed);
        return () => random.NextSingle();
    }

    // Runs a Wait to its end, and returns when each Glance started.
    private static List<(float Time, Wait.Glance Glance)> RunWait(Wait wait)
    {
        var glances = new List<(float, Wait.Glance)>();
        wait.Start();
        for (var time = FrameTime; !wait.IsOver; time += FrameTime)
        {
            if (wait.Update(FrameTime) is { } glance) glances.Add((time, glance));
        }

        return glances;
    }

    [Fact]
    public void AGlanceComesEveryOneToTwoSeconds()
    {
        var wait = new Wait(Seeded(1));
        for (var i = 0; i < 1000; i++)
        {
            var previous = 0f;
            foreach (var (time, _) in RunWait(wait))
            {
                Assert.InRange(time - previous, 1f - FrameTime, 2f + FrameTime);
                previous = time;
            }
        }
    }

    [Fact]
    public void AWaitHasOneToThreeGlances()
    {
        var wait = new Wait(Seeded(2));
        var counts = new HashSet<int>();
        for (var i = 0; i < 1000; i++) counts.Add(RunWait(wait).Count);

        Assert.Equal(new HashSet<int> { 1, 2, 3 }, counts);
    }

    [Theory]
    [InlineData(0f, -120f)]
    [InlineData(1f, 120f)]
    public void AGlanceReachesNoFurtherThan120Degrees(float roll, float head)
    {
        var wait = new Wait(() => roll);
        wait.Start();

        // The first Glance comes after the roll's interval: 1 s at 0, 2 s at 1.
        var glance = wait.Update(1f + roll);

        Assert.NotNull(glance);
        Assert.Equal(head, glance.Value.Head, 3);
    }

    [Fact]
    public void AGlanceAskedForGoesToTheBoundAwayFromTheBody()
    {
        // A 4 s Wait, without a turn round, with a Glance every second.
        var rolls = new Queue<float>(new[] { 1f, 0f, 0.9f, 0f, 0.5f, 0f, 0.5f });
        var wait = new Wait(() => rolls.Dequeue());
        wait.Start();
        wait.GlanceToBoundNext();

        var toBound = wait.Update(1f);
        wait.GlanceToBoundNext();
        var back = wait.Update(1f);

        Assert.Equal(120f, toBound!.Value.Head, 3);
        Assert.Equal(80f, toBound.Value.Body, 3);
        Assert.Equal(-120f, back!.Value.Head, 3);
        Assert.Equal(-200f, back.Value.OffBody, 3);
    }

    [Fact]
    public void GlancesStayWithin120Degrees()
    {
        var wait = new Wait(Seeded(3));
        for (var i = 0; i < 1000; i++)
        {
            foreach (var (_, glance) in RunWait(wait)) Assert.InRange(glance.Head, -120f, 120f);
        }
    }

    [Fact]
    public void TheHeadTurnsAloneUpTo40Degrees()
    {
        var rolls = new Queue<float>(new[] { 0.5f, 0.5f, 0.5f, 0.5f, 0.65f });
        var wait = new Wait(() => rolls.Dequeue());
        wait.Start();

        var glance = wait.Update(1.5f);

        Assert.NotNull(glance);
        Assert.Equal(36f, glance.Value.Head, 3);
        Assert.False(glance.Value.BodyFollows);
        Assert.Equal(0f, glance.Value.Body, 3);
    }

    [Fact]
    public void BeyondTheHeadsReachTheBodyFollows()
    {
        var wait = new Wait(Seeded(4));
        for (var i = 0; i < 1000; i++)
        {
            var body = 0f;
            foreach (var (_, glance) in RunWait(wait))
            {
                var offBody = glance.Head - body;
                Assert.Equal(Math.Abs(offBody) > 40f, glance.BodyFollows);
                body = glance.BodyFollows ? glance.Head - Math.Sign(offBody) * 40f : body;
                Assert.Equal(body, glance.Body, 3);
                Assert.InRange(Math.Abs(glance.Head - glance.Body), 0f, 40f + 1e-3f);
            }
        }
    }

    [Fact]
    public void OneWaitInFourStartsWithATurnRound()
    {
        var wait = new Wait(Seeded(5));
        var turnRounds = 0;
        const int waits = 10000;
        for (var i = 0; i < waits; i++)
        {
            if (wait.Start()) turnRounds++;
        }

        Assert.InRange(turnRounds / (float)waits, 0.23f, 0.27f);
    }

    [Fact]
    public void ATurnRoundAskedForStartsTheNextWaitOnly()
    {
        var wait = new Wait(() => 0.9f);
        wait.TurnRoundNextWait();

        Assert.True(wait.Start());
        Assert.False(wait.Start());
    }
}
