using System;
using System.Collections;
using System.IO;
using UnityEngine;
using Random = UnityEngine.Random;

namespace VepMod.Enemies.Whispral;

public sealed partial class Hallucination
{
    // Found by HallucinationFace, which moves the head while a Mimic plays.
    public const string MimicEmitterName = "Mimic";

    // Playback, from the game's Voice prefab
    private const float MimicSpatialBlend = 1f;
    private const float MimicDopplerLevel = 0.5f;
    private const float MimicMinDistance = 5f;
    private const float MimicMaxDistance = 25f;

    private const float VoiceFilterProbability = 0.5f;

    private static bool _nextLungeFindsNoVoiceSample;

    private AudioSource? _lungeMimic;
    private float _mimicVoiceFrom;
    private float _mimicVoiceUntil;

    /// <summary>
    ///     Whether a Mimic's voice plays, without the silence that pads its clip.
    /// </summary>
    internal bool IsPlayingMimic => Time.time >= _mimicVoiceFrom && Time.time < _mimicVoiceUntil;

    /// <summary>
    ///     Whether a Mimic is voice-filtered, by the local voice filter option.
    /// </summary>
    internal static bool RollVoiceFilter()
    {
        return VepMod.ConfigVoiceFilterEnabled.Value && Random.value < VoiceFilterProbability;
    }

    /// <summary>
    ///     Dev tools only: the next Lunge, of any Hallucination, finds no Voice Sample.
    /// </summary>
    public static void DebugNextLungeFindsNoVoiceSample()
    {
        _nextLungeFindsNoVoiceSample = true;
    }

    // The Victim's client picks the Voice Sample itself, without a command from the Master (ADR-0007).
    private void PlayLungeMimic()
    {
        LOG.Debug($"STALK: {name} speaks a Mimic as it lunges");
        if (_nextLungeFindsNoVoiceSample)
        {
            _nextLungeFindsNoVoiceSample = false;
            _lungeMimic = null;
            LOG.Debug($"No Voice Sample in the Voice Bank of {ImitatedPlayer.VoiceBankKey()}: the dev tools withheld " +
                      "them for this Lunge");
        }
        else
        {
            _lungeMimic = PlayMimic(RollVoiceFilter());
        }

        if (_lungeMimic == null) LOG.Debug($"STALK: {name} lunges without a voice");
    }

    /// <returns>The Mimic's emitter, or null when nothing plays.</returns>
    public AudioSource? PlayMimic(bool voiceFilter)
    {
        if (ImitatedPlayer == null || ControllerTransform == null) return null;

        var player = ImitatedPlayer.VoiceBankKey();
        VoiceSample? sample;
        try
        {
            sample = VepMod.VoiceBanks.PickRandom(player);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            LOG.Warning($"Unreadable Voice Sample in the Voice Bank of {player}: {ex.Message}");
            return null;
        }

        if (sample == null)
        {
            LOG.Debug($"No Voice Sample in the Voice Bank of {player}");
            return null;
        }

        var samples = AudioFilters.ApplyLowPassFilter(sample.Samples, sample.SampleRate);
        var filter = "none";
        if (voiceFilter)
        {
            samples = ApplyRandomVoiceFilter(samples, sample.SampleRate, out filter);
        }

        samples = AudioFilters.ApplyFadeAndPadding(samples, sample.SampleRate);
        LOG.Debug($"Mimic of {ImitatedPlayer.playerName}: Voice Sample of {player} " +
                  $"({(float)sample.Samples.Length / sample.SampleRate:F2}s, {sample.SampleRate} Hz), voice filter {filter}");

        var clip = AudioClip.Create("Mimic", samples.Length, 1, sample.SampleRate, false);
        clip.SetData(samples, 0);
        return PlayOnNewEmitter(clip);
    }

    private static float[] ApplyRandomVoiceFilter(float[] samples, int sampleRate, out string filter)
    {
        switch (Random.Range(0, 3))
        {
            case 0:
                filter = "low pitch";
                return AudioFilters.ApplyPitchShift(samples, AudioFilters.PitchShiftLow);
            case 1:
                filter = "high pitch";
                return AudioFilters.ApplyPitchShift(samples, AudioFilters.PitchShiftHigh);
            default:
                filter = "alien";
                return AudioFilters.ApplyAlienFilter(samples, sampleRate);
        }
    }

