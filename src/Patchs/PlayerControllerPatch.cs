using HarmonyLib;

namespace VepMod.Patchs;

[HarmonyPatch(typeof(PlayerController))]
internal static class PlayerControllerPatch
{
    // private static readonly ManualLogSource log = Logger.CreateLogSource("VepMod.PlayerControllerPatch");
    //
    // [HarmonyPrefix]
    // [HarmonyPatch(nameof(PlayerController.Start))]
    // private static void Start_Prefix(PlayerController __instance)
    // {
    //     // Code to execute for each PlayerController *before* Start() is called.
    //     log.LogDebug($"{__instance} Start Prefix");
    // }
    //
    // [HarmonyPostfix]
    // [HarmonyPatch(nameof(PlayerController.Start))]
    // private static void Start_Postfix(PlayerController __instance)
    // {
    //     // Code to execute for each PlayerController *after* Start() is called.
    //     log.LogDebug($"{__instance} Start Postfix");
    // }
}