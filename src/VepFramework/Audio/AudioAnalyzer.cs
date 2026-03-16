using System;

namespace VepMod.VepFramework.Audio;

/// <summary>
///     Utilitaire statique pour analyser les caractéristiques d'un signal audio.
///     Fournit des méthodes pour calculer RMS, pic d'amplitude, ratio signal/silence, etc.
/// </summary>
public static class AudioAnalyzer
{
    /// <summary>
    ///     Seuil par défaut pour considérer un sample comme du silence.
    ///     En dessous de cette amplitude, le sample est considéré silencieux.
    /// </summary>
    public const float DefaultSilenceThreshold = 0.01f;

    /// <summary>
    ///     Calcule le RMS (Root Mean Square) d'un buffer audio.
    ///     Le RMS représente l'énergie moyenne du signal.
    /// </summary>
    /// <param name="samples">Buffer audio normalisé [-1, 1]</param>
    /// <returns>Valeur RMS entre 0 et 1</returns>
    public static float CalculateRms(float[] samples)
    {
        if (samples == null || samples.Length == 0)
            return 0f;

        var sumSquares = 0.0;
        for (var i = 0; i < samples.Length; i++)
        {
            sumSquares += samples[i] * samples[i];
        }

        return (float)Math.Sqrt(sumSquares / samples.Length);
    }

    /// <summary>
    ///     Calcule le RMS d'une portion du buffer audio.
    /// </summary>
    /// <param name="samples">Buffer audio normalisé [-1, 1]</param>
    /// <param name="offset">Position de départ</param>
    /// <param name="length">Nombre de samples à analyser</param>
    /// <returns>Valeur RMS entre 0 et 1</returns>
    public static float CalculateRms(float[] samples, int offset, int length)
    {
        if (samples == null || samples.Length == 0 || length <= 0)
            return 0f;

        var end = Math.Min(offset + length, samples.Length);
        var actualLength = end - offset;
        if (actualLength <= 0)
            return 0f;

        var sumSquares = 0.0;
        for (var i = offset; i < end; i++)
        {
            sumSquares += samples[i] * samples[i];
        }

        return (float)Math.Sqrt(sumSquares / actualLength);
    }

    /// <summary>
    ///     Trouve le pic d'amplitude (valeur absolue maximale) dans le buffer.
    /// </summary>
    /// <param name="samples">Buffer audio normalisé [-1, 1]</param>
    /// <returns>Pic d'amplitude entre 0 et 1</returns>
    public static float CalculatePeakAmplitude(float[] samples)
    {
        if (samples == null || samples.Length == 0)
            return 0f;

        var peak = 0f;
        for (var i = 0; i < samples.Length; i++)
        {
            var abs = Math.Abs(samples[i]);
            if (abs > peak)
                peak = abs;
        }

        return peak;
    }

    /// <summary>
    ///     Convertit une amplitude linéaire (0-1) en décibels.
    /// </summary>
    /// <param name="amplitude">Amplitude linéaire entre 0 et 1</param>
    /// <returns>Valeur en dB (négative, 0dB = amplitude max)</returns>
    public static float AmplitudeToDecibels(float amplitude)
    {
        if (amplitude <= 0f)
            return float.NegativeInfinity;

        return 20f * (float)Math.Log10(amplitude);
    }

    /// <summary>
    ///     Convertit des décibels en amplitude linéaire.
    /// </summary>
    /// <param name="decibels">Valeur en dB</param>
    /// <returns>Amplitude linéaire entre 0 et 1</returns>
    public static float DecibelsToAmplitude(float decibels)
    {
        return (float)Math.Pow(10, decibels / 20f);
    }

    /// <summary>
    ///     Calcule le ratio de samples non-silencieux dans le buffer.
    /// </summary>
    /// <param name="samples">Buffer audio normalisé [-1, 1]</param>
    /// <param name="silenceThreshold">Seuil en dessous duquel un sample est considéré silencieux</param>
    /// <returns>Ratio entre 0 (tout silence) et 1 (aucun silence)</returns>
    public static float CalculateNonSilenceRatio(float[] samples, float silenceThreshold = DefaultSilenceThreshold)
    {
        if (samples == null || samples.Length == 0)
            return 0f;

        var nonSilentCount = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            if (Math.Abs(samples[i]) > silenceThreshold)
                nonSilentCount++;
        }

        return (float)nonSilentCount / samples.Length;
    }

    /// <summary>
    ///     Analyse complète d'un buffer audio.
    /// </summary>
    /// <param name="samples">Buffer audio normalisé [-1, 1]</param>
    /// <param name="sampleRate">Taux d'échantillonnage en Hz</param>
    /// <param name="silenceThreshold">Seuil de silence</param>
    /// <returns>Résultat d'analyse contenant toutes les métriques</returns>
    public static AudioAnalysisResult Analyze(float[] samples, int sampleRate, float silenceThreshold = DefaultSilenceThreshold)
    {
        if (samples == null || samples.Length == 0)
        {
            return new AudioAnalysisResult(0f, float.NegativeInfinity, 0f, float.NegativeInfinity, 0f, 0f, 0);
        }

        var rms = CalculateRms(samples);
        var peak = CalculatePeakAmplitude(samples);
        var nonSilenceRatio = CalculateNonSilenceRatio(samples, silenceThreshold);
        var duration = (float)samples.Length / sampleRate;

        return new AudioAnalysisResult(
            rms,
            AmplitudeToDecibels(rms),
            peak,
            AmplitudeToDecibels(peak),
            nonSilenceRatio,
            duration,
            samples.Length);
    }
}

/// <summary>
///     Résultat d'une analyse audio complète.
/// </summary>
public readonly struct AudioAnalysisResult
{
    /// <summary>
    ///     RMS (Root Mean Square) - énergie moyenne du signal (0-1).
    /// </summary>
    public float Rms { get; }

    /// <summary>
    ///     RMS en décibels (valeur négative, 0dB = max).
    /// </summary>
    public float RmsDecibels { get; }

    /// <summary>
    ///     Pic d'amplitude - valeur absolue maximale (0-1).
    /// </summary>
    public float PeakAmplitude { get; }

    /// <summary>
    ///     Pic d'amplitude en décibels.
    /// </summary>
    public float PeakDecibels { get; }

    /// <summary>
    ///     Ratio de samples non-silencieux (0-1).
    /// </summary>
    public float NonSilenceRatio { get; }

    /// <summary>
    ///     Durée de l'audio en secondes.
    /// </summary>
    public float DurationSeconds { get; }

    /// <summary>
    ///     Nombre total de samples.
    /// </summary>
    public int SampleCount { get; }

    public AudioAnalysisResult(float rms, float rmsDecibels, float peakAmplitude, float peakDecibels,
        float nonSilenceRatio, float durationSeconds, int sampleCount)
    {
        Rms = rms;
        RmsDecibels = rmsDecibels;
        PeakAmplitude = peakAmplitude;
        PeakDecibels = peakDecibels;
        NonSilenceRatio = nonSilenceRatio;
        DurationSeconds = durationSeconds;
        SampleCount = sampleCount;
    }

    public override string ToString()
    {
        return $"RMS: {Rms:F4} ({RmsDecibels:F1}dB), Peak: {PeakAmplitude:F4} ({PeakDecibels:F1}dB), " +
               $"NonSilence: {NonSilenceRatio:P1}, Duration: {DurationSeconds:F2}s";
    }
}
