using System.Text.Json;
using System.Text.Json.Serialization;

namespace AudioBenchmark.Models;

/// <summary>
///     Ground truth of an audio file.
/// </summary>
public enum AudioLabel
{
    /// <summary>Not classified yet: ambiguous, needs a listen.</summary>
    Unknown,

    /// <summary>Intelligible human speech.</summary>
    Speech,

    /// <summary>Background noise, mic breath, silence, non-vocal sounds.</summary>
    Noise
}

public enum LabelSource
{
    /// <summary>Classified by heuristic: an obvious case.</summary>
    Auto,

    /// <summary>Classified by a person listening.</summary>
    Manual
}

public sealed class FileLabel
{
    public string FileName { get; set; } = "";
    public string PlayerName { get; set; } = "";
    public AudioLabel Label { get; set; } = AudioLabel.Unknown;
    public LabelSource Source { get; set; } = LabelSource.Auto;
    public string Reason { get; set; } = "";

    // Shown to help a manual decision
    public float Rms { get; set; }
    public float PeakAmplitude { get; set; }
    public float SpeechRatio { get; set; }
    public float DurationSeconds { get; set; }
}

public sealed class DatasetLabels
{
    public string DatasetPath { get; set; } = "";
    public DateTime GeneratedAt { get; set; } = DateTime.Now;
    public int TotalFiles { get; set; }
    public int AutoLabeled { get; set; }
    public int ManualRequired { get; set; }
    public List<FileLabel> Labels { get; set; } = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public void Save(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));

    public static DatasetLabels Load(string path) =>
        JsonSerializer.Deserialize<DatasetLabels>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidDataException($"Failed to load labels from {path}");
}
