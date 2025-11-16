using HarmonyLib;

namespace VepMod.Scripts.Patchs;

[HarmonyPatch(typeof(ValuableDirector), "Spawn")]
public class ValuableLoggerPatch
{
    [HarmonyPrefix]
    private static void Prefix(PrefabRef _valuable, ValuableVolume _volume, string _path)
    {
        // if (_valuable == null) return;
        // VepMod.Logger.LogInfo($"ValuableDirector.Spawn: {_valuable.PrefabName} via path {_path}.");
        // var lower = _valuable.PrefabName.ToLower();
        // if (lower.Contains("testcube"))
        // {
        //     VepMod.Logger.LogInfo($"Recognized VepMod valuable spawn: {_valuable.PrefabName} via path {_path}.");
        // }
    }
}