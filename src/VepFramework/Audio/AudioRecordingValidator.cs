namespace VepMod.VepFramework.Audio;

/// <summary>
///     Valide si un enregistrement audio est acceptable pour être envoyé sur le réseau.
///     Utilise des critères configurables pour filtrer les enregistrements de mauvaise qualité.
/// </summary>
public sealed class AudioRecordingValidator
{
    public AudioRecordingValidator(AudioValidationCriteria criteria)
    {
        Criteria = criteria;
    }

    /// <summary>
    ///     Critères de validation pour les enregistrements audio.
    /// </summary>
    public AudioValidationCriteria Criteria { get; }

    /// <summary>
    ///     Valide un enregistrement audio selon les critères configurés.
    /// </summary>
    /// <param name="samples">Buffer audio normalisé [-1, 1]</param>
    /// <param name="sampleRate">Taux d'échantillonnage en Hz</param>
    /// <returns>Résultat de validation avec détails</returns>
    public AudioValidationResult Validate(float[]? samples, int sampleRate)
    {
        if (samples == null || samples.Length == 0)
        {
            return AudioValidationResult.Rejected(AudioRejectionReason.EmptyBuffer, null);
        }

        var analysis = AudioAnalyzer.Analyze(samples, sampleRate, Criteria.SilenceThreshold);

        // Vérification durée minimale
        if (analysis.DurationSeconds < Criteria.MinDurationSeconds)
        {
            return AudioValidationResult.Rejected(AudioRejectionReason.TooShort, analysis);
        }

        // Vérification RMS minimum
        if (analysis.Rms < Criteria.MinRms)
        {
            return AudioValidationResult.Rejected(AudioRejectionReason.RmsTooLow, analysis);
        }

        // Vérification pic d'amplitude minimum
        if (analysis.PeakAmplitude < Criteria.MinPeakAmplitude)
        {
            return AudioValidationResult.Rejected(AudioRejectionReason.PeakTooLow, analysis);
        }

        // Vérification ratio signal/silence
        if (analysis.NonSilenceRatio < Criteria.MinNonSilenceRatio)
        {
            return AudioValidationResult.Rejected(AudioRejectionReason.TooMuchSilence, analysis);
        }

        return AudioValidationResult.Accepted(analysis);
    }

    /// <summary>
    ///     Vérifie si un buffer audio en cours d'enregistrement dépasse le seuil RMS.
    ///     Utilisé pour décider si on doit continuer à enregistrer.
    /// </summary>
    /// <param name="samples">Buffer audio normalisé [-1, 1]</param>
    /// <param name="offset">Position de départ</param>
    /// <param name="length">Nombre de samples à analyser</param>
    /// <returns>True si le signal dépasse le seuil RMS minimum</returns>
    public bool IsAboveRmsThreshold(float[] samples, int offset, int length)
    {
        var rms = AudioAnalyzer.CalculateRms(samples, offset, length);
        return rms >= Criteria.MinRms;
    }
}

/// <summary>
///     Critères de validation pour les enregistrements audio.
///     Tous les critères doivent être satisfaits pour qu'un enregistrement soit accepté.
/// </summary>
public sealed class AudioValidationCriteria
{
    /// <summary>
    ///     Durée minimale en secondes. Les enregistrements plus courts sont rejetés.
    ///     Valeur recommandée: 0.3 - 0.5 secondes.
    /// </summary>
    public float MinDurationSeconds { get; set; } = 0.3f;

    /// <summary>
    ///     RMS minimum (énergie moyenne). En dessous, l'enregistrement est considéré trop faible.
    ///     Valeur entre 0 et 1. Recommandé: 0.01 - 0.05.
    /// </summary>
    public float MinRms { get; set; } = 0.02f;

    /// <summary>
    ///     Pic d'amplitude minimum. L'enregistrement doit contenir au moins un sample atteignant ce niveau.
    ///     Valeur entre 0 et 1. Recommandé: 0.05 - 0.15.
    /// </summary>
    public float MinPeakAmplitude { get; set; } = 0.1f;

    /// <summary>
    ///     Ratio minimum de samples non-silencieux.
    ///     Valeur entre 0 et 1. Recommandé: 0.1 - 0.3 (10% à 30% de contenu non-silencieux).
    /// </summary>
    public float MinNonSilenceRatio { get; set; } = 0.15f;

