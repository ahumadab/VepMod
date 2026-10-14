using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Photon.Pun;
using Unity.VisualScripting;
using UnityEngine;
using VepMod.VepFramework;
#if !VEPMOD_NO_VAD
using VepMod.VepFramework.Audio;
#endif
using VepMod.VepFramework.Extensions;
using Random = UnityEngine.Random;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

namespace VepMod.Enemies.Whispral;

/// <summary>
///     Records, shares and plays Voice Samples for Mimics, in two loops:
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

        var playerAvatar = GetComponent<PlayerAvatar>();
        if (!playerAvatar)
        {
            LOG.Error("PlayerAvatar not found.");
            return;
        }

        if (!InitializeReflectionFields())
        {
            return;
        }

        _sampleRate = VepMod.ConfigSamplingRate.Value;

        StartCoroutine(WaitForVoiceChat(playerAvatar));
    }

    private void OnDestroy()
    {
#if !VEPMOD_NO_VAD
        _vadValidator?.Dispose();
#endif
    }

    #endregion

    #region Constants

    // Recording
    private const int AudioBufferDurationSeconds = 6;
    private const float SilenceTimeoutSeconds = 0.5f;

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

    private PlayerVoiceChat _playerVoiceChat;
    private WavFileManager _wavFileManager;
#if !VEPMOD_NO_VAD
    private VadAudioValidator? _vadValidator;
#if DEBUG
    /// <summary>Shown on the DroidDevTools HUD.</summary>
    public VadValidationResult? LastVadResult { get; private set; }
