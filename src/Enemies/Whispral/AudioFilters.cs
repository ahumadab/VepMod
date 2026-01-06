using UnityEngine;

namespace VepMod.Enemies.Whispral;

/// <summary>
/// Filtres audio pour la modification de voix.
/// </summary>
public static class AudioFilters
{
    // Constantes mathématiques
    private const float TwoPi = 2f * Mathf.PI;

    // Constantes de conversion audio
    private const float Int16MaxValue = 32768f;

    // Paramètres du filtre Alien
    private const float AlienVibratoFrequency = 5f;
    private const float AlienVibratoDepth = 0.05f;
    private const float AlienRingModFrequency = 200f;
    private const float AlienRingModMix = 0.3f;

    // Paramètres de pitch shift
    public const float PitchShiftLow = 0.5f;
    public const float PitchShiftHigh = 1.2f;

    // Paramètres du filtre passe-bas
    private const float DefaultLowPassCutoff = 4500f;

    /// <summary>
    /// Applique un filtre "alien" avec vibrato et ring modulation.
    /// </summary>
    public static float[] ApplyAlienFilter(float[] samples, int sampleRate)
    {
        var result = new float[samples.Length];

        for (var i = 0; i < samples.Length; i++)
        {
            var time = i / (float)sampleRate;

            // Vibrato (modulation de la position de lecture)
            var vibratoOffset = 1f + Mathf.Sin(TwoPi * AlienVibratoFrequency * time) * AlienVibratoDepth;
            var readPosition = i * vibratoOffset;
            var readIndex = (int)readPosition;
            var fraction = readPosition - readIndex;

            // Interpolation linéaire pour le vibrato
            float vibratoSample;
            if (readIndex + 1 < samples.Length)
            {
                vibratoSample = samples[readIndex] * (1f - fraction) + samples[readIndex + 1] * fraction;
            }
            else if (readIndex < samples.Length)
            {
                vibratoSample = samples[readIndex];
            }
            else
            {
                vibratoSample = 0f;
            }

            // Ring modulation
            var ringMod = Mathf.Sin(TwoPi * AlienRingModFrequency * time);
            var ringModSample = vibratoSample * ringMod * AlienRingModMix;

            // Mix final
            result[i] = Mathf.Clamp(vibratoSample * (1f - AlienRingModMix) + ringModSample, -1f, 1f);
        }

        return result;
    }

    /// <summary>
    /// Applique un filtre passe-bas bidirectionnel (forward-backward).
    /// </summary>
    public static float[] ApplyLowPassFilter(float[] samples, int sampleRate, float cutoffFrequency = DefaultLowPassCutoff)
    {
        var rc = 1f / (TwoPi * cutoffFrequency);
        var dt = 1f / sampleRate;
        var alpha = dt / (rc + dt);

        // Forward pass
        var forward = new float[samples.Length];
        forward[0] = samples[0];
        for (var i = 1; i < samples.Length; i++)
        {
            forward[i] = forward[i - 1] + alpha * (samples[i] - forward[i - 1]);
        }

        // Backward pass (pour un filtrage sans déphasage)
        var result = new float[samples.Length];
        result[samples.Length - 1] = forward[samples.Length - 1];
        for (var i = samples.Length - 2; i >= 0; i--)
        {
            result[i] = result[i + 1] + alpha * (forward[i] - result[i + 1]);
        }

        return result;
    }

    /// <summary>
    /// Applique un pitch shift par rééchantillonnage.
    /// </summary>
    public static float[] ApplyPitchShift(float[] samples, float pitchFactor)
    {
        var newLength = (int)(samples.Length / pitchFactor);
        var result = new float[newLength];

        for (var i = 0; i < newLength; i++)
        {
            var readPosition = i * pitchFactor;
            var readIndex = (int)readPosition;
            var fraction = readPosition - readIndex;

            if (readIndex + 1 < samples.Length)
            {
                result[i] = samples[readIndex] * (1f - fraction) + samples[readIndex + 1] * fraction;
            }
            else if (readIndex < samples.Length)
            {
                result[i] = samples[readIndex];
            }
        }

        return result;
    }

    /// <summary>
    /// Convertit un tableau de bytes (PCM 16-bit) en floats normalisés [-1, 1].
    /// </summary>
    public static float[] ConvertBytesToFloats(byte[] byteArray)
    {
        var length = byteArray.Length / 2;
        var result = new float[length];

        for (var i = 0; i < length; i++)
        {
            var sample = (short)(byteArray[i * 2] | (byteArray[i * 2 + 1] << 8));
            result[i] = sample / Int16MaxValue;
        }

        return result;
    }

    /// <summary>
    /// Convertit un tableau de floats normalisés en bytes (PCM 16-bit).
    /// </summary>
    public static byte[] ConvertFloatsToBytes(float[] floatArray)
    {
        var result = new byte[floatArray.Length * 2];

        for (var i = 0; i < floatArray.Length; i++)
        {
            var sample = (short)(floatArray[i] * short.MaxValue);
            result[i * 2] = (byte)(sample & 0xFF);
            result[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
        }

        return result;
    }

    /// <summary>
    /// Applique un fade-in/fade-out et ajoute du padding silence.
    /// </summary>
    public static float[] ApplyFadeAndPadding(float[] samples, int sampleRate)
    {
        const float paddingDuration = 0.5f;
        const float fadeDuration = 0.02f;

        var paddingSamples = (int)(sampleRate * paddingDuration);
        var fadeSamples = (int)(sampleRate * fadeDuration);
        var result = new float[samples.Length + 2 * paddingSamples];

        // Silence au début
        for (var i = 0; i < paddingSamples; i++)
        {
            result[i] = 0f;
        }

        // Audio avec fade
        for (var i = 0; i < samples.Length; i++)
        {
            var fadeMultiplier = 1f;

            if (i < fadeSamples)
            {
                fadeMultiplier = i / (float)fadeSamples; // Fade-in
            }
            else if (i >= samples.Length - fadeSamples)
            {
                fadeMultiplier = (samples.Length - i) / (float)fadeSamples; // Fade-out
            }

            result[i + paddingSamples] = samples[i] * fadeMultiplier;
        }

        // Silence à la fin
        for (var i = samples.Length + paddingSamples; i < result.Length; i++)
        {
            result[i] = 0f;
        }

        return result;
    }
}
