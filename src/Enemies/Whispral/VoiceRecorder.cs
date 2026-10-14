using System;
using System.Collections.Concurrent;
using System.Threading;

namespace VepMod.Enemies.Whispral;

/// <param name="trailingSilenceLength">In samples, at the end of <paramref name="sample" />.</param>
public sealed class Capture(VoiceSample sample, int trailingSilenceLength)
{
    public VoiceSample Sample { get; } = sample;

    public int TrailingSilenceLength { get; } = trailingSilenceLength;

    public float DurationSeconds => (float)Sample.Samples.Length / Sample.SampleRate;
}

/// <summary>
///     Cuts one Capture out of the local player's voice frames per request, and queues it for the main thread.
/// </summary>
/// <param name="logDebug">Called on Photon's voice thread.</param>
public sealed class VoiceRecorder(Action<string> logDebug)
{
    private const float SilenceTimeoutSeconds = 0.5f;
    private const int MaxDurationSeconds = 6;

    private readonly ConcurrentQueue<Capture> _captures = new();
    private int _requested;

    // Photon's voice thread only.
    private float[] _buffer = [];
    private bool _capturing;
    private int _length;
    private int _sampleRate;
    private int _channels;
    private int _silenceLength;
    private int _silenceLimit;

    /// <summary>Any thread. The Capture starts on the next frame where the player talks.</summary>
    public void RequestCapture()
    {
        Interlocked.Exchange(ref _requested, 1);
    }

    /// <summary>Photon's voice thread.</summary>
    /// <param name="pcm">Interleaved when <paramref name="channels" /> is above 1.</param>
    /// <param name="sampleRate">In Hz.</param>
    public void PushFrame(bool isTalking, short[] pcm, int sampleRate, int channels)
    {
        if (!_capturing)
        {
            if (!isTalking || Interlocked.Exchange(ref _requested, 0) == 0) return;
            Start(sampleRate, channels);
        }

        var written = Append(pcm);
        _silenceLength = isTalking ? 0 : _silenceLength + written;
        if (_silenceLength >= _silenceLimit || _length == _buffer.Length) End();
    }

    public bool TryTakeCapture(out Capture capture)
    {
        return _captures.TryDequeue(out capture!);
    }

    /// <summary>Drops the pending request and the queued Captures; a Capture under way still ends in the queue.</summary>
    public void Clear()
    {
        Interlocked.Exchange(ref _requested, 0);
        _captures.Clear();
    }

    private void Start(int sampleRate, int channels)
    {
        _capturing = true;
        _sampleRate = sampleRate;
        _channels = channels;
        _length = 0;
        _silenceLength = 0;
        _silenceLimit = (int)(SilenceTimeoutSeconds * sampleRate);
        if (_buffer.Length != sampleRate * MaxDurationSeconds) _buffer = new float[sampleRate * MaxDurationSeconds];
        logDebug($"Capture started ({sampleRate} Hz, {channels} ch).");
    }

    // The Voice Sample is mono, so the channels are averaged.
    private int Append(short[] pcm)
    {
        var frames = Math.Min(pcm.Length / _channels, _buffer.Length - _length);
        for (var i = 0; i < frames; i++)
        {
            var sum = 0f;
            for (var c = 0; c < _channels; c++) sum += pcm[i * _channels + c];
            _buffer[_length + i] = sum / (_channels * WavCodec.FullScale);
        }

        _length += frames;
        return frames;
    }

    private void End()
    {
        _capturing = false;
        var samples = new float[_length];
        Array.Copy(_buffer, samples, _length);
        var capture = new Capture(new VoiceSample(samples, _sampleRate), _silenceLength);
        _captures.Enqueue(capture);
        logDebug($"Capture ended: {capture.DurationSeconds:F2} s at {_sampleRate} Hz, " +
                 $"{(float)_silenceLength / _sampleRate:F2} s of trailing silence.");
    }
}

/// <returns>Whether <paramref name="speech" /> holds enough speech, and the analysis for the log.</returns>
public delegate (bool hasSpeech, string analysis) SpeechCheck(float[] speech, int sampleRate);

public static class CaptureValidation
{
    // Shorter speech before the trailing silence is analysed with it.
    private const float MinSpeechSeconds = 0.1f;

    /// <param name="speechCheck">Null without a VAD.</param>
    /// <returns>Whether the Capture becomes a Voice Sample, and the details for the log.</returns>
    public static (bool accepted, string detail) Validate(Capture capture, float minDurationSeconds,
        SpeechCheck? speechCheck)
    {
        if (capture.DurationSeconds < minDurationSeconds)
        {
            return (false, $"too short ({capture.DurationSeconds:F2} s < {minDurationSeconds:F2} s)");
        }

        if (speechCheck == null) return (true, "no VAD");

        try
        {
            var (hasSpeech, analysis) = speechCheck(SpeechPart(capture), capture.Sample.SampleRate);
            return (hasSpeech, $"VAD {analysis}");
        }
        catch (Exception ex)
        {
            // Fail-open (ADR-0004).
            return (true, $"VAD threw, accepted: {ex.Message}");
        }
    }

    // The trailing silence would dilute the speech ratio and penalise short words; the Voice Sample keeps it.
    private static float[] SpeechPart(Capture capture)
    {
        var samples = capture.Sample.Samples;
        var speechLength = samples.Length - capture.TrailingSilenceLength;
        if (capture.TrailingSilenceLength == 0 || speechLength < MinSpeechSeconds * capture.Sample.SampleRate)
        {
            return samples;
        }

        var speech = new float[speechLength];
        Array.Copy(samples, speech, speechLength);
        return speech;
    }
}
