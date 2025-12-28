using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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

/// <summary>
///     Gère l'enregistrement, le partage et la lecture des voix pour le système Mimic.
///     Architecture à 2 boucles:
///     - Boucle 1: Partage des sons entre tous les clients
///     - Boucle 2: Commandes de lecture du Master vers les joueurs debuffés
/// </summary>
public sealed class WhispralMimics : MonoBehaviour
{
    private static readonly ManualLogSource LOG = Logger.CreateLogSource($"VepMod.{nameof(WhispralMimics)}");

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
    private const float SilenceTimeoutSeconds = 0.5f;

    // Transmission réseau
    private const int ChunkSizeBytes = 8192;
    private const int ChunkDelayMs = 125;

    // Lecture audio
    private const float SpatialBlend = 1f;
    private const float DopplerLevel = 0.5f;
    private const float MinDistance = 1f;
    private const float MaxDistance = 20f;

    #endregion

    #region Private Fields

    private PlayerVoiceChat playerVoiceChat;
    private WavFileManager wavFileManager;
    private string localPlayerNickName;

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
    private byte[]? lastSavedAudioBytes;
    private bool hasNewRecording;

    // Réception chunks par joueur (pour le partage)
    private readonly Dictionary<string, List<byte[]>> receivedChunksByPlayer = new();
    private readonly Dictionary<string, int> expectedChunkCountByPlayer = new();

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
        if (!VepMod.ConfigEnemyFilterEnabled.Value)
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

