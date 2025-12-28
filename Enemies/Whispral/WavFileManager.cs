using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BepInEx.Logging;
using UnityEngine;
using Logger = BepInEx.Logging.Logger;
using Random = UnityEngine.Random;

namespace VepMod.Enemies.Whispral;

/// <summary>
///     Gestion des fichiers audio WAV (lecture, écriture, nettoyage).
/// </summary>
public class WavFileManager
{
    private const string WavExtension = "*.wav";
    private const string AudioFolderName = "AudioFiles";
    private static readonly ManualLogSource LOG = Logger.CreateLogSource("VepMod.WavFileManager");

    public WavFileManager(int maxClipCount = 10)
    {
        MaxClipCount = maxClipCount;
        FolderPath = Path.Combine(Application.dataPath, AudioFolderName);
        Directory.CreateDirectory(FolderPath);
    }

    public string FolderPath { get; }
    public int MaxClipCount { get; }

    /// <summary>
    ///     Sauvegarde des samples audio dans un fichier WAV.
    /// </summary>
    public async Task<string> SaveAsync(float[] audioData, int sampleRate)
    {
        var filePath = Path.Combine(FolderPath, $"audio_{Guid.NewGuid()}.wav");
        var audioBytes = AudioFilters.ConvertFloatsToBytes(audioData);

        await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        await using var writer = new BinaryWriter(fileStream);

        WriteWavHeader(writer, audioData.Length, sampleRate);
        writer.Write(audioBytes);

        LOG.LogInfo($"Audio saved: {filePath}");
        return filePath;
    }

    /// <summary>
    ///     Lit un fichier WAV aléatoire du dossier.
    /// </summary>
    public async Task<byte[]?> GetRandomFileAsync()
    {
        var files = Directory.GetFiles(FolderPath, WavExtension);
        if (files.Length == 0)
        {
            return null;
        }

        var selectedFile = files[Random.Range(0, files.Length)];
        return await File.ReadAllBytesAsync(selectedFile);
    }

    /// <summary>
    ///     Retourne le nombre de fichiers WAV dans le dossier.
    /// </summary>
    public int GetFileCount()
    {
        return Directory.GetFiles(FolderPath, WavExtension).Length;
    }

    /// <summary>
    ///     Vérifie si le dossier a atteint la limite de fichiers.
    /// </summary>
    public bool IsAtCapacity()
    {
        return GetFileCount() >= MaxClipCount;
    }

    /// <summary>
    ///     Supprime tous les fichiers WAV du dossier.
    /// </summary>
    public async Task ClearAsync()
    {
        var files = Directory.EnumerateFiles(FolderPath, WavExtension);
        var tasks = files.Select(file => Task.Run(() => File.Delete(file))).ToList();
        if (tasks.Count > 0)
        {
            await Task.WhenAll(tasks);
        }

        LOG.LogInfo("Audio folder cleared.");
    }

    /// <summary>
    ///     Écrit l'en-tête WAV standard (PCM 16-bit mono).
    /// </summary>
    private static void WriteWavHeader(BinaryWriter writer, int sampleCount, int sampleRate)
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
}