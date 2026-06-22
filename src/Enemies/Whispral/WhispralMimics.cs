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
///     Gère l'enregistrement, le partage et la lecture des voix pour le système Mimic.
///     Architecture à 2 boucles:
///     - Boucle 1: Partage des sons entre tous les clients
///     - Boucle 2: Commandes de lecture du Master vers les joueurs debuffés
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

        sampleRate = VepMod.ConfigSamplingRate.Value;

        StartCoroutine(WaitForVoiceChat(playerAvatar));
    }

    private void OnDestroy()
    {
#if !VEPMOD_NO_VAD
        vadValidator?.Dispose();
#endif
    }

    #endregion

    #region Constants

    // Enregistrement audio
    private const int AudioBufferDurationSeconds = 6;
    private const float SilenceTimeoutSeconds = 0.5f;

    // Transmission réseau
    private const int ChunkSizeBytes = 8192;
    private const int ChunkDelayMs = 125;

    // Lecture audio (valeurs du prefab Voice du jeu)
    private const float SpatialBlend = 1f;
    private const float DopplerLevel = 0.5f;
    private const float MinDistance = 5f;
    private const float MaxDistance = 25f;

    #endregion

    #region Private Fields

    private PlayerVoiceChat playerVoiceChat;
    private WavFileManager wavFileManager;
#if !VEPMOD_NO_VAD
    private VadAudioValidator? vadValidator;
#if DEBUG
    /// <summary>Dernier résultat VAD (instrumentation dev pour le HUD DroidDevTools).</summary>
    public VadValidationResult? LastVadResult { get; private set; }
