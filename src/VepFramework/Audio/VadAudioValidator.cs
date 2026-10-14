#if !VEPMOD_NO_VAD
using System;
using WebRtcVadSharp;

namespace VepMod.VepFramework.Audio;

/// <summary>
///     Keeps a recording only if WebRTC VAD finds speech in it. Its Gaussian mixture model tells speech apart
///     better than plain RMS or peak thresholds.
/// </summary>
public sealed class VadAudioValidator : IDisposable
{
    private readonly VadValidationCriteria _criteria;
    private readonly WebRtcVad _vad;
    private readonly object _gate = new();
    private bool _disposed;

    public VadAudioValidator(VadValidationCriteria? criteria = null)
    {
        _criteria = criteria ?? VadValidationCriteria.FromSensitivity(VadSensitivity.Balanced);
        _vad = new WebRtcVad
        {
            OperatingMode = _criteria.OperatingMode,
            FrameLength = _criteria.FrameLength,
            SampleRate = _criteria.SampleRate
        };
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _vad.Dispose();
            _disposed = true;
        }
    }

    /// <param name="samples">Normalised to [-1, 1].</param>
    /// <param name="sampleRate">In Hz.</param>
    public VadValidationResult Validate(float[] samples, int sampleRate)
    {
        if (samples == null || samples.Length == 0)
        {
            return VadValidationResult.Rejected(VadRejectionReason.EmptyBuffer, null);
        }

        var duration = (float)samples.Length / sampleRate;
        if (duration < _criteria.MinDurationSeconds)
        {
            return VadValidationResult.Rejected(VadRejectionReason.TooShort,
                new VadAnalysisResult(duration, 0, 0, 0f));
        }

        var pcmBytes = ConvertToPcm16(samples);

        var targetSampleRate = GetTargetSampleRate(sampleRate);
        var frameSamples = GetFrameSamplesCount(targetSampleRate, _criteria.FrameLength);
        var frameBytesSize = frameSamples * 2;

        if (sampleRate != (int)targetSampleRate)
        {
            pcmBytes = Resample(pcmBytes, sampleRate, (int)targetSampleRate);
        }

        var totalFrames = pcmBytes.Length / frameBytesSize;
        if (totalFrames == 0)
        {
            return VadValidationResult.Rejected(VadRejectionReason.TooShort,
                new VadAnalysisResult(duration, 0, 0, 0f));
        }

        var speechFrames = 0;
        var frameBuffer = new byte[frameBytesSize];

        lock (_gate)
        {
            // OnDestroy, on the Unity thread, can dispose the validator while Validate runs on the Photon voice
            // thread: accepting beats calling a native VAD that is already freed.
            if (_disposed)
            {
                return VadValidationResult.Accepted(new VadAnalysisResult(duration, totalFrames, 0, 0f));
            }

            for (var i = 0; i < totalFrames; i++)
            {
                var offset = i * frameBytesSize;
                if (offset + frameBytesSize > pcmBytes.Length)
                {
                    break;
                }

                Array.Copy(pcmBytes, offset, frameBuffer, 0, frameBytesSize);

                if (_vad.HasSpeech(frameBuffer, targetSampleRate, _criteria.FrameLength))
                {
                    speechFrames++;
                }
            }
        }

        var speechRatio = (float)speechFrames / totalFrames;
        var analysis = new VadAnalysisResult(duration, totalFrames, speechFrames, speechRatio);

        if (speechRatio < _criteria.MinSpeechRatio)
        {
            return VadValidationResult.Rejected(VadRejectionReason.NotEnoughSpeech, analysis);
        }

        return VadValidationResult.Accepted(analysis);
    }

    private static byte[] ConvertToPcm16(float[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            var sample = (short)(samples[i] * 32767f);
            bytes[i * 2] = (byte)(sample & 0xFF);
            bytes[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
        }

        return bytes;
    }

    private static SampleRate GetTargetSampleRate(int sampleRate)
    {
        return sampleRate switch
        {
            8000 => SampleRate.Is8kHz,
            16000 => SampleRate.Is16kHz,
            32000 => SampleRate.Is32kHz,
            48000 => SampleRate.Is48kHz,
            // Other rates are resampled to 16 kHz.
            _ => SampleRate.Is16kHz
        };
    }

    private static int GetFrameSamplesCount(SampleRate sampleRate, FrameLength frameLength)
    {
        var rate = (int)sampleRate;
        var ms = frameLength switch
        {
            FrameLength.Is10ms => 10,
            FrameLength.Is20ms => 20,
            FrameLength.Is30ms => 30,
            _ => 20
        };
        return rate * ms / 1000;
    }

    private static byte[] Resample(byte[] pcmBytes, int sourceSampleRate, int targetSampleRate)
    {
        if (pcmBytes == null || pcmBytes.Length < 2)
        {
            return Array.Empty<byte>();
        }

        if (sourceSampleRate == targetSampleRate)
        {
            return pcmBytes;
        }

        var srcSamplesCount = pcmBytes.Length / 2;
        var src = new short[srcSamplesCount];

        for (int i = 0, bi = 0; i < srcSamplesCount; i++, bi += 2)
        {
            src[i] = (short)(pcmBytes[bi] | (pcmBytes[bi + 1] << 8));
        }

        var step = (double)sourceSampleRate / targetSampleRate;
        var dstSamplesCount = (int)Math.Floor((srcSamplesCount - 1) / step);
        if (dstSamplesCount <= 0)
        {
            return Array.Empty<byte>();
        }

        var dst = new short[dstSamplesCount];

        var pos = 0.0;
        for (var i = 0; i < dstSamplesCount; i++)
        {
            var idx = (int)pos;
            var frac = pos - idx;

            var s0 = src[idx];
            var s1 = src[idx + 1];

            var sample = s0 + (s1 - s0) * frac;

            if (sample > short.MaxValue)
            {
                sample = short.MaxValue;
            }
            else if (sample < short.MinValue)
            {
                sample = short.MinValue;
            }

            dst[i] = (short)sample;
            pos += step;

            // Keeps idx + 1 in range on the last sample.
            if (pos >= srcSamplesCount - 1)
            {
                pos = srcSamplesCount - 1.000001;
            }
        }

        var outBytes = new byte[dstSamplesCount * 2];
        for (int i = 0, bi = 0; i < dstSamplesCount; i++, bi += 2)
        {
            var v = dst[i];
            outBytes[bi] = (byte)(v & 0xFF);
            outBytes[bi + 1] = (byte)((v >> 8) & 0xFF);
        }

        return outBytes;
    }
}

