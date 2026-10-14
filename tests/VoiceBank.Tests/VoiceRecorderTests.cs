using System;
using System.Linq;
using VepMod.Enemies.Whispral;
using Xunit;

namespace VoiceBank.Tests;

public sealed class VoiceRecorderTests
{
    private const int Rate = 1000;
    private const int FrameLength = 100; // 0.1 s at Rate

    private readonly VoiceRecorder _recorder = new(_ => { });

    [Fact]
    public void NothingIsCapturedWithoutARequest()
    {
        PushFrames(true, 10);
        PushFrames(false, 10);

        Assert.False(_recorder.TryTakeCapture(out _));
    }

    [Fact]
    public void TheCaptureStartsOnTheNextTalkingFrame()
    {
        _recorder.RequestCapture();
        PushFrames(false, 3, 0.25f);
        PushFrames(true, 1, 0.5f);
        PushFrames(false, 5, 0.25f);

        Assert.True(_recorder.TryTakeCapture(out var capture));
        Assert.Equal(0.5f, capture.Sample.Samples[0]);
    }

    [Fact]
    public void HalfASecondOfSilenceEndsTheCaptureEarly()
    {
        _recorder.RequestCapture();
        PushFrames(true, 10);
        PushFrames(false, 4);

        Assert.False(_recorder.TryTakeCapture(out _));

        PushFrames(false, 1);

        Assert.True(_recorder.TryTakeCapture(out var capture));
        Assert.Equal(1.5f, capture.DurationSeconds);
        Assert.Equal(500, capture.TrailingSilenceLength);
    }

    [Fact]
    public void TheCaptureStopsAtSixSeconds()
    {
        _recorder.RequestCapture();
        PushFrames(true, 65);

        Assert.True(_recorder.TryTakeCapture(out var capture));
        Assert.Equal(6f, capture.DurationSeconds);
        Assert.False(_recorder.TryTakeCapture(out _));
    }

    [Fact]
    public void OneRequestGivesOneCapture()
    {
        _recorder.RequestCapture();
        PushFrames(true, 2);
        PushFrames(false, 5);
        PushFrames(true, 2);
        PushFrames(false, 5);

        Assert.True(_recorder.TryTakeCapture(out _));
        Assert.False(_recorder.TryTakeCapture(out _));
    }

    [Fact]
    public void ClearDropsThePendingRequestAndTheQueuedCaptures()
    {
        _recorder.RequestCapture();
        PushFrames(true, 1);
        PushFrames(false, 5);
        _recorder.RequestCapture();
        _recorder.Clear();
        PushFrames(true, 1);
        PushFrames(false, 5);

        Assert.False(_recorder.TryTakeCapture(out _));
    }

    [Fact]
    public void StereoIsAveragedToMono()
    {
        _recorder.RequestCapture();
        var stereo = new short[FrameLength * 2];
        for (var i = 0; i < stereo.Length; i += 2)
        {
            stereo[i] = 8192;
            stereo[i + 1] = 24576;
        }

        _recorder.PushFrame(true, stereo, Rate, 2);
        for (var i = 0; i < 5; i++) _recorder.PushFrame(false, new short[FrameLength * 2], Rate, 2);

        Assert.True(_recorder.TryTakeCapture(out var capture));
        Assert.Equal(0.5f, capture.Sample.Samples[0]);
        Assert.Equal(0.6f, capture.DurationSeconds);
    }

    [Fact]
    public void TheSampleRateComesFromTheFrames()
    {
        _recorder.RequestCapture();
        _recorder.PushFrame(true, new short[441], 44100, 1);
        for (var i = 0; i < 50; i++) _recorder.PushFrame(false, new short[441], 44100, 1);

        Assert.True(_recorder.TryTakeCapture(out var capture));
        Assert.Equal(44100, capture.Sample.SampleRate);
        Assert.Equal(22050, capture.TrailingSilenceLength);
    }

    [Fact]
    public void ACaptureTooShortIsRejectedBeforeTheVad()
    {
        var checkedSpeech = false;
        var (accepted, _) = CaptureValidation.Validate(Capture(200, 0), 0.3f, (_, _) =>
        {
            checkedSpeech = true;
            return (true, "");
        });

        Assert.False(accepted);
        Assert.False(checkedSpeech);
    }

    [Fact]
    public void TheVadAnalysesTheSpeechWithoutTheTrailingSilence()
    {
        float[]? analysed = null;
        var (accepted, _) = CaptureValidation.Validate(Capture(1500, 500), 0.3f, (speech, _) =>
        {
            analysed = speech;
            return (false, "no speech");
        });

        Assert.False(accepted);
        Assert.Equal(1000, analysed!.Length);
    }

    [Fact]
    public void AThrowingVadAcceptsTheCapture()
    {
        var (accepted, _) = CaptureValidation.Validate(Capture(1500, 500), 0.3f,
            (_, _) => throw new DllNotFoundException("WebRtcVad"));

        Assert.True(accepted);
    }

    [Fact]
    public void WithoutAVadALongEnoughCaptureIsAccepted()
    {
        var (accepted, _) = CaptureValidation.Validate(Capture(1500, 500), 0.3f, null);

        Assert.True(accepted);
    }

    private void PushFrames(bool isTalking, int count, float level = 0f)
    {
        var frame = Enumerable.Repeat((short)(level * 32768f), FrameLength).ToArray();
        for (var i = 0; i < count; i++) _recorder.PushFrame(isTalking, frame, Rate, 1);
    }

    private static Capture Capture(int length, int trailingSilenceLength)
    {
        return new Capture(new VoiceSample(new float[length], Rate), trailingSilenceLength);
    }
}
