using System.Text.Json;
using System.Text.Json.Serialization;

namespace AudioBenchmark.Models;

/// <summary>
///     Classification ground truth d'un fichier audio.
/// </summary>
public enum AudioLabel
{
    /// <summary>Pas encore classifié (cas ambigu nécessitant écoute manuelle).</summary>
    Unknown,

    /// <summary>Contient de la vraie parole humaine intelligible.</summary>
    Speech,

    /// <summary>Bruit de fond, souffle micro, silence, sons non-vocaux.</summary>
    Noise
}

/// <summary>
///     Source de la classification.
/// </summary>
public enum LabelSource
{
    /// <summary>Classifié automatiquement par heuristique (cas évident).</summary>
    Auto,

    /// <summary>Classifié manuellement par écoute humaine.</summary>
    Manual
}

/// <summary>
///     Label d'un fichier audio avec sa source et sa confiance.
/// </summary>
public sealed class FileLabel
{
    public string FileName { get; set; } = "";
    public string PlayerName { get; set; } = "";
    public AudioLabel Label { get; set; } = AudioLabel.Unknown;
    public LabelSource Source { get; set; } = LabelSource.Auto;
    public string Reason { get; set; } = "";

    // Métriques pour aider à la décision manuelle
    public float Rms { get; set; }
    public float PeakAmplitude { get; set; }
    public float SpeechRatio { get; set; }
    public float DurationSeconds { get; set; }
}

/// <summary>
///     Fichier de labels pour le dataset complet.
/// </summary>
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
