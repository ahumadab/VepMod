using HarmonyLib;
using VepMod.VepFramework;

namespace VepMod.Patchs;

[HarmonyPatch(typeof(PlayerAvatar), nameof(PlayerAvatar.SetSpectate))]
internal static class SpectatePatch
{
    private static readonly VepLogger LOG = VepLogger.Create(nameof(SpectatePatch));

    // A camera override the death left running would carry over into the spectate camera.
    private static void Postfix()
    {
        LOG.Debug($"Spectate starts: camera aim override {(CameraAim.Instance.AimTargetActive ? "on" : "off")}, " +
                  $"zoom override {(CameraZoom.Instance.OverrideActive ? "on" : "off")}");
    }
}
