using HarmonyLib;

namespace VepMod.Scripts.Patchs;

[HarmonyPatch(typeof(EnemyDirector), "FirstSpawnPointAdd")]
public class EnemyLoggerPatch
{
    [HarmonyPrefix]
    private static void Prefix(EnemyParent _enemyParent)
    {
        VepMod.Logger.LogInfo($"EnemyDirector.FirstSpawnPointAdd: {_enemyParent} added as first spawn point.");
    }
}