#endif
#endif
    private string localPlayerNickName;

    // Reflection fields
    private FieldInfo? voiceChatField;
    private FieldInfo recorderField;
    private FieldInfo isTalkingField;

    // Enregistrement
    private float[]? audioBuffer;
    private int bufferPosition;
    private int sampleRate;
    private int channelCount = 1;
    private bool isRecording;
    private bool capturingSpeech;
    private bool fileSaved;
    private float silenceTimer;
    private byte[]? lastSavedAudioBytes;
    private bool hasNewRecording;

    // Réception chunks par joueur (pour le partage)
    private readonly Dictionary<string, List<byte[]>> receivedChunksByPlayer = new();
    private readonly Dictionary<string, int> expectedChunkCountByPlayer = new();

    #endregion

    #region Initialization

    private bool InitializeReflectionFields()
    {
        voiceChatField = typeof(PlayerAvatar).GetField("voiceChat", BindingFlags.Instance | BindingFlags.NonPublic);
        if (voiceChatField == null)
        {
            LOG.Error("Could not find 'voiceChat' field in PlayerAvatar.");
            return false;
        }

        recorderField = typeof(PlayerVoiceChat).GetField("recorder", BindingFlags.Instance | BindingFlags.NonPublic);
        if (recorderField == null)
        {
            LOG.Error("Could not find 'recorder' field in PlayerVoiceChat.");
            return false;
        }

        isTalkingField = typeof(PlayerVoiceChat).GetField("isTalking", BindingFlags.Instance | BindingFlags.NonPublic);
        if (isTalkingField == null)
        {
            LOG.Error("Could not find 'isTalking' field in PlayerVoiceChat.");
            return false;
        }

        return true;
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

        LOG.Info("PlayerVoiceChat initialized.");
        DetectSampleRate();

        // Initialiser wavFileManager pour TOUS les joueurs (nécessaire pour recevoir les RPCs)
        // mais la boucle de partage ne démarre que pour le joueur local
        if (SemiFunc.RunIsLevel())
        {
            wavFileManager = new WavFileManager(VepMod.ConfigSamplesPerPlayer.Value);
            localPlayerNickName = PhotonNetwork.LocalPlayer.NickName ?? "unknown";

#if !VEPMOD_NO_VAD
            // Créer le validateur VAD si activé (détection de parole par ML)
            // Sensibilité choisie par l'utilisateur (presets calibrés par benchmark).
            if (VepMod.ConfigVadEnabled.Value)
            {
                try
                {
                    var sensitivity = VepMod.ConfigVadSensitivity.Value;
                    var criteria = VadValidationCriteria.FromSensitivity(sensitivity);
                    vadValidator = new VadAudioValidator(criteria);
                    LOG.Info($"VAD validation enabled (sensitivity={sensitivity}, speechRatio>={criteria.MinSpeechRatio:F2}).");
                }
                catch (Exception ex)
                {
                    LOG.Warning($"VAD initialization failed, recordings will not be filtered: {ex.Message}");
                    vadValidator = null;
                }
            }
#endif

            if (PhotonView.IsMine)
            {
                LOG.Info($"WhispralMimics initialized for local player: {localPlayerNickName}");

                // BOUCLE 1: Partage des sons (uniquement pour le joueur local)
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
                    LOG.Debug($"Detected sample rate: {sampleRate} Hz");
                    return;
                }

                if (value != null && value.GetType().IsEnum)
                {
                    sampleRate = (int)value;
                    LOG.Debug($"Detected enum sample rate: {sampleRate} Hz");
                    return;
                }
            }
        }

        if (sampleRate <= 0)
        {
            sampleRate = AudioSettings.outputSampleRate;
            LOG.Warning($"Fallback to AudioSettings.outputSampleRate: {sampleRate} Hz");
        }
    }

    #endregion

    #region Recording

    public void ProcessVoiceData(short[] voiceData, int frameSampleRate, int channels)
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
            // Format autoritaire fourni par Photon (remplace l'ancienne inférence frameLength*50).
            if (frameSampleRate > 0) sampleRate = frameSampleRate;
            channelCount = channels >= 1 ? channels : 1;
            LOG.Debug($"Speech detected, capturing audio ({sampleRate} Hz, {channelCount} ch, frame {voiceData.Length}).");
        }

        if (!capturingSpeech)
        {
            return;
        }

        EnsureBufferAllocated();
        CopyVoiceDataToBuffer(voiceData);

        if (isTalking)
        {
            silenceTimer = 0f;
        }
        else
        {
            var frameDurationSec = sampleRate > 0 ? (float)(voiceData.Length / channelCount) / sampleRate : 0f;
            silenceTimer += frameDurationSec;
            if (silenceTimer >= SilenceTimeoutSeconds && bufferPosition > 0 && !fileSaved)
            {
                LOG.Debug($"Silence detected for {SilenceTimeoutSeconds}s, finalizing early.");
                FinalizeRecording();
                return;
            }
        }

        if (audioBuffer != null && bufferPosition >= audioBuffer.Length && !fileSaved)
        {
            FinalizeRecording();
        }
    }

    private void EnsureBufferAllocated()
    {
        if (audioBuffer != null) return;

        audioBuffer = new float[sampleRate * AudioBufferDurationSeconds];
        LOG.Debug(
            $"Audio buffer allocated: {audioBuffer.Length} samples ({sampleRate} Hz, {channelCount} ch, {AudioBufferDurationSeconds}s)");
    }

    private void CopyVoiceDataToBuffer(short[] voiceData)
    {
        if (audioBuffer == null) return;

        // Downmix multi-canal -> mono par moyenne des canaux (no-op si channelCount == 1).
        // Le reste du pipeline (WAV, VAD, lecture) est mono.
        var frames = voiceData.Length / channelCount;
        var samplesToWrite = Mathf.Min(frames, audioBuffer.Length - bufferPosition);
        for (var i = 0; i < samplesToWrite; i++)
        {
            var sum = 0f;
            var baseIdx = i * channelCount;
            for (var c = 0; c < channelCount; c++)
            {
                sum += voiceData[baseIdx + c];
            }

            audioBuffer[bufferPosition + i] = sum / (channelCount * 32768f);
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

        // Garde-fou durée minimale (sans VAD)
        var duration = (float)bufferPosition / sampleRate;
        if (duration < VepMod.ConfigAudioMinDuration.Value)
        {
            LOG.Debug($"Recording rejected: too short ({duration:F2}s < {VepMod.ConfigAudioMinDuration.Value}s)");
            return;
        }

#if !VEPMOD_NO_VAD
        // Validation VAD (détection de parole par ML, filtre principal)
        if (vadValidator != null)
        {
            // Rogner le silence de fin (jusqu'à SilenceTimeoutSeconds) avant la validation pour
            // ne pas diluer le ratio de parole et pénaliser les mots courts. Le clip sauvegardé
            // reste complet (silence inclus) — seule l'analyse VAD porte sur la région de parole.
            var vadData = recordedData;
            var trailingSilenceSamples = Mathf.RoundToInt(silenceTimer * sampleRate);
            if (trailingSilenceSamples > 0)
            {
                var speechLen = bufferPosition - trailingSilenceSamples;
                if (speechLen >= sampleRate / 10) // garde-fou : au moins 0,1 s de parole
                {
                    vadData = new float[speechLen];
                    Array.Copy(recordedData, vadData, speechLen);
                }
            }

            try
            {
                var vadResult = vadValidator.Validate(vadData, sampleRate);
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
                // Fail-open : un hoquet du VAD natif ne doit pas perturber le thread voix Photon.
                LOG.Warning($"VAD Validate threw, accepting recording (fail-open): {ex.Message}");
            }
        }
#endif

        LOG.Debug(
            $"Recording validated and finalized: {bufferPosition} samples ({duration:F2}s)");
        SaveRecordingAsync(recordedData);
    }

    private void StartRecording()
    {
        if (isRecording) return;

        LOG.Debug($"StartRecording (sampleRate={sampleRate})");
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

            LOG.Debug($"Audio saved and ready for sharing: {lastSavedAudioBytes.Length} bytes");
        }
        catch (Exception ex)
        {
            LOG.Error($"Error saving recording: {ex.Message}");
        }
    }

    #endregion

    #region Boucle 1: Partage des sons

    /// <summary>
    ///     BOUCLE 1: Enregistre périodiquement la voix et la partage avec les autres joueurs.
    /// </summary>
    private IEnumerator ShareAudioLoop()
    {
        LOG.Info("ShareAudioLoop started.");

        while (true)
        {
            var delay = Random.Range(VepMod.ShareDelay.Min, VepMod.ShareDelay.Max);
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
                chunks[i], i, chunks.Count, localPlayerNickName, sampleRate);

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
            LOG.Debug(
                $"Receiving shared audio from {sourcePlayerNickName}: {totalChunks} chunks at {srcSampleRate} Hz");
        }

        // Vérifier que le joueur existe dans nos buffers
        if (!receivedChunksByPlayer.TryGetValue(sourcePlayerNickName, out var chunks))
        {
            LOG.Warning($"Received chunk for unknown player: {sourcePlayerNickName}");
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
            LOG.Debug($"All chunks received from {sourcePlayerNickName}, saving audio.");

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
            LOG.Debug($"Audio from {sourcePlayerNickName} saved successfully.");
        }
        catch (Exception ex)
        {
            LOG.Error($"Error saving audio from {sourcePlayerNickName}: {ex.Message}");
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

        // Vérifier si on a une hallucination active pour ce joueur
        var hallucinationDebuff = localPlayer.GetComponent<DroidDebuff>();
        if (hallucinationDebuff != null && hallucinationDebuff.IsActive)
        {
            var droid = hallucinationDebuff.GetDroidByPlayerName(sourcePlayerNickName);
            if (droid != null)
            {
                LOG.Debug($"Playing voice from {sourcePlayerNickName} on hallucination droid");
                droid.PlayVoice(applyFilter);
            }
        }
    }

    /// <summary>
    ///     Méthode publique pour que EnemyWhispral puisse envoyer une commande de lecture.
    /// </summary>
    public void SendPlayVoiceCommand(int targetViewID, string sourcePlayerName, bool applyFilter)
    {
        if (!PhotonNetwork.IsConnectedAndReady)
        {
            LOG.Warning("Photon not connected, cannot send voice command.");
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

    /// <summary>
    ///     Joue un son à la position d'un Transform spécifique (pour les hallucinations).
    /// </summary>
    public void PlayAudioAtTransform(Transform target, string sourcePlayerNickName, bool applyFilter)
    {
        if (target == null)
        {
            LOG.Warning("PlayAudioAtTransform: target is null");
            return;
        }

        var (audioData, fileSampleRate) = wavFileManager.GetRandomFile(sourcePlayerNickName);
        if (audioData == null)
        {
            LOG.Debug($"No audio found for player {sourcePlayerNickName}");
            return;
        }

        var processedSamples = ProcessReceivedAudio(audioData, applyFilter, fileSampleRate);
        var audioClip = AudioClip.Create("HallucinationClip", processedSamples.Length, 1, fileSampleRate, false);
        audioClip.SetData(processedSamples, 0);

        PlayAtTransform(target, audioClip);
    }

    private void PlayAtTransform(Transform target, AudioClip clip)
    {
        var audioSource = target.gameObject.GetOrAddComponent<AudioSource>();
        audioSource.enabled = true;
        ConfigureAudioSource(audioSource, clip);

        // Ajouter l'occlusion audio (murs) comme les vrais joueurs
        var lowPassFilter = target.gameObject.GetOrAddComponent<AudioLowPassFilter>();
        var lowPassLogic = target.gameObject.GetOrAddComponent<AudioLowPassLogic>();
        lowPassLogic.ForceStart = true;
        lowPassLogic.AlwaysActive = true;
        lowPassLogic.Fetch = true;
        lowPassLogic.Setup();

        audioSource.Play();

        StartCoroutine(DestroyAudioSourceAfterPlayback(audioSource, clip.length + 0.1f));
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

        // Courbe custom du prefab Voice: plein volume jusqu'à 20% de la distance, puis fade vers 0
        source.rolloffMode = AudioRolloffMode.Custom;
        source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, new AnimationCurve(
            new Keyframe(0.2f, 1f),
            new Keyframe(1f, 0f)
        ));

        source.outputAudioMixerGroup = playerVoiceChat.mixerMicrophoneSound;
    }


    private static IEnumerator DestroyAudioSourceAfterPlayback(AudioSource source, float delay)
    {
        yield return new WaitForSeconds(delay);

        if (source == null) yield break;

        var go = source.gameObject;

        // Détruire dans l'ordre inverse des dépendances (attendre 1 frame entre chaque)
        var lowPassLogic = go.GetComponent<AudioLowPassLogic>();
        if (lowPassLogic != null)
        {
            Destroy(lowPassLogic);
            yield return null;
        }

        var lowPassFilter = go.GetComponent<AudioLowPassFilter>();
        if (lowPassFilter != null)
        {
            Destroy(lowPassFilter);
            yield return null;
        }

        if (source != null) Destroy(source);
    }

    #endregion
}