    // Each Mimic gets its own emitter, so Mimics overlapping on one Hallucination never share an AudioSource.
    private AudioSource PlayOnNewEmitter(AudioClip clip)
    {
        var emitter = new GameObject(MimicEmitterName);
        emitter.transform.SetParent(ControllerTransform, false);

        var audioSource = emitter.AddComponent<AudioSource>();
        ConfigureMimicSource(audioSource, clip);

        // Wall occlusion, as on a real player's voice. Its RequireComponent adds the AudioLowPassFilter.
        var lowPassLogic = emitter.AddComponent<AudioLowPassLogic>();
        lowPassLogic.ForceStart = true;
        lowPassLogic.AlwaysActive = true;
        lowPassLogic.Fetch = true;
        lowPassLogic.Setup();

        audioSource.Play();
        var now = Time.time;
        if (now >= _mimicVoiceUntil) _mimicVoiceFrom = now + AudioFilters.SilencePadding;
        _mimicVoiceUntil = Mathf.Max(_mimicVoiceUntil, now + clip.length - AudioFilters.SilencePadding);
        var mimic = $"Mimic of {ImitatedPlayer.playerName} on Hallucination {GetInstanceID()}";
        LOG.Debug($"{mimic} started ({clip.length:F2}s)");

        var lifetime = clip.length + 0.1f;
        // Destroying the whole GameObject sidesteps Unity's refusal to remove a component another one requires.
        Destroy(emitter, lifetime);
        Destroy(clip, lifetime);
        // Run by the emitter, so a Mimic left in the world logs its end too.
        lowPassLogic.StartCoroutine(LogMimicEnd(mimic, clip.length));
        return audioSource;
    }

    private void ConfigureMimicSource(AudioSource source, AudioClip clip)
    {
        source.clip = clip;
        source.volume = VepMod.ConfigVoiceVolume.Value;
        source.spatialBlend = MimicSpatialBlend;
        source.dopplerLevel = MimicDopplerLevel;
        source.minDistance = MimicMinDistance;
        source.maxDistance = MimicMaxDistance;
        source.spread = 0f;
        source.priority = 0;
        source.bypassEffects = false;
        source.bypassListenerEffects = false;
        source.bypassReverbZones = false;

        // The Voice prefab's curve: full volume up to 20% of the distance, then down to 0.
        source.rolloffMode = AudioRolloffMode.Custom;
        source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, new AnimationCurve(
            new Keyframe(0.2f, 1f),
            new Keyframe(1f, 0f)
        ));

        if (ImitatedPlayer.voiceChat != null)
        {
            source.outputAudioMixerGroup = ImitatedPlayer.voiceChat.mixerMicrophoneSound;
        }
    }

    // Stops with the emitter: LogMimicsCutShort reports the Mimics a Hallucination cuts.
    private static IEnumerator LogMimicEnd(string mimic, float length)
    {
        yield return new WaitForSeconds(length);
        LOG.Debug($"{mimic} ended ({length:F2}s)");
    }

    // Unity refuses to reparent the child of a GameObject being destroyed: the emitter leaves before the Destroy call.
    private void LeaveLungeMimicInWorld()
    {
        if (_lungeMimic == null || !_lungeMimic.isPlaying) return;

        var emitter = _lungeMimic.transform;
        emitter.SetParent(null, true);
        LOG.Debug($"Mimic of {ImitatedPlayer.playerName} on Hallucination {GetInstanceID()} left in the world at " +
                  $"{emitter.position} as the Hallucination is removed, " +
                  $"{_lungeMimic.clip.length - _lungeMimic.time:F2}s still to play");
    }

    private void LogMimicsCutShort()
    {
        if (ControllerTransform == null) return;

        foreach (Transform child in ControllerTransform)
        {
            // An emitter outlives its clip by a moment: only a playing one is cut.
            var source = child.GetComponent<AudioSource>();
            if (child.name != MimicEmitterName || source == null || !source.isPlaying) continue;

            var imitatedPlayer = ImitatedPlayer != null ? ImitatedPlayer.playerName : "?";
            LOG.Debug($"Mimic of {imitatedPlayer} on Hallucination {GetInstanceID()} cut short: the Hallucination " +
                      "was destroyed");
        }
    }
}
