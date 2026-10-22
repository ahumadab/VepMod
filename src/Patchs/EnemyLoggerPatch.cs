using HarmonyLib;
using VepMod.VepFramework;

namespace VepMod.Patchs;

[HarmonyPatch(typeof(EnemyDirector), "FirstSpawnPointAdd")]
public class EnemyLoggerPatch
{
    private static readonly VepLogger LOG = VepLogger.Create<EnemyLoggerPatch>();

    [HarmonyPrefix]
    private static void Prefix(EnemyParent _enemyParent)
    {
        LOG.Info($"EnemyDirector.FirstSpawnPointAdd: {_enemyParent} added as first spawn point.");
    }
}

// The game's end-of-level rule respawns and despawns every enemy, Whisprals included: a timing seen in a test log
// reads against this line.
[HarmonyPatch(typeof(RoundDirector), "ExtractionCompletedAllRPC")]
public class ExtractionsCompletedLoggerPatch
{
    private static readonly VepLogger LOG = VepLogger.Create<ExtractionsCompletedLoggerPatch>();

    [HarmonyPostfix]
    private static void Postfix(RoundDirector __instance)
    {
        if (__instance.allExtractionPointsCompleted)
            LOG.Debug("RoundDirector: every extraction point is completed, the end-of-level rule now moves every enemy.");
    }
}
