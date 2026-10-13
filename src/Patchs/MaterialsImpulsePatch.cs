using HarmonyLib;
using UnityEngine;
using VepMod.Enemies.Whispral;

namespace VepMod.Patchs;

// Silences the footsteps of the players Vanish hides.
[HarmonyPatch(typeof(Materials))]
internal static class MaterialsImpulsePatch
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(Materials.Impulse))]
    private static bool Impulse_Prefix(
        Vector3 origin,
        bool footstep,
        Materials.HostType hostType)
    {
        if (!footstep) return true;

        if (hostType != Materials.HostType.LocalPlayer && hostType != Materials.HostType.OtherPlayer) return true;

        var imitations = PlayerAvatar.instance ? PlayerAvatar.instance.GetComponent<Imitations>() : null;
        return !imitations || !imitations!.IsNearImitatedPlayer(origin);
    }
}