    /// <summary>
    ///     Seuil d'amplitude pour considérer un sample comme du silence.
    ///     Valeur entre 0 et 1. Recommandé: 0.01.
    /// </summary>
    public float SilenceThreshold { get; set; } = 0.01f;

    /// <summary>
    ///     Critères par défaut équilibrés pour filtrer les mauvais enregistrements
    ///     tout en gardant la majorité des vrais enregistrements vocaux.
    /// </summary>
    public static AudioValidationCriteria Default => new()
    {
        MinDurationSeconds = 0.3f,
        MinRms = 0.01f,
        MinPeakAmplitude = 0.05f,
        MinNonSilenceRatio = 0.05f,
        SilenceThreshold = 0.005f
    };

    /// <summary>
    ///     Critères stricts pour filtrer agressivement les mauvais enregistrements.
    ///     Peut rejeter certains enregistrements valides mais faibles.
    /// </summary>
    public static AudioValidationCriteria Strict => new()
    {
        MinDurationSeconds = 0.5f,
        MinRms = 0.02f,
        MinPeakAmplitude = 0.1f,
        MinNonSilenceRatio = 0.15f,
        SilenceThreshold = 0.01f
    };

    /// <summary>
    ///     Critères permissifs pour accepter plus d'enregistrements.
    ///     Filtre uniquement les cas les plus évidents (silence complet, bruit).
    /// </summary>
    public static AudioValidationCriteria Permissive => new()
    {
        MinDurationSeconds = 0.2f,
        MinRms = 0.005f,
        MinPeakAmplitude = 0.02f,
        MinNonSilenceRatio = 0.02f,
        SilenceThreshold = 0.003f
    };

    /// <summary>
    ///     Critères calibrés pour REPO basés sur des données réelles de joueurs.
    ///     Accepte ~75% des enregistrements tout en filtrant le silence/bruit.
    /// </summary>
    public static AudioValidationCriteria Game => new()
    {
        MinDurationSeconds = 0.3f,
        MinRms = 0.008f,
        MinPeakAmplitude = 0.04f,
        MinNonSilenceRatio = 0.03f,
        SilenceThreshold = 0.004f
    };
}

/// <summary>
///     Résultat de la validation d'un enregistrement audio.
/// </summary>
public readonly struct AudioValidationResult
{
    /// <summary>
    ///     Indique si l'enregistrement est accepté.
    /// </summary>
    public bool IsValid { get; }

    /// <summary>
    ///     Raison du rejet si l'enregistrement n'est pas valide.
    /// </summary>
    public AudioRejectionReason RejectionReason { get; }

    /// <summary>
    ///     Analyse détaillée de l'audio (null si buffer vide).
    /// </summary>
    public AudioAnalysisResult? Analysis { get; }

    private AudioValidationResult(bool isValid, AudioRejectionReason rejectionReason, AudioAnalysisResult? analysis)
    {
        IsValid = isValid;
        RejectionReason = rejectionReason;
        Analysis = analysis;
    }

    /// <summary>
    ///     Crée un résultat de validation accepté.
    /// </summary>
    public static AudioValidationResult Accepted(AudioAnalysisResult analysis)
    {
        return new AudioValidationResult(true, AudioRejectionReason.None, analysis);
    }

    /// <summary>
    ///     Crée un résultat de validation rejeté avec la raison.
    /// </summary>
    public static AudioValidationResult Rejected(AudioRejectionReason reason, AudioAnalysisResult? analysis)
    {
        return new AudioValidationResult(false, reason, analysis);
    }

    public override string ToString()
    {
        if (IsValid)
        {
            return $"Valid - {Analysis}";
        }

        return $"Rejected ({RejectionReason}) - {Analysis}";
    }
}

/// <summary>
///     Raisons possibles pour le rejet d'un enregistrement audio.
/// </summary>
public enum AudioRejectionReason
{
    /// <summary>
    ///     Aucune raison - l'enregistrement est valide.
    /// </summary>
    None,

    /// <summary>
    ///     Le buffer audio est vide ou null.
    /// </summary>
    EmptyBuffer,

    /// <summary>
    ///     L'enregistrement est trop court.
    /// </summary>
    TooShort,

    /// <summary>
    ///     Le niveau RMS (énergie moyenne) est trop faible.
    /// </summary>
    RmsTooLow,

    /// <summary>
    ///     Le pic d'amplitude est trop faible.
    /// </summary>
    PeakTooLow,

    /// <summary>
    ///     L'enregistrement contient trop de silence.
    /// </summary>
    TooMuchSilence
}