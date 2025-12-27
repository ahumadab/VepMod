using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx.Logging;
using Photon.Pun;
using UnityEngine;
using VepMod.VepFramework.Extensions;
using Logger = BepInEx.Logging.Logger;
using Random = UnityEngine.Random;

namespace VepMod.Enemies.Whispral;

public class WhispralMimics : MonoBehaviour
{
    private const int MaxClipCount = 10;
    private static readonly ManualLogSource log = Logger.CreateLogSource("VepMod.WhispralMimics");
    public bool isRecording;
    public PhotonView photonView;
    private readonly List<byte[]> receivedChunks = new();
    private readonly HashSet<int> sentChunks = new();
    private float[] audioBuffer;
    private string audioFolderPath;
    private int bufferPosition;
    private bool capturingSpeech;
    private int expectedChunkCount;
    private bool fileSaved;
    private Dictionary<string, bool> filter;
    private PlayerVoiceChat playerVoiceChat;
    private FieldInfo recorderField;
    private int sampleRate;
    private FieldInfo voiceChatField;

    private void Awake()
    {
        photonView = GetComponent<PhotonView>();
        if (!photonView)
        {
            log.LogError("PhotonView not found on WhispralMimics.");
            return;
        }

        var component = GetComponent<PlayerAvatar>();
        if (!component)
        {
            log.LogError("PlayerAvatar not found on WhispralMimics.");
        }

        voiceChatField = typeof(PlayerAvatar).GetField("voiceChat", BindingFlags.Instance | BindingFlags.NonPublic);
        if (voiceChatField == null)
        {
            log.LogError("Could not find 'voiceChat' field in PlayerAvatar.");
            return;
        }

        recorderField = typeof(PlayerVoiceChat).GetField("recorder", BindingFlags.Instance | BindingFlags.NonPublic);
        if (recorderField == null)
        {
            log.LogError("Could not find 'recorder' field in PlayerVoiceChat.");
            return;
        }

        sampleRate = VepMod.ConfigSamplingRate.Value;
        if (VepMod.ConfigFilterEnabled.Value)
        {
            filter = new Dictionary<string, bool>();
            SetEnemyFilter();
        }
        else
        {
            log.LogInfo("Filter not enabled. All enemies (custom included) will mimic voices.");
        }

        StartCoroutine(WaitForVoiceChat(component));
    }

