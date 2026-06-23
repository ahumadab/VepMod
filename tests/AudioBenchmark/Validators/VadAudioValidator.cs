using WebRtcVadSharp;

namespace AudioBenchmark.Validators;

/// <summary>
///     Port standalone de VepMod.VepFramework.Audio.VadAudioValidator.
/// </summary>
public sealed class VadAudioValidator : IDisposable
{
    private readonly VadValidationCriteria _criteria;
    private readonly WebRtcVad _vad;
    private bool _disposed;

    public VadAudioValidator(VadValidationCriteria? criteria = null)
    {
        _criteria = criteria ?? VadValidationCriteria.Default;
        _vad = new WebRtcVad
        {
            OperatingMode = _criteria.OperatingMode,
            FrameLength = _criteria.FrameLength,
            SampleRate = _criteria.SampleRate
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _vad.Dispose();
        _disposed = true;
    }

    public VadValidationResult Validate(float[] samples, int sampleRate)
    {
        if (samples == null || samples.Length == 0)
            return VadValidationResult.Rejected(VadRejectionReason.EmptyBuffer, null);

        var duration = (float)samples.Length / sampleRate;
        if (duration < _criteria.MinDurationSeconds)
            return VadValidationResult.Rejected(VadRejectionReason.TooShort, new VadAnalysisResult(duration, 0, 0, 0f));

        var pcmBytes = ConvertToPcm16(samples);
        var targetSampleRate = GetTargetSampleRate(sampleRate);
        var frameSamples = GetFrameSamplesCount(targetSampleRate, _criteria.FrameLength);
        var frameBytesSize = frameSamples * 2;

        if (sampleRate != (int)targetSampleRate)
            pcmBytes = Resample(pcmBytes, sampleRate, (int)targetSampleRate);

        var totalFrames = pcmBytes.Length / frameBytesSize;
        if (totalFrames == 0)
            return VadValidationResult.Rejected(VadRejectionReason.TooShort, new VadAnalysisResult(duration, 0, 0, 0f));

        var speechFrames = 0;
        var frameBuffer = new byte[frameBytesSize];

        for (var i = 0; i < totalFrames; i++)
        {
            var offset = i * frameBytesSize;
            if (offset + frameBytesSize > pcmBytes.Length) break;
            Array.Copy(pcmBytes, offset, frameBuffer, 0, frameBytesSize);
            if (_vad.HasSpeech(frameBuffer, targetSampleRate, _criteria.FrameLength))
                speechFrames++;
        }

        var speechRatio = (float)speechFrames / totalFrames;
        var analysis = new VadAnalysisResult(duration, totalFrames, speechFrames, speechRatio);

        return speechRatio < _criteria.MinSpeechRatio
            ? VadValidationResult.Rejected(VadRejectionReason.NotEnoughSpeech, analysis)
            : VadValidationResult.Accepted(analysis);
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

    private static SampleRate GetTargetSampleRate(int sampleRate) => sampleRate switch
    {
        8000 => SampleRate.Is8kHz,
        16000 => SampleRate.Is16kHz,
        32000 => SampleRate.Is32kHz,
        48000 => SampleRate.Is48kHz,
        _ => SampleRate.Is16kHz
    };

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
        if (pcmBytes == null || pcmBytes.Length < 2) return [];
        if (sourceSampleRate == targetSampleRate) return pcmBytes;

        var srcSamplesCount = pcmBytes.Length / 2;
        var src = new short[srcSamplesCount];
        for (int i = 0, bi = 0; i < srcSamplesCount; i++, bi += 2)
            src[i] = (short)(pcmBytes[bi] | (pcmBytes[bi + 1] << 8));

        var step = (double)sourceSampleRate / targetSampleRate;
        var dstSamplesCount = (int)Math.Floor((srcSamplesCount - 1) / step);
        if (dstSamplesCount <= 0) return [];

        var dst = new short[dstSamplesCount];
        var pos = 0.0;
        for (var i = 0; i < dstSamplesCount; i++)
        {
            var idx = (int)pos;
            var frac = pos - idx;
            var s0 = src[idx];
            var s1 = src[idx + 1];
            var sample = s0 + (s1 - s0) * frac;
            if (sample > short.MaxValue) sample = short.MaxValue;
            else if (sample < short.MinValue) sample = short.MinValue;
            dst[i] = (short)sample;
            pos += step;
            if (pos >= srcSamplesCount - 1) pos = srcSamplesCount - 1.000001;
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
    public float MinDurationSeconds { get; set; } = 0.3f;
    public float MinSpeechRatio { get; set; } = 0.1f;
    public OperatingMode OperatingMode { get; set; } = OperatingMode.HighQuality;
    public FrameLength FrameLength { get; set; } = FrameLength.Is20ms;
    public SampleRate SampleRate { get; set; } = SampleRate.Is16kHz;

    public static VadValidationCriteria Default => new()
    {
        MinDurationSeconds = 0.3f, MinSpeechRatio = 0.1f,
        OperatingMode = OperatingMode.HighQuality, FrameLength = FrameLength.Is20ms, SampleRate = SampleRate.Is16kHz
    };

    public static VadValidationCriteria Strict => new()
    {
        MinDurationSeconds = 1f, MinSpeechRatio = 0.6f,
        OperatingMode = OperatingMode.Aggressive, FrameLength = FrameLength.Is20ms, SampleRate = SampleRate.Is16kHz
    };

    public static VadValidationCriteria Permissive => new()
    {
        MinDurationSeconds = 0.2f, MinSpeechRatio = 0.05f,
        OperatingMode = OperatingMode.HighQuality, FrameLength = FrameLength.Is20ms, SampleRate = SampleRate.Is16kHz
    };
}

public readonly struct VadAnalysisResult
{
    public float DurationSeconds { get; }
    public int TotalFrames { get; }
    public int SpeechFrames { get; }
    public float SpeechRatio { get; }

    public VadAnalysisResult(float duration, int total, int speech, float ratio)
    { DurationSeconds = duration; TotalFrames = total; SpeechFrames = speech; SpeechRatio = ratio; }

    public override string ToString() => $"Duration: {DurationSeconds:F2}s, Speech: {SpeechFrames}/{TotalFrames} ({SpeechRatio:P1})";
}

public readonly struct VadValidationResult
{
    public bool IsValid { get; }
    public VadRejectionReason RejectionReason { get; }
    public VadAnalysisResult? Analysis { get; }

    private VadValidationResult(bool isValid, VadRejectionReason reason, VadAnalysisResult? analysis)
    { IsValid = isValid; RejectionReason = reason; Analysis = analysis; }

    public static VadValidationResult Accepted(VadAnalysisResult analysis) => new(true, VadRejectionReason.None, analysis);
    public static VadValidationResult Rejected(VadRejectionReason reason, VadAnalysisResult? analysis) => new(false, reason, analysis);

    public override string ToString() => IsValid ? $"Valid - {Analysis}" : $"Rejected ({RejectionReason}) - {Analysis}";
}

public enum VadRejectionReason { None, EmptyBuffer, TooShort, NotEnoughSpeech }