        // Initialiser wavFileManager pour TOUS les joueurs (nécessaire pour recevoir les RPCs)
        // mais la boucle de partage ne démarre que pour le joueur local
        if (SemiFunc.RunIsLevel())
        {
            wavFileManager = new WavFileManager(VepMod.ConfigSamplesPerPlayer.Value);
            localPlayerNickName = PhotonNetwork.LocalPlayer.NickName ?? "unknown";

            if (PhotonView.IsMine)
            {
                LOG.LogInfo($"WhispralMimics initialized for local player: {localPlayerNickName}");

                // BOUCLE 1: Partage des sons (uniquement pour le joueur local)
                StartCoroutine(ShareAudioLoop());
            }
            else
            {
                LOG.LogInfo("WhispralMimics initialized for remote player (RPC reception only).");
            }
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

        var recordedData = new float[bufferPosition];
        Array.Copy(audioBuffer, recordedData, bufferPosition);

        LOG.LogInfo($"Recording finalized: {bufferPosition} samples ({(float)bufferPosition / sampleRate:F2}s)");
        SaveRecordingAsync(recordedData);
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

    private async void SaveRecordingAsync(float[] audioData)
    {
        try
        {
            await wavFileManager.SaveAsync(audioData, sampleRate, localPlayerNickName);

            // Convertir en bytes pour le partage
            var audioBytes = AudioFilters.ConvertFloatsToBytes(audioData);

            // Créer un fichier WAV complet avec header
            using var memoryStream = new MemoryStream();
            using var writer = new BinaryWriter(memoryStream);
            WavFileManager.WriteWavHeader(writer, audioData.Length, sampleRate);
            writer.Write(audioBytes);

            lastSavedAudioBytes = memoryStream.ToArray();
            hasNewRecording = true;

            LOG.LogInfo($"Audio saved and ready for sharing: {lastSavedAudioBytes.Length} bytes");
        }
        catch (Exception ex)
        {
            LOG.LogError($"Error saving recording: {ex.Message}");
        }
    }

    #endregion

    #region Boucle 1: Partage des sons

    /// <summary>
    ///     BOUCLE 1: Enregistre périodiquement la voix et la partage avec les autres joueurs.
    /// </summary>
    private IEnumerator ShareAudioLoop()
    {
        LOG.LogInfo("ShareAudioLoop started.");

        while (true)
        {
            var delay = Random.Range(VepMod.ConfigShareMinDelay.Value, VepMod.ConfigShareMaxDelay.Value);
            yield return new WaitForSeconds(delay);

            // Démarrer l'enregistrement
            StartRecording();

            // Attendre que l'enregistrement soit terminé (max AudioBufferDurationSeconds + 1s)
            var timeout = AudioBufferDurationSeconds + 1f;
            while (isRecording && timeout > 0)
            {
                timeout -= 0.1f;
                yield return new WaitForSeconds(0.1f);
            }

            // Si on a un nouvel enregistrement, le partager
            if (hasNewRecording && lastSavedAudioBytes != null)
            {
                hasNewRecording = false;
                yield return ShareAudioWithOthersAsync(lastSavedAudioBytes).AsCoroutine();
            }
        }
        // ReSharper disable once IteratorNeverReturns
    }

    /// <summary>
    ///     Envoie les données audio en chunks aux autres joueurs.
    /// </summary>
    private async Task ShareAudioWithOthersAsync(byte[] audioData)
    {
        if (!PhotonNetwork.IsConnectedAndReady)
        {
            LOG.LogWarning("Photon not connected, skipping share.");
            return;
        }

        var chunks = ChunkAudioData(audioData);
        LOG.LogInfo($"Sharing audio: {audioData.Length} bytes in {chunks.Count} chunks");

        for (var i = 0; i < chunks.Count; i++)
        {
            if (!PhotonNetwork.IsConnectedAndReady)
            {
                LOG.LogWarning("Photon disconnected, aborting share.");
                return;
            }

            PhotonView.RPC(nameof(ReceiveSharedAudioChunk), RpcTarget.Others,
                chunks[i], i, chunks.Count, localPlayerNickName, sampleRate);

            await Task.Delay(ChunkDelayMs);
        }

        LOG.LogInfo($"Audio shared successfully: {chunks.Count} chunks sent.");
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

    /// <summary>
    ///     RPC: Reçoit un chunk audio d'un autre joueur et le stocke.
    /// </summary>
    [PunRPC]
    public void ReceiveSharedAudioChunk(byte[] chunk, int chunkIndex, int totalChunks, string sourcePlayerNickName,
        int srcSampleRate)
    {
        // Ignorer mes propres sons (ne devrait pas arriver avec RpcTarget.Others)
        if (sourcePlayerNickName == localPlayerNickName) return;

        // Initialiser le buffer pour ce joueur si c'est le premier chunk
        if (chunkIndex == 0)
        {
            receivedChunksByPlayer[sourcePlayerNickName] = new List<byte[]>();
            expectedChunkCountByPlayer[sourcePlayerNickName] = totalChunks;
            LOG.LogInfo($"Receiving shared audio from {sourcePlayerNickName}: {totalChunks} chunks at {srcSampleRate} Hz");
        }

        // Vérifier que le joueur existe dans nos buffers
        if (!receivedChunksByPlayer.TryGetValue(sourcePlayerNickName, out var chunks))
        {
            LOG.LogWarning($"Received chunk for unknown player: {sourcePlayerNickName}");
            return;
        }

        // Ajouter le chunk
        while (chunks.Count <= chunkIndex)
        {
            chunks.Add(null);
        }

        chunks[chunkIndex] = chunk;

        // Vérifier si tous les chunks sont reçus
        var expectedCount = expectedChunkCountByPlayer.GetValueOrDefault(sourcePlayerNickName, 0);
        if (chunks.Count >= expectedCount && chunks.All(c => c != null))
        {
            LOG.LogInfo($"All chunks received from {sourcePlayerNickName}, saving audio.");

            var combinedData = CombineChunks(chunks);
            SaveReceivedAudioAsync(combinedData, srcSampleRate, sourcePlayerNickName);

            // Nettoyer
            receivedChunksByPlayer.Remove(sourcePlayerNickName);
            expectedChunkCountByPlayer.Remove(sourcePlayerNickName);
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
            await wavFileManager.SaveFromBytesAsync(audioData, srcSampleRate, sourcePlayerNickName);
            LOG.LogInfo($"Audio from {sourcePlayerNickName} saved successfully.");
        }
        catch (Exception ex)
        {
            LOG.LogError($"Error saving audio from {sourcePlayerNickName}: {ex.Message}");
        }
    }

    #endregion

    #region Boucle 2: Commandes de lecture

    /// <summary>
    ///     RPC: Le Master demande à un joueur spécifique de jouer un son d'un joueur source.
    ///     Appelé depuis EnemyWhispral.AttachedState.
    /// </summary>
    [PunRPC]
    public void PlayVoiceCommandRPC(int targetViewID, string sourcePlayerNickName, bool applyFilter)
    {
        // Seul le joueur ciblé joue le son
        var localPlayer = PlayerAvatar.instance;
        if (!localPlayer || localPlayer.photonView.ViewID != targetViewID)
        {
            return;
        }

        // Vérifier HearYourself
        if (sourcePlayerNickName == localPlayerNickName && !VepMod.ConfigHearYourself.Value)
        {
            LOG.LogInfo("HearYourself disabled, skipping own voice.");
            return;
        }

        // Récupérer un son LOCALEMENT depuis les fichiers stockés
        var audioData = wavFileManager.GetRandomFile(sourcePlayerNickName);
        if (audioData == null)
        {
            LOG.LogWarning($"No audio found for player {sourcePlayerNickName}");
            return;
        }

        LOG.LogInfo($"Playing voice from {sourcePlayerNickName} (filter: {applyFilter})");
        PlayReceivedAudio(audioData, applyFilter, sampleRate);
    }

    /// <summary>
    ///     Méthode publique pour que EnemyWhispral puisse envoyer une commande de lecture.
    /// </summary>
    public void SendPlayVoiceCommand(int targetViewID, string sourcePlayerName, bool applyFilter)
    {
        if (!PhotonNetwork.IsConnectedAndReady)
        {
            LOG.LogWarning("Photon not connected, cannot send voice command.");
            return;
        }

        PhotonView.RPC(nameof(PlayVoiceCommandRPC), RpcTarget.All, targetViewID, sourcePlayerName, applyFilter);
    }

    /// <summary>
    ///     Retourne la liste des joueurs ayant des sons stockés localement.
    /// </summary>
    public string[] GetAvailablePlayerIds()
    {
        return wavFileManager?.GetAllPlayerNickNames() ?? Array.Empty<string>();
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
            var audioSource = enemy.GetOrAddComponent<AudioSource>();
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
        var enemiesParent = GameObject.Find("Level Generator")?.transform.Find("Enemies");
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
}