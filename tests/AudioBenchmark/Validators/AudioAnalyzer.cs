namespace AudioBenchmark.Validators;

/// <summary>
///     Port standalone de VepMod.VepFramework.Audio.AudioAnalyzer (sans dépendance Unity).
/// </summary>
public static class AudioAnalyzer
{
    public const float DefaultSilenceThreshold = 0.01f;

    public static float CalculateRms(float[] samples)
    {
        if (samples == null || samples.Length == 0) return 0f;

        var sumSquares = 0.0;
        for (var i = 0; i < samples.Length; i++)
            sumSquares += samples[i] * samples[i];

        return (float)Math.Sqrt(sumSquares / samples.Length);
    }

    public static float CalculatePeakAmplitude(float[] samples)
    {
        if (samples == null || samples.Length == 0) return 0f;

        var peak = 0f;
        for (var i = 0; i < samples.Length; i++)
        {
            var abs = Math.Abs(samples[i]);
            if (abs > peak) peak = abs;
        }
        return peak;
    }

    public static float AmplitudeToDecibels(float amplitude)
    {
        if (amplitude <= 0f) return float.NegativeInfinity;
        return 20f * (float)Math.Log10(amplitude);
    }

    public static float CalculateNonSilenceRatio(float[] samples, float silenceThreshold = DefaultSilenceThreshold)
    {
        if (samples == null || samples.Length == 0) return 0f;

        var nonSilentCount = 0;
        for (var i = 0; i < samples.Length; i++)
            if (Math.Abs(samples[i]) > silenceThreshold)
                nonSilentCount++;

        return (float)nonSilentCount / samples.Length;
    }

    public static AudioAnalysisResult Analyze(float[] samples, int sampleRate, float silenceThreshold = DefaultSilenceThreshold)
    {
        if (samples == null || samples.Length == 0)
            return new AudioAnalysisResult(0f, float.NegativeInfinity, 0f, float.NegativeInfinity, 0f, 0f, 0);

        var rms = CalculateRms(samples);
        var peak = CalculatePeakAmplitude(samples);
        var nonSilenceRatio = CalculateNonSilenceRatio(samples, silenceThreshold);
        var duration = (float)samples.Length / sampleRate;

        return new AudioAnalysisResult(rms, AmplitudeToDecibels(rms), peak, AmplitudeToDecibels(peak), nonSilenceRatio, duration, samples.Length);
    }
}

public readonly struct AudioAnalysisResult
{
    public float Rms { get; }
    public float RmsDecibels { get; }
    public float PeakAmplitude { get; }
    public float PeakDecibels { get; }
    public float NonSilenceRatio { get; }
    public float DurationSeconds { get; }
    public int SampleCount { get; }

    public AudioAnalysisResult(float rms, float rmsDb, float peak, float peakDb, float nonSilenceRatio, float duration, int sampleCount)
    {
        Rms = rms; RmsDecibels = rmsDb; PeakAmplitude = peak; PeakDecibels = peakDb;
        NonSilenceRatio = nonSilenceRatio; DurationSeconds = duration; SampleCount = sampleCount;
    }

    public override string ToString() =>
        $"RMS: {Rms:F4} ({RmsDecibels:F1}dB), Peak: {PeakAmplitude:F4} ({PeakDecibels:F1}dB), NonSilence: {NonSilenceRatio:P1}, Duration: {DurationSeconds:F2}s";
}
