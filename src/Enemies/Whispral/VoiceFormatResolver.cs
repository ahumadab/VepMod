using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using VepMod.VepFramework;

namespace VepMod.Enemies.Whispral;

/// <summary>
///     The microphone format of a Photon voice: the real format of the buffers pushed into
///     <c>LocalVoiceFramed&lt;short&gt;.PushDataAsync</c>.
///
///     Photon has two formats: the encoder's (<c>Info.SamplingRate</c> / <c>Info.Channels</c>, e.g. 48000 mono)
///     and the microphone's (<c>InputSamplingRate</c> / <c>InputChannels</c>, e.g. 44100 stereo). Captured data is
///     in the microphone format; labelling it with the encoder format distorts the voice on playback.
///
///     Which type declares the Input* properties varies across PhotonVoice versions, so they are read by
///     defensive reflection, like every internal Photon access in the mod, and cross-checked against
///     <c>FrameDurationUs</c>.
/// </summary>
public static class VoiceFormatResolver
{
    private const int MinPlausibleRate = 8000;
    private const int MaxPlausibleRate = 48000;

    private static readonly VepLogger LOG = VepLogger.Create("VoiceFormatResolver", true);

    // Per voice instance: the reflection runs once per voice.
    private static readonly ConditionalWeakTable<object, Format> Cache = new();

    /// <param name="voice">A <c>LocalVoiceFramed&lt;short&gt;</c>, passed as object to stay decoupled from it.</param>
    /// <param name="bufferLength">The current frame's buffer length, to derive the rate from the frame duration.</param>
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

        var inputRate = TryGetInt(voice, "InputSamplingRate");
        var inputChannels = TryGetInt(voice, "InputChannels");
        var forceChannels = TryGetInt(voice, "ForceChannels");
        var encoderRate = info != null ? TryGetInt(info, "SamplingRate") : null;
        var encoderChannels = info != null ? TryGetInt(info, "Channels") : null;
        var frameDurationUs = info != null ? TryGetInt(info, "FrameDurationUs") : null;

        var channels = FirstPlausibleChannels(inputChannels, forceChannels, encoderChannels) ?? 1;

        int sampleRate;
        string rateSource;
        if (IsPlausibleRate(inputRate))
        {
            sampleRate = inputRate!.Value;
            rateSource = "InputSamplingRate";
        }
        else if (frameDurationUs is > 0 && bufferLength > 0)
        {
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
    ///     Reads an int property or field, up the type hierarchy. Enums count: Photon's SamplingRate enum values are
    ///     the rates in Hz.
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
