using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using VepMod.VepFramework;
using VepMod.VepFramework.Extensions;
using Random = UnityEngine.Random;

namespace VepMod.Enemies.Whispral;

/// <summary>
///     The Voice Banks on disk: AudioFiles/player_{playerNickName}/sample_*.wav
/// </summary>
public sealed class WavFileManager
{
    private const string WavExtension = "*.wav";
    private const string AudioFolderName = "AudioFiles";
    private const string PlayerFolderPrefix = "player_";
    private static readonly VepLogger LOG = VepLogger.Create<WavFileManager>();

    public WavFileManager(int maxSamplesPerPlayer = 10)
    {
        MaxSamplesPerPlayer = maxSamplesPerPlayer;
        BaseFolderPath = Path.Combine(Application.dataPath, AudioFolderName);
        Directory.CreateDirectory(BaseFolderPath);
    }

    public string BaseFolderPath { get; }
    public int MaxSamplesPerPlayer { get; }

    #region Read Operations

    /// <summary>
    ///     A random Voice Sample of the player, with the sample rate read from its WAV header.
    /// </summary>
    public (byte[]? data, int sampleRate) GetRandomFile(string playerNickName)
    {
        var playerFolder = GetPlayerFolder(playerNickName);
        if (!Directory.Exists(playerFolder))
        {
            return (null, 0);
        }

        var files = Directory.GetFiles(playerFolder, WavExtension);
        if (files.Length == 0)
        {
            LOG.Debug($"No audio files found for player {playerNickName}.");
            return (null, 0);
        }

        var selectedFile = files[Random.Range(0, files.Length)];
        var audioData = File.ReadAllBytes(selectedFile);
        var fileSampleRate = ReadSampleRateFromWav(audioData);

        LOG.Debug($"Loaded audio file for {playerNickName} with sample rate {fileSampleRate} Hz");
        return (audioData, fileSampleRate);
    }

    #endregion

    #region WAV Header

    private static int ReadSampleRateFromWav(byte[] wavData)
    {
        if (wavData.Length < 28)
        {
            LOG.Warning("WAV data too short to read sample rate, using default 48000");
            return 48000;
        }

        return BitConverter.ToInt32(wavData, 24);
    }

    /// <summary>
    ///     16-bit PCM, mono.
    /// </summary>
    internal static void WriteWavHeader(BinaryWriter writer, int sampleCount, int sampleRate)
    {
        const int bitsPerSample = 16;
        const int channels = 1;
        const short blockAlign = channels * bitsPerSample / 8;
        var byteRate = sampleRate * channels * bitsPerSample / 8;
        var dataSize = sampleCount * channels * bitsPerSample / 8;

        // RIFF header
        writer.Write("RIFF".ToCharArray());
        writer.Write(36 + dataSize);
        writer.Write("WAVE".ToCharArray());

        // fmt sub-chunk
        writer.Write("fmt ".ToCharArray());
        writer.Write(16); // Sub-chunk size (16 for PCM)
        writer.Write((short)1); // Audio format (1 = PCM)
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write((short)bitsPerSample);

        // data sub-chunk
        writer.Write("data".ToCharArray());
        writer.Write(dataSize);
    }

    #endregion

    #region Player Folder Management

    private string GetPlayerFolder(string playerNickName)
    {
        var safeId = FileSystemSanitizer.Sanitize(playerNickName);
        if (safeId == "unknown" && !string.IsNullOrEmpty(playerNickName))
        {
            LOG.Warning($"Player nickname '{playerNickName}' was fully sanitized to fallback 'unknown'.");
        }

        return Path.Combine(BaseFolderPath, $"{PlayerFolderPrefix}{safeId}");
    }

    public string[] GetAllPlayerNickNames()
    {
        if (!Directory.Exists(BaseFolderPath)) return [];

        return Directory.GetDirectories(BaseFolderPath)
            .Select(Path.GetFileName)
            .Where(name => name != null && name.StartsWith(PlayerFolderPrefix))
            .Select(name => name!.Substring(PlayerFolderPrefix.Length))
            .ToArray();
    }

    #endregion

    #region Save Operations

    public async Task<string> SaveAsync(float[] audioData, int sampleRate, string playerNickName)
    {
        var playerFolder = GetPlayerFolder(playerNickName);
        Directory.CreateDirectory(playerFolder);

        await EnsureCapacityAsync(playerNickName);

        var filePath = Path.Combine(playerFolder, $"sample_{DateTime.Now.Ticks}.wav");
        var audioBytes = AudioFilters.ConvertFloatsToBytes(audioData);

        await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        await using var writer = new BinaryWriter(fileStream);

        WriteWavHeader(writer, audioData.Length, sampleRate);
        writer.Write(audioBytes);

        LOG.Debug($"Audio saved for player {playerNickName}: {Path.GetFileName(filePath)}");
        return filePath;
    }

    /// <summary>
    ///     For a Voice Sample received from another player: the bytes already hold the whole WAV, header included.
    /// </summary>
    public async Task<string> SaveFromBytesAsync(byte[] audioBytes, int sampleRate, string playerNickName)
    {
        var playerFolder = GetPlayerFolder(playerNickName);
        Directory.CreateDirectory(playerFolder);

        await EnsureCapacityAsync(playerNickName);

        var filePath = Path.Combine(playerFolder, $"sample_{DateTime.Now.Ticks}.wav");

        await File.WriteAllBytesAsync(filePath, audioBytes);

        LOG.Debug($"Audio saved from bytes for player {playerNickName}: {Path.GetFileName(filePath)}");
        return filePath;
    }

    /// <summary>
    ///     Drops the oldest Voice Samples until one more fits in the Voice Bank.
    /// </summary>
    private async Task EnsureCapacityAsync(string playerNickName)
    {
        var playerFolder = GetPlayerFolder(playerNickName);
        if (!Directory.Exists(playerFolder))
        {
            return;
        }

        var files = Directory.GetFiles(playerFolder, WavExtension)
            .OrderBy(File.GetCreationTime)
            .ToList();

        while (files.Count >= MaxSamplesPerPlayer)
        {
            var oldestFile = files[0];
            await Task.Run(() => File.Delete(oldestFile));
            LOG.Debug($"Deleted oldest audio file for player {playerNickName}: {Path.GetFileName(oldestFile)}");
            files.RemoveAt(0);
        }
    }

    #endregion
}
