using HarmonyLib;
using VepMod.Enemies.Whispral;

namespace VepMod.Patchs;

// A Hallucination's fake PlayerAvatar (HallucinationBody) runs none of its own code. Its lifecycle never runs on a
// GameObject that is never activated, and is guarded in case it ever did: it would join GameDirector.PlayerList, or tell
// every enemy the Imitated Player left. PlayerAvatarVisuals forwards its animation events to it, and a footstep on the
// Master makes enemies investigate (ADR-0002).
[HarmonyPatch]
internal static class FakePlayerAvatarPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(PlayerAvatar), "Awake")]
    private static bool Awake_Prefix(PlayerAvatar __instance)
    {
        return !HallucinationBody.IsFake(__instance, "Awake");
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(PlayerAvatar), "Start")]
    private static bool Start_Prefix(PlayerAvatar __instance)
    {
        return !HallucinationBody.IsFake(__instance, "Start");
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(PlayerAvatar), "OnDestroy")]
    private static bool OnDestroy_Prefix(PlayerAvatar __instance)
    {
        return !HallucinationBody.IsFake(__instance, "OnDestroy");
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(PlayerAvatar), nameof(PlayerAvatar.Footstep))]
    private static bool Footstep_Prefix(PlayerAvatar __instance)
    {
        return !HallucinationBody.IsFake(__instance, nameof(PlayerAvatar.Footstep));
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(PlayerAvatar), nameof(PlayerAvatar.StandToCrouch))]
    private static bool StandToCrouch_Prefix(PlayerAvatar __instance)
    {
        return !HallucinationBody.IsFake(__instance, nameof(PlayerAvatar.StandToCrouch));
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(PlayerAvatar), nameof(PlayerAvatar.CrouchToStand))]
    private static bool CrouchToStand_Prefix(PlayerAvatar __instance)
    {
        return !HallucinationBody.IsFake(__instance, nameof(PlayerAvatar.CrouchToStand));
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(PlayerAvatar), nameof(PlayerAvatar.CrouchToCrawl))]
    private static bool CrouchToCrawl_Prefix(PlayerAvatar __instance)
    {
        return !HallucinationBody.IsFake(__instance, nameof(PlayerAvatar.CrouchToCrawl));
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(PlayerAvatar), nameof(PlayerAvatar.CrawlToCrouch))]
    private static bool CrawlToCrouch_Prefix(PlayerAvatar __instance)
    {
        return !HallucinationBody.IsFake(__instance, nameof(PlayerAvatar.CrawlToCrouch));
    }
}
