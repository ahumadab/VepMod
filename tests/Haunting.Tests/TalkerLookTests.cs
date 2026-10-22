using System;
using VepMod.Enemies.Whispral;
using Xunit;

namespace Haunting.Tests;

public class TalkerLookTests
{
    private const float FrameTime = 1f / 60f;

    private static Func<float> Seeded(int seed)
    {
        var random = new Random(seed);
        return () => random.NextSingle();
    }

    // Seconds the look holds once the talker stops.
    private static float HoldAfterSilence(TalkerLook<string> look)
    {
        look.Hear("Victim", 2f);
        look.Update(FrameTime);
        var held = 0f;
        while (look.Talker != null)
        {
            look.Update(FrameTime);
            held += FrameTime;
        }

        return held;
    }

    [Theory]
    [InlineData(5.9f, "Victim")]
    [InlineData(6f, null)]
    [InlineData(20f, null)]
    public void OnlyATalkerWithinSixMetresIsLookedAt(float distance, string? talker)
    {
        var look = new TalkerLook<string>(() => 0.5f);
        look.Hear("Victim", distance);
        look.Update(FrameTime);

        Assert.Equal(talker, look.Talker);
    }

    [Fact]
    public void TheNearestTalkerIsLookedAt()
    {
        var look = new TalkerLook<string>(() => 0.5f);
        look.Hear("Victim", 4f);
        look.Hear("Hallucination_Bob", 2f);
        look.Hear("Hallucination_Eve", 3f);
        look.Update(FrameTime);

        Assert.Equal("Hallucination_Bob", look.Talker);
    }

    [Fact]
    public void TheLookHoldsTwoToFourSecondsAfterTheTalkerStops()
    {
        var look = new TalkerLook<string>(Seeded(1));
        var shortest = float.MaxValue;
        var longest = 0f;
        for (var i = 0; i < 1000; i++)
        {
            var held = HoldAfterSilence(look);
            shortest = Math.Min(shortest, held);
            longest = Math.Max(longest, held);
        }

        Assert.InRange(shortest, 2f - FrameTime, 2.1f);
        Assert.InRange(longest, 3.9f, 4f + FrameTime);
    }

    [Fact]
    public void TalkingAgainRestartsTheHold()
    {
        var look = new TalkerLook<string>(() => 0f);
        look.Hear("Victim", 2f);
        look.Update(FrameTime);
        look.Update(1.5f);
        look.Hear("Victim", 2f);
        look.Update(FrameTime);
        look.Update(1.5f);

        Assert.Equal("Victim", look.Talker);
        Assert.Equal(1.5f, look.Silence, 3);
    }

    [Fact]
    public void ANearerTalkerTakesTheLookFromOneStillHeld()
    {
        var look = new TalkerLook<string>(() => 0.5f);
        look.Hear("Victim", 5f);
        look.Update(FrameTime);
        look.Update(1f);
        look.Hear("Hallucination_Bob", 5.5f);
        look.Update(FrameTime);

        Assert.Equal("Hallucination_Bob", look.Talker);
    }
}
