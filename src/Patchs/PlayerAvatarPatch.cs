#nullable disable
using HarmonyLib;
using Photon.Pun;
using VepMod.Enemies.Whispral;
using VepMod.VepFramework;

namespace VepMod.Patchs;

[HarmonyPatch(typeof(PlayerAvatar), "Awake")]
internal class PlayerAvatarPatch
{
    private static readonly VepLogger LOG = VepLogger.Create<PlayerAvatarPatch>();

    private static void Postfix(PlayerAvatar __instance)
    {
        if (!PhotonNetwork.IsConnectedAndReady)
        {
            return;
        }

        __instance.gameObject.AddComponent<VoiceSampleSharing>();
        LOG.Debug($"Voice Sample sharing added to PlayerAvatar: {__instance.name}");
    }
}
