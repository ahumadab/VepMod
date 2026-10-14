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

        // Two game instances on one PC write two logs: this line tells them apart.
        if (__instance.photonView.IsMine)
        {
            LOG.Info($"Local player {PhotonNetwork.LocalPlayer.NickName}, Master: {PhotonNetwork.IsMasterClient}");
        }

        __instance.gameObject.AddComponent<VoiceSampleSharing>();
        LOG.Debug($"Voice Sample sharing added to PlayerAvatar: {__instance.name}");
    }
}
