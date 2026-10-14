using System;
using HarmonyLib;
using VepMod.Enemies.Whispral;
using VepMod.VepFramework;

namespace VepMod.Patchs;

/// <summary>
///     The game's buffered RPC that gives an avatar its nickname and Steam ID, on every client and for every player,
///     late joiners included: the first moment this client knows both.
/// </summary>
[HarmonyPatch(typeof(PlayerAvatar), nameof(PlayerAvatar.AddToStatsManagerRPC))]
internal class VoiceBankMigrationPatch
{
    private static readonly VepLogger LOG = VepLogger.Create<VoiceBankMigrationPatch>();

    private static void Postfix(PlayerAvatar __instance)
    {
        var player = WhispralMimics.VoiceBankKey(__instance);
        try
        {
            var count = VepMod.VoiceBanks.MigrateLegacyBank(player);
            if (count != null)
            {
                LOG.Debug(
                    $"Legacy Voice Bank of nickname {player.Nickname} migrated to Steam ID {player.SteamId}: {count} Voice Samples after merge.");
            }
        }
        catch (Exception ex)
        {
            LOG.Warning($"Could not migrate the legacy Voice Bank of {player}: {ex.Message}");
        }
    }
}
