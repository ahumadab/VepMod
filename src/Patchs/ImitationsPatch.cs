using HarmonyLib;
using UnityEngine;
using VepMod.Enemies.Whispral;

namespace VepMod.Patchs;

// What Vanish cannot reach through the Imitated Player's own objects: sounds played through new AudioSources or the
// shared Materials, and the death head seen reaction.
[HarmonyPatch]
internal static class ImitationsPatch
{
    // Held above zero, the death head's cooldown never runs out, so the head seen reaction cannot fire.
    private const float SeenCooldownHold = 1f; // s

    private static Imitations? LocalImitations =>
        PlayerAvatar.instance ? PlayerAvatar.instance.GetComponent<Imitations>() : null;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Materials), nameof(Materials.Impulse))]
    private static bool Impulse_Prefix(Materials.MaterialTrigger materialTrigger)
    {
        return !IsImitatedPlayerSound(materialTrigger);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Materials), nameof(Materials.Slide))]
    private static bool Slide_Prefix(Materials.MaterialTrigger materialTrigger)
    {
        return !IsImitatedPlayerSound(materialTrigger);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Materials), nameof(Materials.SlideLoop))]
    private static bool SlideLoop_Prefix(Materials.MaterialTrigger materialTrigger)
    {
        return !IsImitatedPlayerSound(materialTrigger);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Sound), nameof(Sound.Play), typeof(Vector3), typeof(float), typeof(float), typeof(float),
        typeof(float))]
    private static void Play_Postfix(Sound __instance, AudioSource? __result)
    {
        MuteIfImitated(__instance, __result, __instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Sound), nameof(Sound.Play), typeof(Transform), typeof(float), typeof(float), typeof(float),
        typeof(float))]
    private static void PlayFollowing_Postfix(Sound __instance, AudioSource? __result, Transform followTarget)
    {
        MuteIfImitated(__instance, __result, followTarget);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Sound), nameof(Sound.Play), typeof(Transform), typeof(Vector3), typeof(float), typeof(float),
        typeof(float), typeof(float))]
    private static void PlayFollowingAtContact_Postfix(Sound __instance, AudioSource? __result, Transform followTarget)
    {
        MuteIfImitated(__instance, __result, followTarget);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(PlayerDeathHead), "Update")]
    private static void DeathHeadUpdate_Prefix(PlayerDeathHead __instance, ref float ___seenCooldownTimer)
    {
        if (___seenCooldownTimer >= SeenCooldownHold || !__instance.playerAvatar) return;

        var imitations = LocalImitations;
        if (imitations && imitations!.IsImitatedPlayer(__instance.playerAvatar))
        {
            ___seenCooldownTimer = SeenCooldownHold;
        }
    }

    // Without a Source of its own, a Sound plays through a new AudioSource destroyed once the clip ends; a Sound with
    // one plays through the player's AudioSource, which Vanish mutes already.
    private static void MuteIfImitated(Sound sound, AudioSource? played, object emitter)
    {
        if (played && !played!.mute && !sound.Source && IsImitatedPlayerSound(emitter))
        {
            played.mute = true;
        }
    }

    private static bool IsImitatedPlayerSound(object emitter)
    {
        var imitations = LocalImitations;
        return imitations && imitations!.IsImitatedPlayerSound(emitter);
    }
}
