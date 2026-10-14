using System;
using System.IO;
using VepMod.Enemies.Whispral;
using Xunit;

namespace VoiceBank.Tests;

public class WavCodecTests
{
    [Fact]
    public void RoundTripKeepsTheSamplesAndTheSampleRate()
    {
        float[] samples = [0.5f, -0.25f, 0f, 0.123f, -0.987f];

        var decoded = WavCodec.Decode(WavCodec.Encode(new VoiceSample(samples, 24000)));

        Assert.Equal(24000, decoded.SampleRate);
        Assert.Equal(samples.Length, decoded.Samples.Length);
        for (var i = 0; i < samples.Length; i++) Assert.Equal(samples[i], decoded.Samples[i], 1f / 32768f);
    }

    [Fact]
    public void DecodingYieldsThePcmSamplesOnlyNeverTheHeader()
    {
        float[] samples = [0.5f, -0.5f, 0.25f];

        var decoded = WavCodec.Decode(WavCodec.Encode(new VoiceSample(samples, 48000)));

        Assert.Equal(samples, decoded.Samples);
    }

    [Fact]
    public void ChunksBeforeTheDataAreSkipped()
    {
        var wav = WavCodec.Encode(new VoiceSample([0.5f, -0.5f], 16000));
        // A LIST chunk of 3 bytes, padded to 4, between fmt and data.
        byte[] list = [(byte)'L', (byte)'I', (byte)'S', (byte)'T', 3, 0, 0, 0, 1, 2, 3, 0];
        var withList = new byte[wav.Length + list.Length];
        wav.AsSpan(0, 36).CopyTo(withList);
        list.CopyTo(withList, 36);
        wav.AsSpan(36).CopyTo(withList.AsSpan(36 + list.Length));

        Assert.Equal([0.5f, -0.5f], WavCodec.Decode(withList).Samples);
    }

    [Fact]
    public void OutOfRangeSamplesAreClipped()
    {
        var decoded = WavCodec.Decode(WavCodec.Encode(new VoiceSample([2f, -2f], 48000)));

        Assert.Equal(32767f / 32768f, decoded.Samples[0]);
        Assert.Equal(-1f, decoded.Samples[1]);
    }

    [Fact]
    public void RejectsWhatIsNotAWav()
    {
        Assert.Throws<InvalidDataException>(() => WavCodec.Decode([1, 2, 3]));
        Assert.Throws<InvalidDataException>(() => WavCodec.Decode(new byte[64]));
    }

    [Fact]
    public void RejectsAStereoWav()
    {
        var wav = WavCodec.Encode(new VoiceSample([0f, 0f], 48000));
        wav[22] = 2;

        Assert.Throws<InvalidDataException>(() => WavCodec.Decode(wav));
    }

    [Fact]
    public void RejectsATruncatedWav()
    {
        var wav = WavCodec.Encode(new VoiceSample([0f, 0f, 0f, 0f], 48000));

        Assert.Throws<InvalidDataException>(() => WavCodec.Decode(wav[..^2]));
    }
}
