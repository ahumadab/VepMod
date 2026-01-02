using HarmonyLib;
using UnityEngine;
using VepMod.Enemies.Whispral;

namespace VepMod.Patchs;

/// <summary>
///     Patch pour bloquer les sons de pas des joueurs cachés par InvisibleDebuff.
/// </summary>
[HarmonyPatch(typeof(Materials))]
internal static class MaterialsImpulsePatch
{
    /// <summary>
    ///     Intercepte Materials.Impulse pour bloquer les sons de pas des joueurs invisibles.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(nameof(Materials.Impulse))]
    private static bool Impulse_Prefix(
        Vector3 origin,
        bool footstep,
        Materials.HostType hostType)
    {
        // On ne bloque que les sons de pas
        if (!footstep) return true;

        // On ne bloque que les sons des joueurs (pas les ennemis, etc.)
        if (hostType != Materials.HostType.LocalPlayer && hostType != Materials.HostType.OtherPlayer) return true;

        // Vérifier si la position correspond à un joueur caché
        if (InvisibleDebuff.IsPositionNearHiddenPlayer(origin))
        {
            return false; // Bloquer le son
        }

        return true; // Laisser passer
    }
}