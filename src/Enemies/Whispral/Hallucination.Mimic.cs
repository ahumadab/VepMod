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

    private float _mimicVoiceFrom;
    private float _mimicVoiceUntil;

    /// <summary>
    ///     Whether a Mimic's voice plays, without the silence that pads its clip.
    /// </summary>
    internal bool IsPlayingMimic => Time.time >= _mimicVoiceFrom && Time.time < _mimicVoiceUntil;

    public void PlayMimic(bool voiceFilter)
    {
        if (ImitatedPlayer == null || ControllerTransform == null) return;

        var player = ImitatedPlayer.VoiceBankKey();
        VoiceSample? sample;
        try
        {
            sample = VepMod.VoiceBanks.PickRandom(player);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            LOG.Warning($"Unreadable Voice Sample in the Voice Bank of {player}: {ex.Message}");
            return;
        }

        if (sample == null)
        {
            LOG.Debug($"No Voice Sample in the Voice Bank of {player}");
            return;
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
        PlayOnNewEmitter(clip);
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
    private void PlayOnNewEmitter(AudioClip clip)
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
        LOG.Debug($"Mimic of {ImitatedPlayer.playerName} on Hallucination {GetInstanceID()} started ({clip.length:F2}s)");

        var lifetime = clip.length + 0.1f;
        // Destroying the whole GameObject sidesteps Unity's refusal to remove a component another one requires.
        Destroy(emitter, lifetime);
        Destroy(clip, lifetime);
        StartCoroutine(LogMimicEnd(clip.length));
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

    // Stops with the Hallucination: LogMimicsCutShort reports the Mimics it cuts.
    private IEnumerator LogMimicEnd(float length)
    {
        yield return new WaitForSeconds(length);
        LOG.Debug($"Mimic of {ImitatedPlayer.playerName} on Hallucination {GetInstanceID()} ended ({length:F2}s)");
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
