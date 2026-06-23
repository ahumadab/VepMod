namespace AudioBenchmark.Validators;

/// <summary>
///     Port standalone de VepMod.VepFramework.Audio.AudioRecordingValidator.
/// </summary>
public sealed class AudioRecordingValidator
{
    public AudioValidationCriteria Criteria { get; }

    public AudioRecordingValidator(AudioValidationCriteria criteria)
    {
        Criteria = criteria;
    }

    public AudioValidationResult Validate(float[]? samples, int sampleRate)
    {
        if (samples == null || samples.Length == 0)
            return AudioValidationResult.Rejected(AudioRejectionReason.EmptyBuffer, null);

        var analysis = AudioAnalyzer.Analyze(samples, sampleRate, Criteria.SilenceThreshold);

        if (analysis.DurationSeconds < Criteria.MinDurationSeconds)
            return AudioValidationResult.Rejected(AudioRejectionReason.TooShort, analysis);

        if (analysis.Rms < Criteria.MinRms)
            return AudioValidationResult.Rejected(AudioRejectionReason.RmsTooLow, analysis);

        if (analysis.PeakAmplitude < Criteria.MinPeakAmplitude)
            return AudioValidationResult.Rejected(AudioRejectionReason.PeakTooLow, analysis);

        if (analysis.NonSilenceRatio < Criteria.MinNonSilenceRatio)
            return AudioValidationResult.Rejected(AudioRejectionReason.TooMuchSilence, analysis);

        return AudioValidationResult.Accepted(analysis);
    }
}

public sealed class AudioValidationCriteria
{
    public float MinDurationSeconds { get; set; } = 0.3f;
    public float MinRms { get; set; } = 0.02f;
    public float MinPeakAmplitude { get; set; } = 0.1f;
    public float MinNonSilenceRatio { get; set; } = 0.15f;
    public float SilenceThreshold { get; set; } = 0.01f;

    public static AudioValidationCriteria Default => new()
    {
        MinDurationSeconds = 0.3f, MinRms = 0.01f, MinPeakAmplitude = 0.05f,
        MinNonSilenceRatio = 0.05f, SilenceThreshold = 0.005f
    };

    public static AudioValidationCriteria Strict => new()
    {
        MinDurationSeconds = 0.5f, MinRms = 0.02f, MinPeakAmplitude = 0.1f,
        MinNonSilenceRatio = 0.15f, SilenceThreshold = 0.01f
    };

    public static AudioValidationCriteria Permissive => new()
    {
        MinDurationSeconds = 0.2f, MinRms = 0.005f, MinPeakAmplitude = 0.02f,
        MinNonSilenceRatio = 0.02f, SilenceThreshold = 0.003f
    };

    public static AudioValidationCriteria Game => new()
    {
        MinDurationSeconds = 0.3f, MinRms = 0.008f, MinPeakAmplitude = 0.04f,
        MinNonSilenceRatio = 0.03f, SilenceThreshold = 0.004f
    };
}

public readonly struct AudioValidationResult
{
    public bool IsValid { get; }
    public AudioRejectionReason RejectionReason { get; }
    public AudioAnalysisResult? Analysis { get; }

    private AudioValidationResult(bool isValid, AudioRejectionReason reason, AudioAnalysisResult? analysis)
    { IsValid = isValid; RejectionReason = reason; Analysis = analysis; }

    public static AudioValidationResult Accepted(AudioAnalysisResult analysis) => new(true, AudioRejectionReason.None, analysis);
    public static AudioValidationResult Rejected(AudioRejectionReason reason, AudioAnalysisResult? analysis) => new(false, reason, analysis);

    public override string ToString() => IsValid ? $"Valid - {Analysis}" : $"Rejected ({RejectionReason}) - {Analysis}";
}

public enum AudioRejectionReason { None, EmptyBuffer, TooShort, RmsTooLow, PeakTooLow, TooMuchSilence }
