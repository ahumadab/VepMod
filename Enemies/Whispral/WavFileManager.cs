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
///     Gestion des fichiers audio WAV par joueur (lecture, écriture, nettoyage).
///     Structure: AudioFiles/player_{playerNickName}/sample_*.wav
/// </summary>
public sealed class WavFileManager
{
    private const string WavExtension = "*.wav";
    private const string AudioFolderName = "AudioFiles";
    private const string PlayerFolderPrefix = "player_";
    private static readonly ManualLogSource LOG = Logger.CreateLogSource($"VepMod.{nameof(WavFileManager)}");

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

        LOG.LogInfo($"Audio saved for player {playerNickName}: {Path.GetFileName(filePath)}");
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

        LOG.LogInfo($"Audio saved from bytes for player {playerNickName}: {Path.GetFileName(filePath)}");
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
            LOG.LogInfo($"Deleted oldest audio file for player {playerNickName}: {Path.GetFileName(oldestFile)}");
            files.RemoveAt(0);
        }
    }

    #endregion

    #region Read Operations

    /// <summary>
    ///     Lit un fichier WAV aléatoire pour un joueur spécifique.
    /// </summary>
    private async Task<byte[]?> GetRandomFileAsync(string playerNickName)
    {
        var playerFolder = GetPlayerFolder(playerNickName);
        if (!Directory.Exists(playerFolder))
        {
            return null;
        }

        var files = Directory.GetFiles(playerFolder, WavExtension);
        if (files.Length == 0)
        {
            return null;
        }

        var selectedFile = files[Random.Range(0, files.Length)];
        return await File.ReadAllBytesAsync(selectedFile);
    }

    /// <summary>
    ///     Lit un fichier WAV aléatoire parmi tous les joueurs, avec possibilité d'exclusion.
    /// </summary>
    public async Task<(byte[] audioData, string playerNickName)?> GetRandomFileFromAnyPlayerAsync(
        string[]? excludePlayerNickNames = null)
    {
        var playerNickNames = GetAllPlayerNickNames()
            .Where(id => excludePlayerNickNames == null || !excludePlayerNickNames.Contains(id))
            .ToArray();

        if (playerNickNames.Length == 0)
        {
            return null;
        }

        var randomPlayerNickName = playerNickNames[Random.Range(0, playerNickNames.Length)];
        var audioData = await GetRandomFileAsync(randomPlayerNickName);

        return audioData != null ? (audioData, randomPlayerNickName) : null;
    }

    /// <summary>
    ///     Version synchrone de GetRandomFileAsync pour les contextes où async n'est pas possible.
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
            return null;
        }

        var selectedFile = files[Random.Range(0, files.Length)];
        return File.ReadAllBytes(selectedFile);
    }

    #endregion

    #region Count & Capacity

    /// <summary>
    ///     Retourne le nombre de fichiers WAV pour un joueur spécifique.
    /// </summary>
    private int GetFileCountForPlayer(string playerNickName)
    {
        var playerFolder = GetPlayerFolder(playerNickName);
        if (!Directory.Exists(playerFolder))
        {
            return 0;
        }

        return Directory.GetFiles(playerFolder, WavExtension).Length;
    }

    /// <summary>
    ///     Vérifie si le dossier d'un joueur a atteint la limite de fichiers.
    /// </summary>
    public bool IsAtCapacityForPlayer(string playerNickName)
    {
        return GetFileCountForPlayer(playerNickName) >= MaxSamplesPerPlayer;
    }

    /// <summary>
    ///     Retourne le nombre total de fichiers WAV pour tous les joueurs.
    /// </summary>
    public int GetTotalFileCount()
    {
        return GetAllPlayerNickNames().Sum(GetFileCountForPlayer);
    }

    #endregion

    #region Clear Operations

    /// <summary>
    ///     Supprime tous les fichiers WAV pour un joueur spécifique.
    /// </summary>
    private async Task ClearPlayerAsync(string playerNickName)
    {
        var playerFolder = GetPlayerFolder(playerNickName);
        if (!Directory.Exists(playerFolder))
        {
            return;
        }

        var files = Directory.GetFiles(playerFolder, WavExtension);
        var tasks = files.Select(file => Task.Run(() => File.Delete(file))).ToList();
        if (tasks.Count > 0)
        {
            await Task.WhenAll(tasks);
        }

        LOG.LogInfo($"Audio folder cleared for player {playerNickName}.");
    }

    /// <summary>
    ///     Supprime tous les fichiers WAV pour tous les joueurs.
    /// </summary>
    public async Task ClearAllAsync()
    {
        var playerNickNames = GetAllPlayerNickNames();
        foreach (var playerNickName in playerNickNames)
        {
            await ClearPlayerAsync(playerNickName);
        }

        LOG.LogInfo("All audio folders cleared.");
    }

    #endregion
}