using BepInEx.Logging;
using HarmonyLib;

namespace VepMod.Patchs;

[HarmonyPatch(typeof(EnemyDirector), "FirstSpawnPointAdd")]
public class EnemyLoggerPatch
{
    private static readonly ManualLogSource LOG = Logger.CreateLogSource("VepMod.EnemyLoggerPatch");

    [HarmonyPrefix]
    private static void Prefix(EnemyParent _enemyParent)
    {
        LOG.LogInfo($"EnemyDirector.FirstSpawnPointAdd: {_enemyParent} added as first spawn point.");
    }
}