using System;
using System.IO;
using System.Linq;
using VepMod.VepFramework.Extensions;

namespace VepMod.Enemies.Whispral;

/// <summary>
///     The player a Voice Bank belongs to: their Steam ID, or their nickname when the game has none (ADR-0006).
/// </summary>
public readonly struct PlayerKey(string? steamId, string? nickname)
{
    /// <summary>Null when the game has no Steam ID for the player (<c>"0"</c> or empty, as in a local test without Steam).</summary>
    public string? SteamId { get; } = string.IsNullOrEmpty(steamId) || steamId == "0" ? null : steamId;

    public string Nickname { get; } = nickname ?? "";

    public override string ToString()
    {
        return SteamId == null ? $"{Nickname} (no Steam ID)" : $"{Nickname} (Steam ID {SteamId})";
    }
}

/// <summary>
///     Every Voice Bank this client holds, one folder per player under <see cref="RootFolder" />, one WAV per Voice
///     Sample. Thread-safe: Voice Samples are saved from background tasks.
/// </summary>
/// <param name="samplesPerPlayer">The cap of each Voice Bank, read at every save.</param>
public sealed class VoiceBanks(string rootFolder, Func<int> samplesPerPlayer)
{
    // Distinct prefixes, so a nickname can never name a Steam ID's folder.
    private const string SteamIdPrefix = "steam_";
    private const string NicknamePrefix = "player_";
    private const string SamplePattern = "*.wav";

    private readonly object _lock = new();
    private readonly Random _random = new();

    public string RootFolder { get; } = rootFolder;

    public void Save(PlayerKey player, VoiceSample sample)
    {
        var wav = WavCodec.Encode(sample);
        lock (_lock)
        {
            var folder = BankFolder(player);
            Directory.CreateDirectory(folder);

            // The file name orders a Voice Bank by age. Mono's DateTime.Now ticks in steps of several milliseconds.
            var ticks = DateTime.Now.Ticks;
            while (File.Exists(SamplePath(folder, ticks))) ticks++;
            File.WriteAllBytes(SamplePath(folder, ticks), wav);

            Cap(folder);
        }
    }

    /// <returns>Null when the player's Voice Bank is empty.</returns>
    /// <exception cref="InvalidDataException">The picked file is not a Voice Sample.</exception>
    public VoiceSample? PickRandom(PlayerKey player)
    {
        lock (_lock)
        {
            var files = SampleFiles(BankFolder(player));
            return files.Length == 0 ? null : WavCodec.Decode(File.ReadAllBytes(files[_random.Next(files.Length)]));
        }
    }

    // ponytail: moves the nickname Voice Banks of the versions before ADR-0006; remove it once a release decides to
    // drop them and delete what is left (ADR-0006).
    /// <summary>
    ///     Moves the Voice Bank older versions kept under the player's nickname into the one under their Steam ID, then
    ///     caps it. The first player seen with a nickname takes the whole bank.
    /// </summary>
    /// <returns>The player's Voice Sample count after the merge, or null when there was nothing to migrate.</returns>
    public int? MigrateLegacyBank(PlayerKey player)
    {
        if (player.SteamId == null) return null;

        lock (_lock)
        {
            var legacyFolder = NicknameFolder(player);
            if (!Directory.Exists(legacyFolder)) return null;

            var folder = BankFolder(player);
            Directory.CreateDirectory(folder);
            foreach (var file in SampleFiles(legacyFolder))
            {
                var destination = Path.Combine(folder, Path.GetFileName(file));
                if (!File.Exists(destination)) File.Move(file, destination);
            }

            Directory.Delete(legacyFolder, true);
            Cap(folder);
            return SampleFiles(folder).Length;
        }
    }

    private void Cap(string folder)
    {
        var files = SampleFiles(folder);
        for (var i = 0; i < files.Length - samplesPerPlayer(); i++) File.Delete(files[i]);
    }

    private string BankFolder(PlayerKey player)
    {
        return player.SteamId == null
            ? NicknameFolder(player)
            : Path.Combine(RootFolder, SteamIdPrefix + FileSystemSanitizer.Sanitize(player.SteamId));
    }

    private string NicknameFolder(PlayerKey player)
    {
        return Path.Combine(RootFolder, NicknamePrefix + FileSystemSanitizer.Sanitize(player.Nickname));
    }

    private static string SamplePath(string folder, long ticks)
    {
        return Path.Combine(folder, $"sample_{ticks:D18}.wav");
    }

    /// <returns>Oldest first.</returns>
    private static string[] SampleFiles(string folder)
    {
        return Directory.Exists(folder)
            ? Directory.GetFiles(folder, SamplePattern).OrderBy(file => file, StringComparer.Ordinal).ToArray()
            : [];
    }
}
