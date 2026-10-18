using HarmonyLib;
using VepMod.VepFramework;

namespace VepMod.Patchs;

[HarmonyPatch(typeof(PlayerAvatar), nameof(PlayerAvatar.SetSpectate))]
internal static class SpectatePatch
{
    private static readonly VepLogger LOG = VepLogger.Create(nameof(SpectatePatch));

    // Proves that the camera overrides a death sets (a Lunge's aim and zoom) are over once the spectate camera starts.
    private static void Postfix()
    {
        LOG.Debug($"Spectate starts: camera aim override {(CameraAim.Instance.AimTargetActive ? "on" : "off")}, " +
                  $"zoom override {(CameraZoom.Instance.OverrideActive ? "on" : "off")}");
    }
}
