using System;
using VepMod.Enemies.Whispral;
using Xunit;

namespace Haunting.Tests;

public class PresenceTests
{
    private const float Frame = 1f / 60f;

    [Fact]
    public void ASpawnTimerRefilledWheneverPresenceAnswersNeverRunsOut()
    {
        var timer = 15f;
        for (var elapsed = 0f; elapsed < 3600f; elapsed += Frame)
        {
            if (Presence.RefilledSpawnTimer(timer) is { } refilled) timer = refilled;
            Assert.True(timer > 0f);
            timer -= Frame;
        }
    }

    [Fact]
    public void ASpawnTimerAtZeroIsLeftAlone()
    {
        Assert.Null(Presence.RefilledSpawnTimer(0f));
    }

    [Fact]
    public void ASpawnTimerFarFromRunningOutIsLeftAlone()
    {
        Assert.Null(Presence.RefilledSpawnTimer(15f));
    }

    [Fact]
    public void AReturnDelayIsDrawnIn120To180Seconds()
    {
        var random = new Random(1);
        var min = float.MaxValue;
        var max = float.MinValue;
        for (var i = 0; i < 10000; i++)
        {
            var delay = Presence.ReturnDelay(random.NextSingle());
            Assert.InRange(delay, 120f, 180f);
            min = Math.Min(min, delay);
            max = Math.Max(max, delay);
        }

        Assert.InRange(min, 120f, 121f);
        Assert.InRange(max, 179f, 180f);
        Assert.Equal(120f, Presence.ReturnDelay(0f));
        Assert.Equal(180f, Presence.ReturnDelay(1f));
    }

    [Fact]
    public void TheRoamPointIsTheOneNearTheTeamWhenItExists()
    {
        Assert.Equal(("team", Presence.RoamPointSource.NearTheTeam),
            Presence.RoamPoint("team", "itself", "anywhere"));
    }

    [Fact]
    public void WithoutAPointNearTheTeamTheRoamPointIsTheOneAroundItself()
    {
        Assert.Equal(("itself", Presence.RoamPointSource.AroundItself),
            Presence.RoamPoint(null, "itself", "anywhere"));
    }

    [Fact]
    public void WithOnlyAPointAnywhereTheRoamPointIsThatOne()
    {
        Assert.Equal(("anywhere", Presence.RoamPointSource.Anywhere),
            Presence.RoamPoint(null, null, "anywhere"));
    }

    [Fact]
    public void NoRoamPointWhenNoneExists()
    {
        Assert.Null(Presence.RoamPoint<string>(null, null, null));
    }
}
