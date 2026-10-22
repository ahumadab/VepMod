using System;
using System.Collections.Generic;
using System.Linq;
using VepMod.Enemies.Whispral;
using Xunit;

namespace Haunting.Tests;

public class WhisperScheduleTests
{
    private const float Frame = 1f / 60f;
    private const float SoundLength = 6f;

    [Fact]
    public void AWhisperStartsOnTheFirstFrameOfTheHauntingAndNoneBefore()
    {
        var schedule = new WhisperSchedule(() => 0.5f);
        for (var frame = 0; frame < 600; frame++)
        {
            Assert.Equal(WhisperSchedule.Step.None, schedule.Update(frame * Frame, false));
        }

        Assert.Equal(WhisperSchedule.Step.Start, schedule.Update(600 * Frame, true));
    }

    [Fact]
    public void TheNextWhisperStartsOnlyAfterTheSoundAndItsSilence()
    {
        var schedule = new WhisperSchedule(() => 0.5f);
        Assert.Equal(WhisperSchedule.Step.Start, schedule.Update(0f, true));
        schedule.Started(SoundLength);

        var silence = WhisperSchedule.MinSilence + (WhisperSchedule.MaxSilence - WhisperSchedule.MinSilence) * 0.5f;
        float? ended = null;
        for (var frame = 1; ; frame++)
        {
            var time = frame * Frame;
            var step = schedule.Update(time, true);
            if (step == WhisperSchedule.Step.End) ended = time;
            if (step != WhisperSchedule.Step.Start) continue;

            Assert.InRange(ended!.Value, SoundLength, SoundLength + Frame);
            Assert.InRange(time - ended.Value, silence, silence + Frame);
            return;
        }
    }

    [Fact]
    public void EverySilenceIsDrawnIn8To15Seconds()
    {
        var silences = Haunt(new Random(1), 2000).Silences;

        Assert.All(silences, silence => Assert.InRange(silence, 8f, 15f + 2 * Frame));
        Assert.InRange(silences.Min(), 8f, 8.1f);
        Assert.InRange(silences.Max(), 14.9f, 15f + 2 * Frame);
    }

    [Fact]
    public void NoSideTwiceInARowAndEverySideUsed()
    {
        var sides = Haunt(new Random(2), 2000).Sides;

        for (var i = 1; i < sides.Count; i++) Assert.NotEqual(sides[i - 1], sides[i]);
        Assert.Equal(Enum.GetValues<WhisperSchedule.Side>().Length, new HashSet<WhisperSchedule.Side>(sides).Count);
    }

    [Fact]
    public void AWhisperFadesInOver1SecondAndOutOverTheSoundsLast1Point5Seconds()
    {
        var schedule = new WhisperSchedule(() => 0.5f);
        schedule.Update(0f, true);
        schedule.Started(SoundLength);

        Assert.Equal(0f, schedule.Volume);
        AssertVolumeAt(schedule, 0.5f, 0.5f);
        AssertVolumeAt(schedule, 1f, 1f);
        AssertVolumeAt(schedule, 3f, 1f);
        AssertVolumeAt(schedule, SoundLength - 1.5f, 1f);
        AssertVolumeAt(schedule, SoundLength - 0.75f, 0.5f);

        Assert.Equal(WhisperSchedule.Step.End, schedule.Update(SoundLength, true));
        Assert.Equal(0f, schedule.Volume);
        Assert.Equal(WhisperSchedule.FadeOut, schedule.FadeLength);
    }

    [Fact]
    public void TheEndOfTheHauntingReleasesTheWhisperFromItsVolumeOver1SecondAndNoWhisperFollows()
    {
        var schedule = new WhisperSchedule(() => 0.5f);
        schedule.Update(0f, true);
        schedule.Started(SoundLength);
        AssertVolumeAt(schedule, 0.5f, 0.5f);

        Assert.Equal(WhisperSchedule.Step.None, schedule.Update(0.5f, false));
        Assert.Equal(0.5f, schedule.Volume, 3);
        Assert.Equal(WhisperSchedule.Step.None, schedule.Update(1f, false));
        Assert.Equal(0.25f, schedule.Volume, 3);
        Assert.Equal(WhisperSchedule.Step.Release, schedule.Update(1.5f, false));
        Assert.Equal(0f, schedule.Volume);
        Assert.Equal(WhisperSchedule.ReleaseFade, schedule.FadeLength, 3);

        for (var frame = 0; frame < 3600; frame++)
        {
            Assert.Equal(WhisperSchedule.Step.None, schedule.Update(1.5f + frame * Frame, false));
        }
    }

    [Fact]
    public void TheEndOfTheHauntingDuringASilenceStartsNoWhisper()
    {
        var schedule = new WhisperSchedule(() => 0.5f);
        schedule.Update(0f, true);
        schedule.Started(SoundLength);
        Assert.Equal(WhisperSchedule.Step.End, schedule.Update(SoundLength, true));

        for (var frame = 0; frame < 3600; frame++)
        {
            Assert.Equal(WhisperSchedule.Step.None, schedule.Update(SoundLength + frame * Frame, false));
        }
    }

    [Fact]
    public void EverySideOffsetIs30To50CentimetresFromTheHead()
    {
        foreach (var side in Enum.GetValues<WhisperSchedule.Side>())
        {
            var (x, y, z) = WhisperSchedule.Offset(side);
            Assert.InRange(MathF.Sqrt(x * x + y * y + z * z), 0.3f, 0.5f);
        }
    }

    private static void AssertVolumeAt(WhisperSchedule schedule, float time, float volume)
    {
        Assert.Equal(WhisperSchedule.Step.None, schedule.Update(time, true));
        Assert.Equal(volume, schedule.Volume, 3);
    }

    // Frame by frame through one Haunting, each Whisper sound 6 to 10 s long.
    private static (List<float> Silences, List<WhisperSchedule.Side> Sides) Haunt(Random random, int whispers)
    {
        var schedule = new WhisperSchedule(random.NextSingle);
        var silences = new List<float>();
        var sides = new List<WhisperSchedule.Side>();
        float? ended = null;
        for (var frame = 0; sides.Count < whispers; frame++)
        {
            var time = (float)(frame * (double)Frame);
            switch (schedule.Update(time, true))
            {
                case WhisperSchedule.Step.Start:
                    schedule.Started(6f + 4f * random.NextSingle());
                    sides.Add(schedule.LastSide);
                    if (ended.HasValue) silences.Add(time - ended.Value);
                    break;
                case WhisperSchedule.Step.End:
                    ended = time;
                    break;
            }
        }

        return (silences, sides);
    }
}
