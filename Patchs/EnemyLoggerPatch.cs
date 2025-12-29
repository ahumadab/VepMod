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