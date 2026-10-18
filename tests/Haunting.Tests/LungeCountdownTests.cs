using System;
using System.Collections.Generic;
using VepMod.Enemies.Whispral;
using Xunit;
using Event = VepMod.Enemies.Whispral.LungeCountdown.Event;

namespace Haunting.Tests;

public class LungeCountdownTests
{
    private const float FrameTime = 1f / 60f;
    private const float Near = 3f; // m
    private const float Far = 8f; // m

    // Steps the countdown for a duration, and returns when each event came, from the first frame stepped.
    private static List<(float Time, Event Event)> Run(LungeCountdown countdown, float duration,
        float distance = Near, bool isHidden = false, bool isGazeOn = false)
    {
        var events = new List<(float, Event)>();
        var frames = (int)Math.Round(duration / FrameTime);
        for (var frame = 0; frame < frames; frame++)
        {
            if (countdown.Update(FrameTime, distance, isHidden, isGazeOn) is { } e) events.Add((frame * FrameTime, e));
        }

        return events;
    }

    private static LungeCountdown Started()
    {
        var countdown = new LungeCountdown();
        countdown.Start();
        Assert.Equal(Event.Started, countdown.Update(FrameTime, Near, false, false));
        return countdown;
    }

    private static void RunToTheLunge(LungeCountdown countdown)
    {
        while (countdown.Update(FrameTime, Near, false, false) != Event.Lunge)
        {
        }
    }

    private static void AssertAbout(float expected, float time)
    {
        Assert.InRange(time, expected - 2 * FrameTime, expected + 2 * FrameTime);
    }

    [Fact]
    public void NoEventBeforeTheStart()
    {
        var countdown = new LungeCountdown();

        Assert.Empty(Run(countdown, 10f));
        Assert.Empty(Run(countdown, 10f, isGazeOn: true));
    }

    [Fact]
    public void TheCountStartsOnTheFirstFrameInSight()
    {
        var countdown = new LungeCountdown();
        countdown.Start();

        Assert.Empty(Run(countdown, 2f, isHidden: true));
        Assert.Equal(Event.Started, countdown.Update(FrameTime, Near, false, false));
    }

    [Fact]
    public void TheLungeComesFiveSecondsAfterTheStartWithinReachAndInSight()
    {
        var events = Run(Started(), 6f);

        var (time, e) = Assert.Single(events);
        Assert.Equal(Event.Lunge, e);
        AssertAbout(5f, time);
    }

    [Fact]
    public void TheLungeWaitsForTheVictimToComeBackWithinFiveMetres()
    {
        var countdown = Started();

        var away = Run(countdown, 20f, Far);
        var back = countdown.Update(FrameTime, 4.9f, false, false);

        var (time, e) = Assert.Single(away);
        Assert.Equal(Event.Over, e);
        AssertAbout(5f, time);
        Assert.Equal(Event.Lunge, back);
    }

    [Fact]
    public void TheCountPausesWithoutProgressWhileSomethingStandsBetweenThem()
    {
        var countdown = Started();
        Run(countdown, 2f);

        var hidden = Run(countdown, 30f, isHidden: true);
        var resumed = Run(countdown, 4f);

        Assert.Equal(Event.Paused, Assert.Single(hidden).Event);
        Assert.Equal(new[] { Event.Resumed, Event.Lunge }, resumed.ConvertAll(r => r.Event));
        AssertAbout(3f, resumed[1].Time);
    }

    [Fact]
    public void EachReturnOfTheSightResumesTheCountOnce()
    {
        var countdown = Started();

        var events = new List<Event>();
        for (var i = 0; i < 3; i++)
        {
            events.AddRange(Run(countdown, 0.5f, isHidden: true).ConvertAll(r => r.Event));
            events.AddRange(Run(countdown, 0.5f).ConvertAll(r => r.Event));
        }

        Assert.Equal(new[] { Event.Paused, Event.Resumed, Event.Paused, Event.Resumed, Event.Paused, Event.Resumed },
            events);
    }

    [Fact]
    public void AGazeBeforeTheLungeEndsTheCount()
    {
        var countdown = Started();
        Run(countdown, 4f);

        var gazed = Run(countdown, 0.5f, isGazeOn: true);
        var after = Run(countdown, 20f);

        Assert.Equal(Event.Gazed, Assert.Single(gazed).Event);
        Assert.Empty(after);
    }

    [Fact]
    public void AGazeAfterTheLungeIsIgnored()
    {
        var countdown = Started();
        RunToTheLunge(countdown);

        var events = Run(countdown, 2f, isGazeOn: true);

        Assert.Equal(Event.Kill, Assert.Single(events).Event);
    }

    [Theory]
    [InlineData(Near, false)]
    [InlineData(Far, false)]
    [InlineData(Near, true)]
    [InlineData(100f, true)]
    public void TheKillComesOneSecondAfterTheLungeWhateverTheDistanceAndTheSight(float distance, bool isHidden)
    {
        var countdown = Started();
        RunToTheLunge(countdown);

        var events = Run(countdown, 5f, distance, isHidden);

        var (time, e) = Assert.Single(events);
        Assert.Equal(Event.Kill, e);
        AssertAbout(1f, time);
    }
}