#endif
#endif
    private string _localPlayerNickName;

    // Reflection fields
    private FieldInfo? _voiceChatField;
    private FieldInfo _recorderField;
    private FieldInfo _isTalkingField;

    // Recording
    private float[]? _audioBuffer;
    private int _bufferPosition;
    private int _sampleRate;
    private int _channelCount = 1;
    private bool _isRecording;
    private bool _capturingSpeech;
    private bool _fileSaved;
    private float _silenceTimer;
    private byte[]? _lastSavedAudioBytes;
    private bool _hasNewRecording;

    // Shared Voice Samples being received, per player
    private readonly Dictionary<string, List<byte[]?>> _receivedChunksByPlayer = new();
    private readonly Dictionary<string, int> _expectedChunkCountByPlayer = new();

    #endregion

    #region Initialization

    private bool InitializeReflectionFields()
    {
        _voiceChatField = typeof(PlayerAvatar).GetField("voiceChat", BindingFlags.Instance | BindingFlags.NonPublic);
        if (_voiceChatField == null)
        {
            LOG.Error("Could not find 'voiceChat' field in PlayerAvatar.");
            return false;
        }

        _recorderField = typeof(PlayerVoiceChat).GetField("recorder", BindingFlags.Instance | BindingFlags.NonPublic);
        if (_recorderField == null)
        {
            LOG.Error("Could not find 'recorder' field in PlayerVoiceChat.");
            return false;
        }

        _isTalkingField = typeof(PlayerVoiceChat).GetField("isTalking", BindingFlags.Instance | BindingFlags.NonPublic);
        if (_isTalkingField == null)
        {
            LOG.Error("Could not find 'isTalking' field in PlayerVoiceChat.");
            return false;
        }

        return true;
    }

    private IEnumerator WaitForVoiceChat(PlayerAvatar playerAvatar)
    {
        while (!_playerVoiceChat)
        {
            if (_voiceChatField == null)
            {
                yield break;
            }

            _playerVoiceChat = (PlayerVoiceChat)_voiceChatField.GetValue(playerAvatar);
            yield return null;
        }

        LOG.Info("PlayerVoiceChat initialized.");
        DetectSampleRate();

        // Every player's instance receives the sharing RPCs, so each one needs the Voice Banks; only the local
        // player's instance shares.
        if (SemiFunc.RunIsLevel())
        {
            _wavFileManager = new WavFileManager(VepMod.ConfigSamplesPerPlayer.Value);
            _localPlayerNickName = PhotonNetwork.LocalPlayer.NickName ?? "unknown";

#if !VEPMOD_NO_VAD
            if (VepMod.ConfigVadEnabled.Value)
            {
                try
                {
                    var sensitivity = VepMod.ConfigVadSensitivity.Value;
                    var criteria = VadValidationCriteria.FromSensitivity(sensitivity);
                    _vadValidator = new VadAudioValidator(criteria);
                    LOG.Info($"VAD validation enabled (sensitivity={sensitivity}, speechRatio>={criteria.MinSpeechRatio:F2}).");
                }
                catch (Exception ex)
                {
                    LOG.Warning($"VAD initialization failed, recordings will not be filtered: {ex.Message}");
                    _vadValidator = null;
                }
            }
#endif

            if (PhotonView.IsMine)
            {
                LOG.Info($"WhispralMimics initialized for local player: {_localPlayerNickName}");

                StartCoroutine(ShareAudioLoop());
            }
            else
            {
                LOG.Info("WhispralMimics initialized for remote player (RPC reception only).");
            }
        }
    }

    private void DetectSampleRate()
    {
        var recorder = _recorderField.GetValue(_playerVoiceChat);
        if (recorder != null)
        {
            var recorderType = recorder.GetType();
            var sampleRateProp = recorderType.GetProperty("SamplingRate")
                                 ?? recorderType.GetProperty("RecordingSampleRate")
                                 ?? recorderType.GetProperty("SamplingRateOverride");

            if (sampleRateProp != null)
            {
                var value = sampleRateProp.GetValue(recorder);
                if (value is int sr and > 0)
                {
                    _sampleRate = sr;
                    LOG.Debug($"Detected sample rate: {_sampleRate} Hz");
                    return;
                }

                if (value != null && value.GetType().IsEnum)
                {
                    _sampleRate = (int)value;
                    LOG.Debug($"Detected enum sample rate: {_sampleRate} Hz");
                    return;
                }
            }
        }

        if (_sampleRate <= 0)
        {
            _sampleRate = AudioSettings.outputSampleRate;
            LOG.Warning($"Fallback to AudioSettings.outputSampleRate: {_sampleRate} Hz");
        }
    }

    #endregion

    #region Recording

    public void ProcessVoiceData(short[] voiceData, int frameSampleRate, int channels)
    {
        if (!_isRecording || !PhotonView.IsMine)
        {
            return;
        }

        var isTalking = (bool)_isTalkingField.GetValue(_playerVoiceChat);
        if (isTalking && !_capturingSpeech)
        {
            _capturingSpeech = true;
            _bufferPosition = 0;
            _fileSaved = false;
            _silenceTimer = 0f;
            // Photon's own format, more reliable than inferring it from the frame length.
            if (frameSampleRate > 0) _sampleRate = frameSampleRate;
            _channelCount = channels >= 1 ? channels : 1;
            LOG.Debug($"Speech detected, capturing audio ({_sampleRate} Hz, {_channelCount} ch, frame {voiceData.Length}).");
        }

        if (!_capturingSpeech)
        {
            return;
        }

        EnsureBufferAllocated();
        CopyVoiceDataToBuffer(voiceData);

        if (isTalking)
        {
            _silenceTimer = 0f;
        }
        else
        {
            var frameDurationSec = _sampleRate > 0 ? (float)(voiceData.Length / _channelCount) / _sampleRate : 0f;
            _silenceTimer += frameDurationSec;
            if (_silenceTimer >= SilenceTimeoutSeconds && _bufferPosition > 0 && !_fileSaved)
            {
                LOG.Debug($"Silence detected for {SilenceTimeoutSeconds}s, finalizing early.");
                FinalizeRecording();
                return;
            }
        }

        if (_audioBuffer != null && _bufferPosition >= _audioBuffer.Length && !_fileSaved)
        {
            FinalizeRecording();
        }
    }

    private void EnsureBufferAllocated()
    {
        if (_audioBuffer != null) return;

        _audioBuffer = new float[_sampleRate * AudioBufferDurationSeconds];
        LOG.Debug(
            $"Audio buffer allocated: {_audioBuffer.Length} samples ({_sampleRate} Hz, {_channelCount} ch, {AudioBufferDurationSeconds}s)");
    }

    private void CopyVoiceDataToBuffer(short[] voiceData)
    {
        if (_audioBuffer == null) return;

        // Averaged down to mono: the rest of the pipeline (WAV, VAD, playback) is mono.
        var frames = voiceData.Length / _channelCount;
        var samplesToWrite = Mathf.Min(frames, _audioBuffer.Length - _bufferPosition);
        for (var i = 0; i < samplesToWrite; i++)
        {
            var sum = 0f;
            var baseIdx = i * _channelCount;
            for (var c = 0; c < _channelCount; c++)
            {
                sum += voiceData[baseIdx + c];
            }

            _audioBuffer[_bufferPosition + i] = sum / (_channelCount * 32768f);
        }

        _bufferPosition += samplesToWrite;
    }

    private void FinalizeRecording()
    {
        _isRecording = false;
        _capturingSpeech = false;
        _fileSaved = true;

        if (_audioBuffer == null || _bufferPosition == 0) return;

        var recordedData = new float[_bufferPosition];
        Array.Copy(_audioBuffer, recordedData, _bufferPosition);

        var duration = (float)_bufferPosition / _sampleRate;
        if (duration < VepMod.ConfigAudioMinDuration.Value)
        {
            LOG.Debug($"Recording rejected: too short ({duration:F2}s < {VepMod.ConfigAudioMinDuration.Value}s)");
            return;
        }

#if !VEPMOD_NO_VAD
        if (_vadValidator != null)
        {
            // The trailing silence (up to SilenceTimeoutSeconds) would dilute the speech ratio and penalise short
            // words, so the VAD analyses the speech only. The saved Voice Sample keeps its silence.
            var vadData = recordedData;
            var trailingSilenceSamples = Mathf.RoundToInt(_silenceTimer * _sampleRate);
            if (trailingSilenceSamples > 0)
            {
                var speechLen = _bufferPosition - trailingSilenceSamples;
                if (speechLen >= _sampleRate / 10) // at least 0.1 s of speech
                {
                    vadData = new float[speechLen];
                    Array.Copy(recordedData, vadData, speechLen);
                }
            }

            try
            {
                var vadResult = _vadValidator.Validate(vadData, _sampleRate);
#if DEBUG
                LastVadResult = vadResult;
#endif
                if (!vadResult.IsValid)
                {
                    LOG.Debug($"Recording rejected (VAD): {vadResult.RejectionReason} - {vadResult.Analysis}");
                    return;
                }

                LOG.Debug($"Recording passed VAD: {vadResult.Analysis}");
            }
            catch (Exception ex)
            {
                // Accepting keeps a hiccup of the native VAD from disturbing the Photon voice thread.
                LOG.Warning($"VAD Validate threw, accepting recording (fail-open): {ex.Message}");
            }
        }
#endif

        LOG.Debug(
            $"Recording validated and finalized: {_bufferPosition} samples ({duration:F2}s)");
        SaveRecordingAsync(recordedData);
    }

    private void StartRecording()
    {
        if (_isRecording) return;

        LOG.Debug($"StartRecording (sampleRate={_sampleRate})");
        _audioBuffer = null;
        _bufferPosition = 0;
        _isRecording = true;
        _capturingSpeech = false;
        _fileSaved = false;
    }

    private async void SaveRecordingAsync(float[] audioData)
    {
        try
        {
            await _wavFileManager.SaveAsync(audioData, _sampleRate, _localPlayerNickName);

            var audioBytes = AudioFilters.ConvertFloatsToBytes(audioData);

            using var memoryStream = new MemoryStream();
            using var writer = new BinaryWriter(memoryStream);
            WavFileManager.WriteWavHeader(writer, audioData.Length, _sampleRate);
            writer.Write(audioBytes);

            _lastSavedAudioBytes = memoryStream.ToArray();
            _hasNewRecording = true;

            LOG.Debug($"Audio saved and ready for sharing: {_lastSavedAudioBytes.Length} bytes");
        }
        catch (Exception ex)
        {
            LOG.Error($"Error saving recording: {ex.Message}");
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

            StartRecording();

            var timeout = AudioBufferDurationSeconds + 1f;
            while (_isRecording && timeout > 0)
            {
                timeout -= 0.1f;
                yield return new WaitForSeconds(0.1f);
            }

            if (_hasNewRecording && _lastSavedAudioBytes != null)
            {
                _hasNewRecording = false;
                yield return ShareAudioWithOthersAsync(_lastSavedAudioBytes).AsCoroutine();
            }
        }
        // ReSharper disable once IteratorNeverReturns
    }

    private async Task ShareAudioWithOthersAsync(byte[] audioData)
    {
        if (!PhotonNetwork.IsConnectedAndReady)
        {
            LOG.Warning("Photon not connected, skipping share.");
            return;
        }

        var chunks = ChunkAudioData(audioData);
        LOG.Debug($"Sharing audio: {audioData.Length} bytes in {chunks.Count} chunks");

        for (var i = 0; i < chunks.Count; i++)
        {
            if (!PhotonNetwork.IsConnectedAndReady)
            {
                LOG.Warning("Photon disconnected, aborting share.");
                return;
            }

            PhotonView.RPC(nameof(ReceiveSharedAudioChunk), RpcTarget.Others,
                chunks[i], i, chunks.Count, _localPlayerNickName, _sampleRate);

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
            SaveReceivedAudioAsync(combinedData, srcSampleRate, sourcePlayerNickName);

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

    private async void SaveReceivedAudioAsync(byte[] audioData, int srcSampleRate, string sourcePlayerNickName)
    {
        try
        {
            await _wavFileManager.SaveFromBytesAsync(audioData, srcSampleRate, sourcePlayerNickName);
            LOG.Debug($"Audio from {sourcePlayerNickName} saved successfully.");
        }
        catch (Exception ex)
        {
            LOG.Error($"Error saving audio from {sourcePlayerNickName}: {ex.Message}");
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

    public string[] GetAvailablePlayerIds()
    {
        return _wavFileManager?.GetAllPlayerNickNames() ?? Array.Empty<string>();
    }

    #endregion

    #region Audio Processing & Playback

    private float[] ProcessReceivedAudio(byte[] audioData, bool applyVoiceFilter, int receivedSampleRate)
    {
        var samples = AudioFilters.ConvertBytesToFloats(audioData);
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

    public void PlayAudioAtTransform(Transform hallucination, string sourcePlayerNickName, bool applyFilter)
    {
        if (hallucination == null)
        {
            LOG.Warning("PlayAudioAtTransform: hallucination is null");
            return;
        }

        var (audioData, fileSampleRate) = _wavFileManager.GetRandomFile(sourcePlayerNickName);
        if (audioData == null)
        {
            LOG.Debug($"No audio found for player {sourcePlayerNickName}");
            return;
        }

        var processedSamples = ProcessReceivedAudio(audioData, applyFilter, fileSampleRate);
        var audioClip = AudioClip.Create("HallucinationClip", processedSamples.Length, 1, fileSampleRate, false);
        audioClip.SetData(processedSamples, 0);

        PlayAtTransform(hallucination, audioClip, sourcePlayerNickName);
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

        source.outputAudioMixerGroup = _playerVoiceChat.mixerMicrophoneSound;
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
