using HarmonyLib;
using VepMod.Enemies.Whispral;

namespace VepMod.Patchs;

// Silences the footsteps of the players Vanish hides.
[HarmonyPatch(typeof(Materials))]
internal static class MaterialsImpulsePatch
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(Materials.Impulse))]
    private static bool Impulse_Prefix(bool footstep, Materials.MaterialTrigger materialTrigger)
    {
        if (!footstep) return true;

        var imitations = PlayerAvatar.instance ? PlayerAvatar.instance.GetComponent<Imitations>() : null;
        return !imitations || !imitations!.IsImitatedPlayerFootstep(materialTrigger);
    }
}
