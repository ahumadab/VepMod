using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using VepMod.VepFramework;

namespace VepMod.Patchs;

/// <summary>
///     Résout le format AUDIO SOURCE (micro) d'une voix Photon, c'est-à-dire le format réel des
///     buffers poussés dans <c>LocalVoiceFramed&lt;short&gt;.PushDataAsync</c>.
///
///     Photon expose deux formats : l'encodeur/transmission (<c>Info.SamplingRate</c> /
///     <c>Info.Channels</c>, ex. 48000 mono) et la source (<c>InputSamplingRate</c> /
///     <c>InputChannels</c>, ex. 44100 stéréo = le vrai format du micro). Les données capturées
///     sont au format SOURCE ; les étiqueter avec le format encodeur déforme la voix à la relecture.
///
///     Le type déclarant exact des propriétés Input* n'étant pas garanti selon la version de
///     PhotonVoice, on procède par réflexion défensive (pattern déjà utilisé pour tout l'accès
///     interne Photon du mod), avec recoupement via <c>FrameDurationUs</c> et garde-fous.
/// </summary>
public static class VoiceFormatResolver
{
    private const int MinPlausibleRate = 8000;
    private const int MaxPlausibleRate = 48000;

    private static readonly VepLogger LOG = VepLogger.Create("VoiceFormatResolver", true);

    // Cache par instance de voix : la réflexion ne s'exécute qu'une fois par voix.
    private static readonly ConditionalWeakTable<object, Format> Cache = new();

    /// <summary>
    ///     Renvoie le format source (sampleRate, channels) de la voix, mis en cache par instance.
    /// </summary>
    /// <param name="voice">L'instance <c>LocalVoiceFramed&lt;short&gt;</c> (passée en object pour découpler).</param>
    /// <param name="bufferLength">Taille du buffer de la frame courante (sert au recoupement temps réel).</param>
    public static (int sampleRate, int channels) Resolve(object voice, int bufferLength)
    {
        if (voice == null)
        {
            return (MaxPlausibleRate, 1);
        }

        if (Cache.TryGetValue(voice, out var cached))
        {
            return (cached.SampleRate, cached.Channels);
        }

        var format = ResolveUncached(voice, bufferLength);
        Cache.Add(voice, format);
        return (format.SampleRate, format.Channels);
    }

    private static Format ResolveUncached(object voice, int bufferLength)
    {
        object? info = null;
        try
        {
            info = GetMemberValue(voice, "Info");
        }
        catch (Exception ex)
        {
            LOG.Warning($"Could not read voice Info: {ex.Message}");
        }

        // --- Candidats bruts (pour diagnostic + résolution) ---
        var inputRate = TryGetInt(voice, "InputSamplingRate");
        var inputChannels = TryGetInt(voice, "InputChannels");
        var forceChannels = TryGetInt(voice, "ForceChannels");
        var encoderRate = info != null ? TryGetInt(info, "SamplingRate") : null;
        var encoderChannels = info != null ? TryGetInt(info, "Channels") : null;
        var frameDurationUs = info != null ? TryGetInt(info, "FrameDurationUs") : null;

        // --- Channels source : priorité Input, sinon Force, sinon encodeur, sinon mono ---
        var channels = FirstPlausibleChannels(inputChannels, forceChannels, encoderChannels) ?? 1;

        // --- SampleRate source : Input, sinon dérivé du temps réel, sinon encodeur ---
        int sampleRate;
        string rateSource;
        if (IsPlausibleRate(inputRate))
        {
            sampleRate = inputRate!.Value;
            rateSource = "InputSamplingRate";
        }
        else if (frameDurationUs is > 0 && bufferLength > 0)
        {
            // rate = (samples par canal) / (durée de frame en secondes)
            var perChannel = (double)bufferLength / channels;
            var derived = (int)Math.Round(perChannel * 1_000_000d / frameDurationUs.Value);
            if (IsPlausibleRate(derived))
            {
                sampleRate = derived;
                rateSource = "derived(FrameDurationUs)";
            }
            else
            {
                sampleRate = FallbackRate(encoderRate, out rateSource);
            }
        }
        else
        {
            sampleRate = FallbackRate(encoderRate, out rateSource);
        }

        LOG.Info(
            $"Voice source format resolved: {sampleRate} Hz, {channels} ch (via {rateSource}). " +
            $"Candidates: InputSamplingRate={Fmt(inputRate)}, InputChannels={Fmt(inputChannels)}, " +
            $"ForceChannels={Fmt(forceChannels)}, encoder={Fmt(encoderRate)}Hz/{Fmt(encoderChannels)}ch, " +
            $"FrameDurationUs={Fmt(frameDurationUs)}, bufferLength={bufferLength}.");

        return new Format(sampleRate, channels);
    }

    private static int FallbackRate(int? encoderRate, out string source)
    {
        if (IsPlausibleRate(encoderRate))
        {
            source = "Info.SamplingRate(fallback)";
            return encoderRate!.Value;
        }

        source = "default(fallback)";
        return MaxPlausibleRate;
    }

    private static int? FirstPlausibleChannels(params int?[] candidates)
    {
        foreach (var c in candidates)
        {
            if (c is >= 1 and <= 8)
            {
                return c;
            }
        }

        return null;
    }

    private static bool IsPlausibleRate(int? rate)
    {
        return rate is >= MinPlausibleRate and <= MaxPlausibleRate;
    }

    /// <summary>
    ///     Lit une propriété/champ entier (ou enum, dont la valeur sous-jacente est l'entier voulu —
    ///     ex. l'enum SamplingRate de Photon vaut directement les Hz) en remontant la hiérarchie.
    /// </summary>
    private static int? TryGetInt(object obj, string name)
    {
        try
        {
            var value = GetMemberValue(obj, name);
            return value switch
            {
                null => null,
                int i => i,
                Enum => Convert.ToInt32(value),
                _ when value is IConvertible => Convert.ToInt32(value),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static object? GetMemberValue(object obj, string name)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        for (var type = obj.GetType(); type != null; type = type.BaseType)
        {
            var prop = type.GetProperty(name, flags | BindingFlags.DeclaredOnly);
            if (prop != null && prop.CanRead)
            {
                return prop.GetValue(obj);
            }

            var field = type.GetField(name, flags | BindingFlags.DeclaredOnly);
            if (field != null)
            {
                return field.GetValue(obj);
            }
        }

        return null;
    }

    private static string Fmt(int? value)
    {
        return value?.ToString() ?? "n/a";
    }

    private sealed class Format
    {
        public Format(int sampleRate, int channels)
        {
            SampleRate = sampleRate;
            Channels = channels;
        }

        public int SampleRate { get; }
        public int Channels { get; }
    }
}
