using System;
using System.Collections;
using System.IO;
using Photon.Pun;
using UnityEngine;
using VepMod.VepFramework;
using Random = UnityEngine.Random;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

namespace VepMod.Enemies.Whispral;

/// <summary>
///     Carries the Master's Mimic command to a Victim's client and plays the Mimic on the Hallucination.
/// </summary>
public sealed class WhispralMimics : MonoBehaviour
{
    private static readonly VepLogger LOG = VepLogger.Create<WhispralMimics>(true);

    public PhotonView PhotonView { get; private set; }

    #region Unity Lifecycle

    private void Awake()
    {
        PhotonView = GetComponent<PhotonView>();
        _playerAvatar = GetComponent<PlayerAvatar>();
    }

    #endregion

    #region Constants

    // Playback, from the game's Voice prefab
    private const float SpatialBlend = 1f;
    private const float DopplerLevel = 0.5f;
    private const float MinDistance = 5f;
    private const float MaxDistance = 25f;

    /// <summary>Name of the child GameObject that carries one Mimic's AudioSource under its Hallucination.</summary>
    public const string MimicEmitterName = "Mimic";

    #endregion

    #region Private Fields

    private PlayerAvatar _playerAvatar;

    #endregion

    #region Mimic commands

    /// <summary>
    ///     Sent by the Master from EnemyWhispral.AttachedState; only the Victim's client acts on it.
    /// </summary>
    [PunRPC]
    public void PlayVoiceCommandRPC(int targetViewID, string imitatedPlayerNickName, bool applyFilter)
    {
        var localPlayer = PlayerAvatar.instance;
        if (!localPlayer || localPlayer.photonView.ViewID != targetViewID)
        {
            return;
        }

        var imitations = localPlayer.GetComponent<Imitations>();
        var hallucination = imitations ? imitations.FindHallucination(imitatedPlayerNickName) : null;
        if (hallucination)
        {
            LOG.Debug($"Playing voice from {imitatedPlayerNickName} on their Hallucination");
            hallucination!.PlayVoice(applyFilter);
        }
    }

    public void SendPlayVoiceCommand(int targetViewID, string imitatedPlayerNickName, bool applyFilter)
    {
        if (!PhotonNetwork.IsConnectedAndReady)
        {
            LOG.Warning("Photon not connected, cannot send voice command.");
            return;
        }

        PhotonView.RPC(nameof(PlayVoiceCommandRPC), RpcTarget.All, targetViewID, imitatedPlayerNickName, applyFilter);
    }

    #endregion

    #region Audio Processing & Playback

    private float[] ProcessReceivedAudio(float[] samples, bool applyVoiceFilter, int receivedSampleRate)
    {
        samples = AudioFilters.ApplyLowPassFilter(samples, receivedSampleRate);

        if (applyVoiceFilter)
        {
            samples = ApplyRandomVoiceFilter(samples, receivedSampleRate);
        }

        return AudioFilters.ApplyFadeAndPadding(samples, receivedSampleRate);
    }

    private float[] ApplyRandomVoiceFilter(float[] samples, int receivedSampleRate)
    {
        var filterType = Random.Range(0, 3);
        return filterType switch
        {
            0 => AudioFilters.ApplyPitchShift(samples, AudioFilters.PitchShiftLow),
            1 => AudioFilters.ApplyPitchShift(samples, AudioFilters.PitchShiftHigh),
            2 => AudioFilters.ApplyAlienFilter(samples, receivedSampleRate),
            _ => samples
        };
    }

    public void PlayAudioAtTransform(Transform hallucination, PlayerAvatar imitatedPlayer, bool applyFilter)
    {
        if (hallucination == null)
        {
            LOG.Warning("PlayAudioAtTransform: hallucination is null");
            return;
        }

        var player = imitatedPlayer.VoiceBankKey();
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

        LOG.Debug($"Picked a Voice Sample of {player} ({sample.Samples.Length} samples, {sample.SampleRate} Hz)");
        var processedSamples = ProcessReceivedAudio(sample.Samples, applyFilter, sample.SampleRate);
        var audioClip = AudioClip.Create("HallucinationClip", processedSamples.Length, 1, sample.SampleRate, false);
        audioClip.SetData(processedSamples, 0);

        PlayAtTransform(hallucination, audioClip, imitatedPlayer.playerName);
    }

    // Each Mimic gets its own emitter, so Mimics overlapping on one Hallucination never share an AudioSource. Parented
    // to the Hallucination, the emitter follows it and goes with it if it is destroyed first.
    private void PlayAtTransform(Transform hallucination, AudioClip clip, string imitatedPlayerNickName)
    {
        var emitter = new GameObject(MimicEmitterName);
        emitter.transform.SetParent(hallucination, false);

        var audioSource = emitter.AddComponent<AudioSource>();
        ConfigureAudioSource(audioSource, clip);

        // Wall occlusion, as on a real player's voice. Its RequireComponent adds the AudioLowPassFilter.
        var lowPassLogic = emitter.AddComponent<AudioLowPassLogic>();
        lowPassLogic.ForceStart = true;
        lowPassLogic.AlwaysActive = true;
        lowPassLogic.Fetch = true;
        lowPassLogic.Setup();

        audioSource.Play();

        var mimic = $"{imitatedPlayerNickName} on Hallucination {hallucination.GetInstanceID()}";
        LOG.Debug($"Mimic of {mimic} started ({clip.length:F2}s)");

        var lifetime = clip.length + 0.1f;
        // Destroying the whole GameObject sidesteps Unity's refusal to remove a component another one requires.
        Destroy(emitter, lifetime);
        Destroy(clip, lifetime);
        StartCoroutine(LogMimicEnd(emitter, mimic, clip.length));
    }

    private void ConfigureAudioSource(AudioSource source, AudioClip clip)
    {
        source.clip = clip;
        source.volume = VepMod.ConfigVoiceVolume.Value;
        source.spatialBlend = SpatialBlend;
        source.dopplerLevel = DopplerLevel;
        source.minDistance = MinDistance;
        source.maxDistance = MaxDistance;
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

        source.outputAudioMixerGroup = _playerAvatar.voiceChat.mixerMicrophoneSound;
    }


    private static IEnumerator LogMimicEnd(GameObject emitter, string mimic, float length)
    {
        yield return new WaitForSeconds(length);
        LOG.Debug(emitter != null
            ? $"Mimic of {mimic} ended ({length:F2}s)"
            : $"Mimic of {mimic} cut short: its Hallucination was destroyed");
    }

    #endregion
}
