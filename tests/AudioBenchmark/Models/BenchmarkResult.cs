namespace AudioBenchmark.Models;

public sealed class BenchmarkResult
{
    // File
    public required string FileName { get; init; }
    public required string PlayerName { get; init; }
    public required float DurationSeconds { get; init; }
    public required int SampleRate { get; init; }
    public required long FileSizeBytes { get; init; }

    // Raw audio metrics
    public required float Rms { get; init; }
    public required float RmsDb { get; init; }
    public required float PeakAmplitude { get; init; }
    public required float PeakDb { get; init; }
    public required float NonSilenceRatio { get; init; }

    // RMS pre-filter, per preset
    public required string PreFilter_Game { get; init; }
    public required string PreFilter_Default { get; init; }
    public required string PreFilter_Strict { get; init; }
    public required string PreFilter_Permissive { get; init; }

    // VAD, per preset
    public required float VadSpeechRatio { get; init; }
    public required int VadSpeechFrames { get; init; }
    public required int VadTotalFrames { get; init; }
    public required string Vad_Default { get; init; }
    public required string Vad_Strict { get; init; }
    public required string Vad_Permissive { get; init; }

    // Combined pipeline
    public required string Combined_Result { get; init; }
    public required string Combined_RejectionStage { get; init; }

    // Performance
    public required double PreFilterMs { get; init; }
    public required double VadMs { get; init; }
    public required double CombinedMs { get; init; }

    public string ToCsvLine()
    {
        return string.Join(",",
            CsvEscape(FileName),
            CsvEscape(PlayerName),
            DurationSeconds.ToString("F3"),
            SampleRate,
            FileSizeBytes,
            Rms.ToString("F6"),
            RmsDb.ToString("F1"),
            PeakAmplitude.ToString("F6"),
            PeakDb.ToString("F1"),
            NonSilenceRatio.ToString("F4"),
            CsvEscape(PreFilter_Game),
            CsvEscape(PreFilter_Default),
            CsvEscape(PreFilter_Strict),
            CsvEscape(PreFilter_Permissive),
            VadSpeechRatio.ToString("F4"),
            VadSpeechFrames,
            VadTotalFrames,
            CsvEscape(Vad_Default),
            CsvEscape(Vad_Strict),
            CsvEscape(Vad_Permissive),
            CsvEscape(Combined_Result),
            CsvEscape(Combined_RejectionStage),
            PreFilterMs.ToString("F2"),
            VadMs.ToString("F2"),
            CombinedMs.ToString("F2")
        );
    }

    public static string CsvHeader()
    {
        return string.Join(",",
            "FileName", "PlayerName", "DurationSec", "SampleRate", "FileSizeBytes",
            "RMS", "RMS_dB", "Peak", "Peak_dB", "NonSilenceRatio",
            "PreFilter_Game", "PreFilter_Default", "PreFilter_Strict", "PreFilter_Permissive",
            "VAD_SpeechRatio", "VAD_SpeechFrames", "VAD_TotalFrames",
            "VAD_Default", "VAD_Strict", "VAD_Permissive",
            "Combined_Result", "Combined_RejectionStage",
            "PreFilter_ms", "VAD_ms", "Combined_ms"
        );
    }

    private static string CsvEscape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
