using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using VepMod.VepFramework;
using Random = UnityEngine.Random;

namespace VepMod.Enemies.Whispral;

/// <summary>
///     Gestion des fichiers audio WAV par joueur (lecture, écriture, nettoyage).
///     Structure: AudioFiles/player_{playerNickName}/sample_*.wav
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

    #region WAV Header

    /// <summary>
    ///     Écrit l'en-tête WAV standard (PCM 16-bit mono).
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

    #region Read Operations

    /// <summary>
    ///     Lit un fichier WAV aléatoire pour un joueur spécifique.
    /// </summary>
    public byte[]? GetRandomFile(string playerNickName)
    {
        var playerFolder = GetPlayerFolder(playerNickName);
        if (!Directory.Exists(playerFolder))
        {
            return null;
        }

        var files = Directory.GetFiles(playerFolder, WavExtension);
        if (files.Length == 0)
        {
            LOG.Debug($"No audio files found for player {playerNickName}.");
            return null;
        }

        var selectedFile = files[Random.Range(0, files.Length)];
        return File.ReadAllBytes(selectedFile);
    }

    #endregion

    #region Player Folder Management

    /// <summary>
    ///     Retourne le chemin du dossier pour un joueur spécifique.
    /// </summary>
    private string GetPlayerFolder(string playerNickName)
    {
        var safeId = SanitizePlayerNickName(playerNickName);
        return Path.Combine(BaseFolderPath, $"{PlayerFolderPrefix}{safeId}");
    }

    /// <summary>
    ///     Nettoie l'ID joueur pour éviter les caractères invalides dans les noms de dossier.
    /// </summary>
    private static string SanitizePlayerNickName(string playerNickName)
    {
        if (string.IsNullOrEmpty(playerNickName))
        {
            LOG.Warning("Player nickname is null or empty. Using 'unknown' as fallback.");
            return "unknown";
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Join("_", playerNickName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    ///     Retourne la liste des IDs de tous les joueurs ayant des sons stockés.
    /// </summary>
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

    /// <summary>
    ///     Sauvegarde des samples audio dans un fichier WAV pour un joueur spécifique.
    /// </summary>
    public async Task<string> SaveAsync(float[] audioData, int sampleRate, string playerNickName)
    {
        var playerFolder = GetPlayerFolder(playerNickName);
        Directory.CreateDirectory(playerFolder);

        // Rotation: supprimer le plus ancien si à capacité
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
    ///     Sauvegarde des données audio brutes (bytes) pour un joueur spécifique.
    ///     Utilisé pour sauvegarder les sons reçus d'autres joueurs.
    /// </summary>
    public async Task<string> SaveFromBytesAsync(byte[] audioBytes, int sampleRate, string playerNickName)
    {
        var playerFolder = GetPlayerFolder(playerNickName);
        Directory.CreateDirectory(playerFolder);

        // Rotation: supprimer le plus ancien si à capacité
        await EnsureCapacityAsync(playerNickName);

        var filePath = Path.Combine(playerFolder, $"sample_{DateTime.Now.Ticks}.wav");

        // Les bytes contiennent déjà l'en-tête WAV complet, donc on écrit directement
        await File.WriteAllBytesAsync(filePath, audioBytes);

        LOG.Debug($"Audio saved from bytes for player {playerNickName}: {Path.GetFileName(filePath)}");
        return filePath;
    }

    /// <summary>
    ///     S'assure que le dossier du joueur ne dépasse pas la capacité max.
    ///     Supprime les fichiers les plus anciens si nécessaire.
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