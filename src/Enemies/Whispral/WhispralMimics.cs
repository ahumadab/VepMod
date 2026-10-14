using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Photon.Pun;
using Unity.VisualScripting;
using UnityEngine;
using VepMod.VepFramework;
using VepMod.VepFramework.Extensions;
using Random = UnityEngine.Random;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

namespace VepMod.Enemies.Whispral;

/// <summary>
///     Saves, shares and plays Voice Samples for Mimics, in two loops:
///     - every client shares its player's Voice Samples with all the others;
///     - the Master tells a Victim's client which Hallucination performs a Mimic.
/// </summary>
public sealed class WhispralMimics : MonoBehaviour
{
    private static readonly VepLogger LOG = VepLogger.Create<WhispralMimics>(true);

    public PhotonView PhotonView { get; private set; }

    #region Unity Lifecycle

    private void Awake()
    {
        PhotonView = GetComponent<PhotonView>();
        if (!PhotonView)
        {
            LOG.Error("PhotonView not found.");
            return;
        }

        _playerAvatar = GetComponent<PlayerAvatar>();
        if (!_playerAvatar)
        {
            LOG.Error("PlayerAvatar not found.");
            return;
        }

        StartCoroutine(WaitForVoiceChat());
    }

    #endregion

    #region Constants

    // Network
    private const int ChunkSizeBytes = 8192;
    private const int ChunkDelayMs = 125;

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
#if DEBUG
    /// <summary>Shown on the DroidDevTools HUD.</summary>
    public string? LastCaptureVerdict { get; private set; }
#endif
    private string _localPlayerNickName;

    // Shared Voice Samples being received, per player
    private readonly Dictionary<string, List<byte[]?>> _receivedChunksByPlayer = new();
    private readonly Dictionary<string, int> _expectedChunkCountByPlayer = new();

    #endregion

    #region Initialization

    private IEnumerator WaitForVoiceChat()
    {
        while (!_playerAvatar.voiceChat)
        {
            yield return null;
        }

        LOG.Info("PlayerVoiceChat initialized.");

        // Every player's instance receives the sharing RPCs; only the local player's instance shares.
        if (SemiFunc.RunIsLevel())
        {
            _localPlayerNickName = PhotonNetwork.LocalPlayer.NickName ?? "unknown";

            if (PhotonView.IsMine)
            {
                LOG.Info($"WhispralMimics initialized for local player: {_localPlayerNickName}");

                StartCoroutine(ShareAudioLoop());
                StartCoroutine(SaveCaptures());
            }
            else
            {
                LOG.Info("WhispralMimics initialized for remote player (RPC reception only).");
            }
        }
    }

    #endregion

    #region Capture validation

    // Photon's voice thread only fills the recorder; the Captures are validated and saved off the main thread.
    private IEnumerator SaveCaptures()
    {
        var player = _playerAvatar.VoiceBankKey();
        while (true)
        {
            if (!VepMod.VoiceRecorder.TryTakeCapture(out var capture))
            {
                yield return null;
                continue;
            }

            var minDuration = VepMod.ConfigAudioMinDuration.Value;
            var saving = Task.Run(() => ValidateAndSave(capture, player, minDuration));
            while (!saving.IsCompleted)
            {
                yield return null;
            }

            var (verdict, wav) = saving.Result;
#if DEBUG
            LastCaptureVerdict = verdict;
#endif
            if (wav != null)
            {
                yield return ShareAudioWithOthersAsync(wav, capture.Sample.SampleRate).AsCoroutine();
            }
        }
        // ReSharper disable once IteratorNeverReturns
    }

    /// <summary>Runs in a background task.</summary>
    /// <returns>The verdict for the HUD, and the WAV to share, null when the Capture is not a Voice Sample.</returns>
    private static (string verdict, byte[]? wav) ValidateAndSave(Capture capture, PlayerKey player, float minDuration)
    {
        var (accepted, reason) = CaptureValidation.Validate(capture, minDuration, VepMod.SpeechCheck);
        var verdict = $"{(accepted ? "accepted" : "rejected")} " +
                      $"({capture.DurationSeconds:F2} s at {capture.Sample.SampleRate} Hz): {reason}";
        if (!accepted)
        {
            LOG.Debug($"Capture {verdict}");
            return (verdict, null);
        }

        try
        {
            VepMod.VoiceBanks.Save(player, capture.Sample);
            var wav = WavCodec.Encode(capture.Sample);
            LOG.Debug($"Capture {verdict}. Voice Sample saved in the Voice Bank of {player}.");
            return (verdict, wav);
        }
        catch (Exception ex)
        {
            LOG.Error($"Error saving the Voice Sample of {player}: {ex.Message}");
            return (verdict, null);
        }
    }

