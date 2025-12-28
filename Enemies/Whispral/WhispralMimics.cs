using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx.Logging;
using Photon.Pun;
using Unity.VisualScripting;
using UnityEngine;
using VepMod.VepFramework.Extensions;
using Logger = BepInEx.Logging.Logger;
using Random = UnityEngine.Random;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

namespace VepMod.Enemies.Whispral;

public sealed class WhispralMimics : MonoBehaviour
{
    private static readonly ManualLogSource LOG = Logger.CreateLogSource("VepMod.WhispralMimics");

    public PhotonView PhotonView { get; private set; }

    #region Unity Lifecycle

    private void Awake()
    {
        PhotonView = GetComponent<PhotonView>();
        if (!PhotonView)
        {
            LOG.LogError("PhotonView not found.");
            return;
        }

        var playerAvatar = GetComponent<PlayerAvatar>();
        if (!playerAvatar)
        {
            LOG.LogError("PlayerAvatar not found.");
            return;
        }

        if (!InitializeReflectionFields())
        {
            return;
        }

        sampleRate = VepMod.ConfigSamplingRate.Value;
        InitializeEnemyFilter();

        StartCoroutine(WaitForVoiceChat(playerAvatar));
    }

    #endregion

    #region Constants

    // Enregistrement audio
    private const int AudioBufferDurationSeconds = 6;
    private const int FrameDurationMs = 20;
    private const int FramesPerSecond = 1000 / FrameDurationMs; // 50 frames/s
    private const float SilenceTimeoutSeconds = 0.5f; // Temps de silence avant d'arrêter l'enregistrement

    // Transmission réseau
    private const int ChunkSizeBytes = 8192;
    private const int ChunkDelayMs = 125;
    private const float VoiceFilterProbability = 0.1f;

    // Lecture audio
    private const float SpatialBlend = 1f;
    private const float DopplerLevel = 0.5f;
    private const float MinDistance = 1f;
    private const float MaxDistance = 20f;

    #endregion

    #region Private Fields

    private PlayerVoiceChat playerVoiceChat;
    private WavFileManager wavFileManager;

    // Reflection fields
    private FieldInfo? voiceChatField;
    private FieldInfo recorderField;
    private FieldInfo isTalkingField;

    // Enregistrement
    private float[]? audioBuffer;
    private int bufferPosition;
    private int sampleRate;
    private bool isRecording;
    private bool capturingSpeech;
    private bool fileSaved;
    private float silenceTimer;

    // Réception chunks
    private readonly List<byte[]> receivedChunks = new();
    private readonly HashSet<int> sentChunks = new();
    private int expectedChunkCount;

    // Filtrage ennemis
    private Dictionary<string, bool> enemyFilter;

    #endregion

    #region Initialization

    private bool InitializeReflectionFields()
    {
        voiceChatField = typeof(PlayerAvatar).GetField("voiceChat", BindingFlags.Instance | BindingFlags.NonPublic);
        if (voiceChatField == null)
        {
            LOG.LogError("Could not find 'voiceChat' field in PlayerAvatar.");
            return false;
        }

        recorderField = typeof(PlayerVoiceChat).GetField("recorder", BindingFlags.Instance | BindingFlags.NonPublic);
        if (recorderField == null)
        {
            LOG.LogError("Could not find 'recorder' field in PlayerVoiceChat.");
            return false;
        }

        isTalkingField = typeof(PlayerVoiceChat).GetField("isTalking", BindingFlags.Instance | BindingFlags.NonPublic);
        if (isTalkingField == null)
        {
            LOG.LogError("Could not find 'isTalking' field in PlayerVoiceChat.");
            return false;
        }

        return true;
    }

    private void InitializeEnemyFilter()
    {
        if (!VepMod.ConfigFilterEnabled.Value)
        {
            LOG.LogInfo("Filter disabled. All enemies will mimic voices.");
            return;
        }

        enemyFilter = new Dictionary<string, bool>();
        foreach (var entry in VepMod.EnemyConfigEntries)
        {
            enemyFilter[entry.Key ?? ""] = entry.Value.Value;
        }

        LOG.LogInfo("Enemy filter initialized.");
    }

    private IEnumerator WaitForVoiceChat(PlayerAvatar playerAvatar)
    {
        // Attendre que PlayerVoiceChat soit initialisé
        while (!playerVoiceChat)
        {
            if (voiceChatField == null)
            {
                yield break;
            }

            playerVoiceChat = (PlayerVoiceChat)voiceChatField.GetValue(playerAvatar);
            yield return null;
        }

        LOG.LogInfo("PlayerVoiceChat initialized.");

        DetectSampleRate();

        if (PhotonView.IsMine && SemiFunc.RunIsLevel())
        {
            wavFileManager = new WavFileManager();
            StartCoroutine(RecordAtRandomIntervals());
        }
    }

