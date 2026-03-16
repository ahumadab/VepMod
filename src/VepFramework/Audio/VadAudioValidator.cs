using System;
using WebRtcVadSharp;

namespace VepMod.VepFramework.Audio;

/// <summary>
///     Valide les enregistrements audio en utilisant WebRTC VAD (Voice Activity Detection).
///     Utilise un modèle GMM (Gaussian Mixture Model) pour détecter la parole,
///     plus précis que les simples seuils d'énergie (RMS/Peak).
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

    /// <summary>
    ///     Valide un enregistrement audio en comptant les frames contenant de la parole.
    /// </summary>
    /// <param name="samples">Buffer audio normalisé [-1, 1]</param>
    /// <param name="sampleRate">Taux d'échantillonnage en Hz</param>
    /// <returns>Résultat de validation avec statistiques VAD</returns>
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

        // Convertir float[] en byte[] (PCM 16-bit)
        var pcmBytes = ConvertToPcm16(samples);

        // Calculer la taille d'une frame en bytes
        var targetSampleRate = GetTargetSampleRate(sampleRate);
        var frameSamples = GetFrameSamplesCount(targetSampleRate, _criteria.FrameLength);
        var frameBytesSize = frameSamples * 2; // 16-bit = 2 bytes par sample

        // Resampler si nécessaire
        if (sampleRate != (int)targetSampleRate)
        {
            pcmBytes = Resample(pcmBytes, sampleRate, (int)targetSampleRate);
        }

        // Compter les frames avec parole
        var totalFrames = pcmBytes.Length / frameBytesSize;
        if (totalFrames == 0)
        {
            return VadValidationResult.Rejected(VadRejectionReason.TooShort,
                new VadAnalysisResult(duration, 0, 0, 0f));
        }

        var speechFrames = 0;
        var frameBuffer = new byte[frameBytesSize];

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
            // Pour les autres sample rates, on resample vers 16kHz
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

        // 1) bytes -> short (PCM16 little-endian)
        var srcSamplesCount = pcmBytes.Length / 2;
        var src = new short[srcSamplesCount];

        for (int i = 0, bi = 0; i < srcSamplesCount; i++, bi += 2)
        {
            src[i] = (short)(pcmBytes[bi] | (pcmBytes[bi + 1] << 8));
        }

        // 2) Resample linéaire
        var step = (double)sourceSampleRate / targetSampleRate; // position avance en "samples source" par sample target
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

            // interpolation linéaire
            var sample = s0 + (s1 - s0) * frac;

            // clamp -> short
            if (sample > short.MaxValue)
            {
                sample = short.MaxValue;
            }
            else if (sample < short.MinValue) sample = short.MinValue;

            dst[i] = (short)sample;
            pos += step;

            // sécurité (évite idx+1 out of range en fin)
            if (pos >= srcSamplesCount - 1)
            {
                pos = srcSamplesCount - 1.000001;
            }
        }

        // 3) short -> bytes (PCM16 little-endian)
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

/// <summary>
///     Critères de validation VAD.
/// </summary>
public sealed class VadValidationCriteria
{
    /// <summary>
    ///     Durée minimale en secondes.
    /// </summary>
    public float MinDurationSeconds { get; set; } = 0.3f;

    /// <summary>
    ///     Ratio minimum de frames contenant de la parole (0-1).
    /// </summary>
    public float MinSpeechRatio { get; set; } = 0.1f;

    /// <summary>
    ///     Mode de détection VAD. Plus agressif = moins de faux positifs mais peut rater de la parole faible.
    /// </summary>
    public OperatingMode OperatingMode { get; set; } = OperatingMode.HighQuality;

    /// <summary>
    ///     Durée d'une frame d'analyse.
    /// </summary>
    public FrameLength FrameLength { get; set; } = FrameLength.Is20ms;

    /// <summary>
    ///     Sample rate cible pour l'analyse.
    /// </summary>
    public SampleRate SampleRate { get; set; } = SampleRate.Is16kHz;

    public static VadValidationCriteria Default => new()
    {
        MinDurationSeconds = 0.3f,
        MinSpeechRatio = 0.1f,
        OperatingMode = OperatingMode.HighQuality,
        FrameLength = FrameLength.Is20ms,
        SampleRate = SampleRate.Is16kHz
    };

    /// <summary>
    ///     Preset calibré par benchmark sur 248 fichiers WAV réels (180 parole, 68 bruit).
    ///     Meilleur F1=90% : precision 85%, recall 97%.
    ///     Note: MinDurationSeconds = 0 car le garde-fou durée est géré en amont par WhispralMimics
    ///     (lit ConfigAudioMinDuration pour rester cohérent avec la config utilisateur).
    /// </summary>
    public static VadValidationCriteria Production => new()
    {
        MinDurationSeconds = 0f,
        MinSpeechRatio = 0.40f,
        OperatingMode = OperatingMode.HighQuality,
        FrameLength = FrameLength.Is20ms,
        SampleRate = SampleRate.Is16kHz
    };

    public static VadValidationCriteria Strict => new()
    {
        MinDurationSeconds = 1f,
        MinSpeechRatio = 0.6f,
        OperatingMode = OperatingMode.Aggressive,
        FrameLength = FrameLength.Is20ms,
        SampleRate = SampleRate.Is16kHz
    };

    public static VadValidationCriteria Permissive => new()
    {
        MinDurationSeconds = 0.2f,
        MinSpeechRatio = 0.05f,
        OperatingMode = OperatingMode.HighQuality,
        FrameLength = FrameLength.Is20ms,
        SampleRate = SampleRate.Is16kHz
    };
}

/// <summary>
///     Résultat de l'analyse VAD.
/// </summary>
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

/// <summary>
///     Résultat de validation VAD.
/// </summary>
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

/// <summary>
///     Raisons de rejet VAD.
/// </summary>
public enum VadRejectionReason
{
    None,
    EmptyBuffer,
    TooShort,
    NotEnoughSpeech
}