    #endregion

    #region Voice Sample sharing

    private IEnumerator ShareAudioLoop()
    {
        LOG.Info("ShareAudioLoop started.");

        while (true)
        {
            var delay = Random.Range(VepMod.ShareDelay.Min, VepMod.ShareDelay.Max);
            yield return new WaitForSeconds(delay);

            VepMod.VoiceRecorder.RequestCapture();
        }
        // ReSharper disable once IteratorNeverReturns
    }

    private async Task ShareAudioWithOthersAsync(byte[] audioData, int sampleRate)
    {
        if (!PhotonNetwork.IsConnectedAndReady)
        {
            LOG.Warning("Photon not connected, skipping share.");
            return;
        }

        var chunks = ChunkAudioData(audioData);
        LOG.Debug($"Voice Sample handed to sharing: {audioData.Length} bytes in {chunks.Count} chunks");

        for (var i = 0; i < chunks.Count; i++)
        {
            if (!PhotonNetwork.IsConnectedAndReady)
            {
                LOG.Warning("Photon disconnected, aborting share.");
                return;
            }

            PhotonView.RPC(nameof(ReceiveSharedAudioChunk), RpcTarget.Others,
                chunks[i], i, chunks.Count, _localPlayerNickName, sampleRate);

            await Task.Delay(ChunkDelayMs);
        }

        LOG.Debug($"Audio shared successfully: {chunks.Count} chunks sent.");
    }

    private static List<byte[]> ChunkAudioData(byte[] audioData)
    {
        var chunks = new List<byte[]>();
        for (var offset = 0; offset < audioData.Length; offset += ChunkSizeBytes)
        {
            var length = Mathf.Min(ChunkSizeBytes, audioData.Length - offset);
            var chunk = new byte[length];
            Array.Copy(audioData, offset, chunk, 0, length);
            chunks.Add(chunk);
        }

        return chunks;
    }

    [PunRPC]
    public void ReceiveSharedAudioChunk(byte[] chunk, int chunkIndex, int totalChunks, string sourcePlayerNickName,
        int srcSampleRate)
    {
        // Sent to RpcTarget.Others, so this is only a safety net.
        if (sourcePlayerNickName == _localPlayerNickName) return;

        if (chunkIndex == 0)
        {
            _receivedChunksByPlayer[sourcePlayerNickName] = new List<byte[]?>();
            _expectedChunkCountByPlayer[sourcePlayerNickName] = totalChunks;
            LOG.Debug(
                $"Receiving shared audio from {sourcePlayerNickName}: {totalChunks} chunks at {srcSampleRate} Hz");
        }

        if (!_receivedChunksByPlayer.TryGetValue(sourcePlayerNickName, out var chunks))
        {
            LOG.Warning($"Received chunk for unknown player: {sourcePlayerNickName}");
            return;
        }

        while (chunks.Count <= chunkIndex)
        {
            chunks.Add(null);
        }

        chunks[chunkIndex] = chunk;

        var expectedCount = _expectedChunkCountByPlayer.GetValueOrDefault(sourcePlayerNickName, 0);
        if (chunks.Count >= expectedCount && chunks.All(c => c != null))
        {
            LOG.Debug($"All chunks received from {sourcePlayerNickName}, saving audio.");

            var combinedData = CombineChunks(chunks!);
            SaveReceivedAudioAsync(combinedData);

            _receivedChunksByPlayer.Remove(sourcePlayerNickName);
            _expectedChunkCountByPlayer.Remove(sourcePlayerNickName);
        }
    }

    private static byte[] CombineChunks(List<byte[]> chunks)
    {
        var totalLength = chunks.Sum(c => c.Length);
        var result = new byte[totalLength];
        var offset = 0;

        foreach (var chunk in chunks)
        {
            Array.Copy(chunk, 0, result, offset, chunk.Length);
            offset += chunk.Length;
        }

        return result;
    }

    // The sender of a Voice Sample is the owner of the avatar whose PhotonView it arrives on (ADR-0006).
    private async void SaveReceivedAudioAsync(byte[] wav)
    {
        var player = _playerAvatar.VoiceBankKey();
        try
        {
            await Task.Run(() => VepMod.VoiceBanks.Save(player, WavCodec.Decode(wav)));
            LOG.Debug($"Voice Sample received from {player} saved in their Voice Bank.");
        }
        catch (Exception ex)
        {
            LOG.Error($"Error saving the Voice Sample received from {player}: {ex.Message}");
        }
    }

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