    private void DetectSampleRate()
    {
        var recorder = recorderField.GetValue(playerVoiceChat);
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
                    sampleRate = sr;
                    LOG.LogInfo($"Detected sample rate: {sampleRate} Hz");
                    return;
                }

                if (value != null && value.GetType().IsEnum)
                {
                    sampleRate = (int)value;
                    LOG.LogInfo($"Detected enum sample rate: {sampleRate} Hz");
                    return;
                }
            }
        }

        if (sampleRate <= 0)
        {
            sampleRate = AudioSettings.outputSampleRate;
            LOG.LogWarning($"Fallback to AudioSettings.outputSampleRate: {sampleRate} Hz");
        }
    }

    #endregion

    #region Recording

    public void ProcessVoiceData(short[] voiceData)
    {
        if (!isRecording || !PhotonView.IsMine)
        {
            return;
        }

        var isTalking = (bool)isTalkingField.GetValue(playerVoiceChat);
        if (isTalking && !capturingSpeech)
        {
            capturingSpeech = true;
            bufferPosition = 0;
            fileSaved = false;
            silenceTimer = 0f;
            LOG.LogInfo("Speech detected, capturing audio.");
        }

        if (!capturingSpeech)
        {
            return;
        }

        EnsureBufferAllocated(voiceData.Length);
        CopyVoiceDataToBuffer(voiceData);

        // Gérer le timeout de silence
        if (isTalking)
        {
            silenceTimer = 0f;
        }
        else
        {
            silenceTimer += FrameDurationMs / 1000f;
            if (silenceTimer >= SilenceTimeoutSeconds && bufferPosition > 0 && !fileSaved)
            {
                LOG.LogInfo($"Silence detected for {SilenceTimeoutSeconds}s, finalizing early.");
                FinalizeRecording();
                return;
            }
        }

        if (audioBuffer != null && bufferPosition >= audioBuffer.Length && !fileSaved)
        {
            FinalizeRecording();
        }
    }

    private void EnsureBufferAllocated(int frameLength)
    {
        if (audioBuffer != null) return;

        // Vérifier le sample rate basé sur la taille de frame (20ms par frame Photon)
        var inferredSampleRate = frameLength * FramesPerSecond;
        if (sampleRate != inferredSampleRate)
        {
            LOG.LogWarning(
                $"SampleRate mismatch: {sampleRate} vs inferred {inferredSampleRate}. Using {inferredSampleRate}.");
            sampleRate = inferredSampleRate;
        }

        audioBuffer = new float[sampleRate * AudioBufferDurationSeconds];
        LOG.LogInfo(
            $"Audio buffer allocated: {audioBuffer.Length} samples ({sampleRate} Hz, {AudioBufferDurationSeconds}s)");
    }

    private void CopyVoiceDataToBuffer(short[] voiceData)
    {
        if (audioBuffer == null) return;
        var samplesToWrite = Mathf.Min(voiceData.Length, audioBuffer.Length - bufferPosition);
        for (var i = 0; i < samplesToWrite; i++)
        {
            audioBuffer[bufferPosition + i] = voiceData[i] / 32768f;
        }

        bufferPosition += samplesToWrite;
    }

    private void FinalizeRecording()
    {
        isRecording = false;
        capturingSpeech = false;
        fileSaved = true;

        if (audioBuffer == null || bufferPosition == 0) return;

        // Créer un tableau avec uniquement les données enregistrées
        var recordedData = new float[bufferPosition];
        Array.Copy(audioBuffer, recordedData, bufferPosition);

        LOG.LogInfo($"Recording finalized: {bufferPosition} samples ({(float)bufferPosition / sampleRate:F2}s)");
        SaveAndPlayAsync(recordedData);
    }

    private void StartRecording()
    {
        if (isRecording) return;

        LOG.LogInfo($"StartRecording (sampleRate={sampleRate})");
        audioBuffer = null;
        bufferPosition = 0;
        isRecording = true;
        capturingSpeech = false;
        fileSaved = false;
    }

    private IEnumerator RecordAtRandomIntervals()
    {
        while (true)
        {
            if (wavFileManager.IsAtCapacity())
            {
                yield return wavFileManager.ClearAsync().AsCoroutine();
            }

            var delay = Random.Range(VepMod.ConfigMinDelay.Value, VepMod.ConfigMaxDelay.Value);
            yield return new WaitForSeconds(delay);
            StartRecording();
        }
        // ReSharper disable once IteratorNeverReturns
    }

    #endregion

    #region Audio Processing & Playback

    private async void SaveAndPlayAsync(float[] audioData)
    {
        try
        {
            await wavFileManager.SaveAsync(audioData, sampleRate);

            var audioBytes = await wavFileManager.GetRandomFileAsync();
            if (audioBytes != null)
            {
                await SendAudioInChunksAsync(audioBytes);
            }
        }
        catch (Exception)
        {
            // ignored
        }
    }

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

    private void PlayReceivedAudio(byte[] audioData, bool applyVoiceFilter, int receivedSampleRate)
    {
        if (applyVoiceFilter)
        {
            LOG.LogInfo("Applying voice filter.");
        }

        var processedSamples = ProcessReceivedAudio(audioData, applyVoiceFilter, receivedSampleRate);
        var audioClip = AudioClip.Create("MimicClip", processedSamples.Length, 1, receivedSampleRate, false);
        audioClip.SetData(processedSamples, 0);

        PlayOnEnemies(audioClip);
    }

    private void PlayOnEnemies(AudioClip clip)
    {
        var enemies = GetValidEnemies();
        foreach (var enemy in enemies)
        {
            enemy.transform.Find("Enable/Controller");
            var controller = enemy.transform.gameObject;
            if (controller == null) continue;

            var audioSource = controller.GetOrAddComponent<AudioSource>();
            ConfigureAudioSource(audioSource, clip);
            audioSource.Play();

            StartCoroutine(DestroyAudioSourceAfterPlayback(audioSource, clip.length + 0.1f));
        }
    }

    private void ConfigureAudioSource(AudioSource source, AudioClip clip)
    {
        source.clip = clip;
        source.volume = VepMod.ConfigVoiceVolume.Value;
        source.spatialBlend = SpatialBlend;
        source.dopplerLevel = DopplerLevel;
        source.minDistance = MinDistance;
        source.maxDistance = MaxDistance;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.outputAudioMixerGroup = playerVoiceChat.mixerMicrophoneSound;
    }

    private IEnumerable<GameObject> GetValidEnemies()
    {
        var enemiesParent = GameObject.Find("Level Generator").transform.Find("Enemies");
        if (enemiesParent == null) yield break;

        foreach (Transform child in enemiesParent)
        {
            var enemy = child.gameObject;
            if (enemy == null || enemy.name.Contains("Gnome")) continue;

            if (enemyFilter != null)
            {
                var enemyName = enemy.name.Replace("(Clone)", "");
                if (!enemyFilter.TryGetValue(enemyName, out var isEnabled) || !isEnabled)
                {
                    LOG.LogInfo($"Skipped {enemyName}: disabled in config");
                    continue;
                }
            }

            yield return enemy;
        }
    }

    private static IEnumerator DestroyAudioSourceAfterPlayback(AudioSource source, float delay)
    {
        yield return new WaitForSeconds(delay);
        Destroy(source);
    }

    #endregion

    #region Network (RPC)

    private async Task SendAudioInChunksAsync(byte[] audioData)
    {
        var chunks = ChunkAudioData(audioData);
        sentChunks.Clear();

        for (var i = 0; i < chunks.Count; i++)
        {
            if (!PhotonNetwork.IsConnectedAndReady)
            {
                LOG.LogWarning("Photon disconnected, aborting send.");
                return;
            }

            var applyFilter = VepMod.ConfigFilterEnabled.Value && Random.value < VoiceFilterProbability;
            var target = VepMod.ConfigHearYourself.Value ? RpcTarget.All : RpcTarget.Others;

            PhotonView.RPC(nameof(ReceiveAudioChunk), target, chunks[i], i, chunks.Count, applyFilter, sampleRate);
            sentChunks.Add(i);

            await Task.Delay(ChunkDelayMs);
        }

        LOG.LogInfo($"All {chunks.Count} chunks sent.");
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
    public void ReceiveAudioChunk(byte[] chunk, int chunkIndex, int totalChunks, bool applyFilter, int senderSampleRate)
    {
        if (chunkIndex == 0)
        {
            receivedChunks.Clear();
            expectedChunkCount = totalChunks;
            LOG.LogInfo($"Receiving audio: {totalChunks} chunks at {senderSampleRate} Hz");
        }

        if (chunkIndex >= expectedChunkCount)
        {
            LOG.LogWarning($"Chunk index {chunkIndex} exceeds expected {expectedChunkCount}.");
            return;
        }

        // Ensure list is large enough
        while (receivedChunks.Count <= chunkIndex)
        {
            receivedChunks.Add(null);
        }

        receivedChunks[chunkIndex] = chunk;

        // Check if all chunks received
        if (receivedChunks.Count >= expectedChunkCount && receivedChunks.All(c => c != null))
        {
            LOG.LogInfo("All chunks received, playing audio.");
            var combinedData = CombineChunks();
            PlayReceivedAudio(combinedData, applyFilter, senderSampleRate);

            receivedChunks.Clear();
            expectedChunkCount = 0;
        }
    }

    private byte[] CombineChunks()
    {
        var totalLength = receivedChunks.Sum(c => c.Length);
        var result = new byte[totalLength];
        var offset = 0;

        foreach (var chunk in receivedChunks)
        {
            Array.Copy(chunk, 0, result, offset, chunk.Length);
            offset += chunk.Length;
        }

        return result;
    }

    #endregion
}