    public void ProcessVoiceData(short[] voiceData)
    {
        if (!isRecording || !photonView.IsMine)
        {
            return;
        }

        if ((bool)typeof(PlayerVoiceChat).GetField("isTalking", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(playerVoiceChat) && !capturingSpeech)
        {
            capturingSpeech = true;
            bufferPosition = 0;
            fileSaved = false;
            log.LogInfo("Speech detected, capturing audio.");
        }

        if (!capturingSpeech)
        {
            return;
        }

        if (audioBuffer == null)
        {
            // On suppose 20 ms par frame Photon
            var inferredSampleRate = voiceData.Length * 50; // 320 * 50 = 16000
            if (sampleRate != inferredSampleRate)
            {
                log.LogWarning(
                    $"SampleRate mismatch: recorder reported {sampleRate}, " +
                    $"but frames suggest {inferredSampleRate}. Using {inferredSampleRate}."
                );
                sampleRate = inferredSampleRate;
            }

            audioBuffer = new float[sampleRate * 3]; // 3 secondes au vrai sample rate
            log.LogInfo($"audioBuffer allocated: length={audioBuffer.Length} ({sampleRate} Hz, 3s)");
        }

        // 20 ms par frame → estimateSR ≈ len * 50

        var num = Mathf.Min(voiceData.Length, audioBuffer.Length - bufferPosition);
        for (var index = 0; index < num; ++index)
        {
            audioBuffer[bufferPosition + index] = voiceData[index] / 32768f;
        }

        bufferPosition += num;
        if (bufferPosition < audioBuffer.Length || fileSaved)
        {
            return;
        }

        isRecording = false;
        capturingSpeech = false;
        fileSaved = true;
        log.LogInfo("Buffer full, saving audio.");
        SaveAudioToFileAsync(audioBuffer);
    }

    [PunRPC]
    public void ReceiveAudioChunk(
        byte[] chunk,
        int chunkIndex,
        int totalChunks,
        bool applyFilter,
        int senderSampleRate)
    {
        if (chunkIndex == 0)
        {
            receivedChunks.Clear();
            expectedChunkCount = totalChunks;
            log.LogInfo($"New audio transmission started, expecting {totalChunks} chunks at {senderSampleRate} Hz.");
            // Usually, "New audio transmission started, expecting 36 chunks at 48000 Hz."
        }

        if (chunkIndex >= expectedChunkCount)
        {
            log.LogWarning($"Received chunk index {chunkIndex} exceeds expected {expectedChunkCount}.");
        }
        else
        {
            if (chunkIndex >= receivedChunks.Count)
            {
                receivedChunks.AddRange(Enumerable.Repeat((byte[])null, chunkIndex - receivedChunks.Count + 1));
            }

            receivedChunks[chunkIndex] = chunk;
            if (receivedChunks.Count < expectedChunkCount || !receivedChunks.All(c => c != null))
            {
                return;
            }

            log.LogInfo("All chunks received, playing audio.");
            PlayReceivedAudio(CombineChunks(receivedChunks), applyFilter, senderSampleRate);
            receivedChunks.Clear();
            expectedChunkCount = 0;
        }
    }

    private float[] ApplyAlienFilter(float[] samples)
    {
        var numArray = new float[samples.Length];
        const float num1 = 5f;
        const float num2 = 0.05f;
        const float num3 = 200f;
        const float num4 = 0.3f;
        for (var index1 = 0; index1 < samples.Length; ++index1)
        {
            var num5 = index1 / (float)sampleRate;
            var num6 = 1f + Mathf.Sin(6.28318548f * num1 * num5) * num2;
            var num7 = index1 * num6;
            var index2 = (int)num7;
            var num8 = num7 - index2;
            var num9 = 0.0f;
            if (index2 + 1 < samples.Length)
            {
                num9 = (float)(samples[index2] * (1.0 - num8) + samples[index2 + 1] * (double)num8);
            }
            else if (index2 < samples.Length)
            {
                num9 = samples[index2];
            }

            var num10 = Mathf.Sin(6.28318548f * num3 * num5);
            var num11 = num9 * num10 * num4;
            numArray[index1] = num9 * (1f - num4) + num11;
            numArray[index1] = Mathf.Clamp(numArray[index1], -1f, 1f);
        }

        return numArray;
    }

    private float[] ApplyLowPassFilter(float[] samples, float cutoffFreq)
    {
        var numArray1 = new float[samples.Length];
        const double unknownMagicNumber = 6.2831854820251465;
        var num1 = (float)(1.0 / (unknownMagicNumber * cutoffFreq));
        var num2 = 1f / sampleRate;
        var num3 = num2 / (num1 + num2);
        numArray1[0] = samples[0];
        for (var index = 1; index < samples.Length; ++index)
        {
            numArray1[index] = numArray1[index - 1] + num3 * (samples[index] - numArray1[index - 1]);
        }

        var numArray2 = new float[samples.Length];
        numArray2[samples.Length - 1] = numArray1[samples.Length - 1];
        for (var index = samples.Length - 2; index >= 0; --index)
        {
            numArray2[index] = numArray2[index + 1] + num3 * (numArray1[index] - numArray2[index + 1]);
        }

        return numArray2;
    }

    private static float[] ApplyPitchShift(float[] samples, float pitchFactor)
    {
        log.LogInfo("Pitch shift applied.");
        var length = (int)(samples.Length / (double)pitchFactor);
        var numArray = new float[length];
        for (var index1 = 0; index1 < length; ++index1)
        {
            var num1 = index1 * pitchFactor;
            var index2 = (int)num1;
            var num2 = num1 - index2;
            if (index2 + 1 < samples.Length)
            {
                numArray[index1] = (float)(samples[index2] * (1.0 - num2) + samples[index2 + 1] * (double)num2);
            }
            else if (index2 < samples.Length)
            {
                numArray[index1] = samples[index2];
            }
        }

        return numArray;
    }

    private static List<byte[]> ChunkAudioData(byte[] audioData, int chunkSize)
    {
        var numArrayList = new List<byte[]>();
        for (var sourceIndex = 0; sourceIndex < audioData.Length; sourceIndex += chunkSize)
        {
            var length = Mathf.Min(chunkSize, audioData.Length - sourceIndex);
            var destinationArray = new byte[length];
            Array.Copy(audioData, sourceIndex, destinationArray, 0, length);
            numArrayList.Add(destinationArray);
        }

        return numArrayList;
    }

    private async Task ClearAudioFolderAsync()
    {
        var strArray = Directory.GetFiles(audioFolderPath, "*.wav");
        for (var index = 0; index < strArray.Length; ++index)
        {
            var file = strArray[index];
            await Task.Run((Action)(() => File.Delete(file)));
        }

        strArray = null;
        log.LogInfo("Audio folder cleared.");
    }

    private static byte[] CombineChunks(List<byte[]> chunks)
    {
        var destinationArray = new byte[chunks.Sum(chunk => chunk.Length)];
        var destinationIndex = 0;
        foreach (var chunk in chunks)
        {
            Array.Copy(chunk, 0, destinationArray, destinationIndex, chunk.Length);
            destinationIndex += chunk.Length;
        }

        return destinationArray;
    }

    private float[] ConvertByteArrayToFloatArray(
        byte[] byteArray,
        bool applyVoiceFilter,
        int senderSampleRate)
    {
        const double unknownMagicNumber = 0.019999999552965164;
        const float unknownMagicFloat = 32768f;
        var num1 = (int)(senderSampleRate * 0.5);
        var length = byteArray.Length / 2;
        var num2 = (int)(senderSampleRate * unknownMagicNumber);
        var samples1 = new float[length];
        for (var index = 0; index < length; ++index)
        {
            samples1[index] = BitConverter.ToInt16(byteArray, index * 2) / unknownMagicFloat;
        }

        var samples2 = ApplyLowPassFilter(samples1, 4500f);
        if (applyVoiceFilter)
        {
            var num3 = Random.Range(0, 3);
            if (num3 == 0)
            {
                samples2 = ApplyPitchShift(samples2, 0.5f);
            }

            if (num3 == 1)
            {
                samples2 = ApplyPitchShift(samples2, 1.2f);
            }

            if (num3 == 2)
            {
                samples2 = ApplyAlienFilter(samples2);
            }
        }

        var floatArray = new float[samples2.Length + 2 * num1];
        for (var index = 0; index < num1; ++index)
        {
            floatArray[index] = 0.0f;
        }

        for (var index = 0; index < samples2.Length; ++index)
        {
            var num4 = samples2[index];
            var num5 = 1f;
            if (index < num2)
            {
                num5 = index / (float)num2;
            }
            else if (index >= samples2.Length - num2)
            {
                num5 = (samples2.Length - index) / (float)num2;
            }

            floatArray[index + num1] = num4 * num5;
        }

        for (var index = samples2.Length + num1; index < floatArray.Length; ++index)
        {
            floatArray[index] = 0.0f;
        }

        return floatArray;
    }

    private static byte[] ConvertFloatArrayToByteArray(float[] audioData)
    {
        var byteArray = new byte[audioData.Length * 2];
        for (var index = 0; index < audioData.Length; ++index)
        {
            BitConverter.GetBytes((short)(audioData[index] * (double)short.MaxValue)).CopyTo(byteArray, index * 2);
        }

        return byteArray;
    }

    private static IEnumerator DestroyAfterDelay(AudioSource audioSource, float delay)
    {
        yield return new WaitForSeconds(delay);
        Destroy(audioSource);
    }

    private static List<GameObject> GetEnemiesList()
    {
        var source = GameObject.Find("Level Generator")?.transform.Find("Enemies");
        return source != null
            ? source.Cast<Transform>().Select(t => t.gameObject).ToList()
            : new List<GameObject>();
    }

    private async Task PlayRandomAudioFile()
    {
        var files = Directory.GetFiles(audioFolderPath, "*.wav");
        string selectedFile;
        byte[] audioBytes;
        if (files.Length == 0)
        {
            files = null;
            selectedFile = null;
            audioBytes = null;
        }
        else
        {
            selectedFile = files[Random.Range(0, files.Length)];
            audioBytes = await File.ReadAllBytesAsync(selectedFile);
            await SendAudioInChunksAsync(audioBytes);
            files = null;
            selectedFile = null;
            audioBytes = null;
        }
    }

    private void PlayReceivedAudio(byte[] audioData, bool applyVoiceFilter, int senderSampleRate)
    {
        if (applyVoiceFilter)
        {
            log.LogInfo("Expecting filter.");
        }

        var floatArray = ConvertByteArrayToFloatArray(audioData, applyVoiceFilter, senderSampleRate);
        var audioClip = AudioClip.Create("ReceivedClip", floatArray.Length, 1, senderSampleRate, false);
        audioClip.SetData(floatArray, 0);
        foreach (var gameObject1 in GetEnemiesList().Where(e => e != null && !e.name.Contains("Gnome")))
        {
            if (VepMod.ConfigFilterEnabled.Value)
            {
                var key = gameObject1.name.Replace("(Clone)", "");
                bool flag;
                if (!filter.TryGetValue(key, out flag) || !flag)
                {
                    log.LogInfo("Skipped " + key + ": disabled in config");
                    continue;
                }
            }

            var gameObject2 = gameObject1.transform.Find("Enable/Controller")?.gameObject;
            if (!(gameObject2 == null))
            {
                var audioSource = gameObject2.GetComponent<AudioSource>() ?? gameObject2.AddComponent<AudioSource>();
                audioSource.clip = audioClip;
                audioSource.volume = VepMod.ConfigVoiceVolume.Value;
                audioSource.spatialBlend = 1f;
                audioSource.dopplerLevel = 0.5f;
                audioSource.minDistance = 1f;
                audioSource.maxDistance = 20f;
                audioSource.rolloffMode = (AudioRolloffMode)1;
                audioSource.outputAudioMixerGroup = playerVoiceChat.mixerMicrophoneSound;
                audioSource.Play();
                StartCoroutine(DestroyAfterDelay(audioSource, audioClip.length + 0.1f));
            }
        }
    }

    private IEnumerator RecordAtRandomIntervals()
    {
        while (true) // Main loop
        {
            var clipCount = Directory.GetFiles(audioFolderPath, "*.wav").Length;
            if (clipCount >= MaxClipCount)
            {
                yield return ClearAudioFolderAsync().AsCoroutine();
            }

            var randomDelay = Random.Range(VepMod.ConfigMinDelay.Value, VepMod.ConfigMaxDelay.Value);
            yield return new WaitForSeconds(randomDelay);
            StartRecording();
        }
    }

    private async Task SaveAudioToFileAsync(float[] audioData)
    {
        var filePath = Path.Combine(audioFolderPath, $"audio_{Guid.NewGuid()}.wav");
        var audioBytes = ConvertFloatArrayToByteArray(audioData);
        await using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
        {
            await using (var writer = new BinaryWriter(fileStream))
            {
                WriteWavHeader(writer, audioData.Length, sampleRate);
                writer.Write(audioBytes);
            }
        }

        log.LogInfo("Audio saved to: " + filePath);
        await PlayRandomAudioFile();
        filePath = null;
        audioBytes = null;
    }

    private async Task SendAudioInChunksAsync(byte[] audioData)
    {
        var chunks = ChunkAudioData(audioData, 8192);
        sentChunks.Clear();
        for (var i = 0; i < chunks.Count; ++i)
        {
            if (!PhotonNetwork.IsConnectedAndReady)
            {
                log.LogWarning("Photon disconnected, aborting send.");
                chunks = null;
                return;
            }

            var applyVoiceFilter = Random.value > 0.89999997615814209;
            if (VepMod.ConfigHearYourself.Value)
            {
                photonView.RPC("ReceiveAudioChunk", 0, chunks[i], i, chunks.Count, applyVoiceFilter, sampleRate);
            }
            else
            {
                photonView.RPC("ReceiveAudioChunk", (RpcTarget)1, chunks[i], i, chunks.Count, applyVoiceFilter,
                    sampleRate);
            }

            sentChunks.Add(i);
            await Task.Delay(125);
        }

        log.LogInfo("All chunks sent.");
        chunks = null;
    }

    private void SetEnemyFilter()
    {
        filter.Clear();
        if (filter == null)
        {
            log.LogError("Enemy filter is null");
        }

        foreach (var enemyConfigEntry in VepMod.EnemyConfigEntries)
        {
            filter.Add(enemyConfigEntry.Key ?? "", enemyConfigEntry.Value.Value);
        }

        log.LogInfo("Config loaded and filter set.");
    }

    private void StartRecording()
    {
        if (isRecording) return;

        log.LogInfo($"StartRecording with sampleRate={sampleRate}"); // 48000Hz
        // audioBuffer = new float[sampleRate * 3]; // 144000 // 3 seconds buffer
        audioBuffer = null; // 144000 // 3 seconds buffer
        bufferPosition = 0;
        isRecording = true;
        capturingSpeech = false;
        fileSaved = false;
        log.LogInfo("Recording started.");
    }

    private IEnumerator WaitForVoiceChat(PlayerAvatar playerAvatar)
    {
        for (playerVoiceChat = (PlayerVoiceChat)voiceChatField.GetValue(playerAvatar);
             playerVoiceChat == null;
             playerVoiceChat = (PlayerVoiceChat)voiceChatField.GetValue(playerAvatar))
        {
            yield return null;
        }

        log.LogInfo("PlayerVoiceChat successfully initialized.");


        var recorder = recorderField.GetValue(playerVoiceChat);
        if (recorder != null)
        {
            var recorderType = recorder.GetType();
            var prop = recorderType.GetProperty("SamplingRate") ??
                       recorderType.GetProperty("RecordingSampleRate") ??
                       recorderType.GetProperty("SamplingRateOverride");

            if (prop != null)
            {
                var value = prop.GetValue(recorder);
                if (value is int sr && sr > 0)
                {
                    sampleRate = sr;
                    log.LogInfo($"Using recorder sample rate: {sampleRate}");
                }
                else if (value != null && value.GetType().IsEnum)
                {
                    // Si c'est un enum Photon.Voice.SamplingRate = 8000, 16000, 24000, 48000...
                    sampleRate = (int)value;
                    log.LogInfo($"Using recorder enum sample rate: {sampleRate}");
                }
            }
        }

        if (sampleRate <= 0)
        {
            // fallback sécurisé
            sampleRate = AudioSettings.outputSampleRate;
            log.LogWarning($"Falling back to AudioSettings.outputSampleRate = {sampleRate}");
        }


        if (photonView.IsMine && SemiFunc.RunIsLevel())
        {
            audioFolderPath = Path.Combine(Application.dataPath, "AudioFiles");
            Directory.CreateDirectory(audioFolderPath);
            StartCoroutine(RecordAtRandomIntervals());
        }
    }

    private static void WriteWavHeader(BinaryWriter writer, int sampleCount, int sampleRate)
    {
        writer.Write("RIFF".ToCharArray());
        writer.Write(36 + sampleCount * 2);
        writer.Write("WAVE".ToCharArray());
        writer.Write("fmt ".ToCharArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data".ToCharArray());
        writer.Write(sampleCount * 2);
    }
}