public sealed class VadValidationCriteria
{
    /// <summary>
    ///     In seconds.
    /// </summary>
    public float MinDurationSeconds { get; set; } = 0.3f;

    /// <summary>
    ///     Minimum share of frames with speech, from 0 to 1.
    /// </summary>
    public float MinSpeechRatio { get; set; } = 0.1f;

    /// <summary>
    ///     More aggressive means fewer false positives, but quiet speech can be missed.
    /// </summary>
    public OperatingMode OperatingMode { get; set; } = OperatingMode.HighQuality;

    public FrameLength FrameLength { get; set; } = FrameLength.Is20ms;

    public SampleRate SampleRate { get; set; } = SampleRate.Is16kHz;

    /// <summary>
    ///     The ratios are points measured by the benchmark (248 real WAVs, 180 speech and 68 noise, all in
    ///     OperatingMode.HighQuality):
    ///       Permissive 0.20 -> recall 99% / precision 80%
    ///       Balanced   0.40 -> recall 97% / precision 85% (best F1)
    ///       Strict     0.60 -> recall 90% / precision 89%
    ///     MinDurationSeconds is 0: WhispralMimics checks the duration upstream, from ConfigAudioMinDuration, so it
    ///     follows the user's config.
    /// </summary>
    public static VadValidationCriteria FromSensitivity(VadSensitivity sensitivity)
    {
        var ratio = sensitivity switch
        {
            VadSensitivity.Permissive => 0.20f,
            VadSensitivity.Strict => 0.60f,
            _ => 0.40f // Balanced
        };

        return new VadValidationCriteria
        {
            MinDurationSeconds = 0f,
            MinSpeechRatio = ratio,
            OperatingMode = OperatingMode.HighQuality,
            FrameLength = FrameLength.Is20ms,
            SampleRate = SampleRate.Is16kHz
        };
    }
}

/// <summary>
///     Exposed in the user config.
/// </summary>
public enum VadSensitivity
{
    /// <summary>Keeps nearly all speech (ratio 0.20), lets a bit more noise through.</summary>
    Permissive,

    /// <summary>The default trade-off (ratio 0.40), best F1.</summary>
    Balanced,

    /// <summary>Filters harder (ratio 0.60), can reject quiet speech.</summary>
    Strict
}

public readonly struct VadAnalysisResult
{
    public float DurationSeconds { get; }
    public int TotalFrames { get; }
    public int SpeechFrames { get; }
    public float SpeechRatio { get; }

    public VadAnalysisResult(float durationSeconds, int totalFrames, int speechFrames, float speechRatio)
    {
        DurationSeconds = durationSeconds;
        TotalFrames = totalFrames;
        SpeechFrames = speechFrames;
        SpeechRatio = speechRatio;
    }

    public override string ToString()
    {
        return $"Duration: {DurationSeconds:F2}s, Speech: {SpeechFrames}/{TotalFrames} frames ({SpeechRatio:P1})";
    }
}

public readonly struct VadValidationResult
{
    public bool IsValid { get; }
    public VadRejectionReason RejectionReason { get; }
    public VadAnalysisResult? Analysis { get; }

    private VadValidationResult(bool isValid, VadRejectionReason reason, VadAnalysisResult? analysis)
    {
        IsValid = isValid;
        RejectionReason = reason;
        Analysis = analysis;
    }

    public static VadValidationResult Accepted(VadAnalysisResult analysis)
    {
        return new VadValidationResult(true, VadRejectionReason.None, analysis);
    }

    public static VadValidationResult Rejected(VadRejectionReason reason, VadAnalysisResult? analysis)
    {
        return new VadValidationResult(false, reason, analysis);
    }

    public override string ToString()
    {
        return IsValid ? $"Valid - {Analysis}" : $"Rejected ({RejectionReason}) - {Analysis}";
    }
}

public enum VadRejectionReason
{
    None,
    EmptyBuffer,
    TooShort,
    NotEnoughSpeech
}
#endif
