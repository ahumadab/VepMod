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

        var mimics = __instance.GetComponent<WhispralMimics>();
        if (!mimics)
        {
            mimics = __instance.gameObject.AddComponent<WhispralMimics>();
            LOG.Debug($"Added Mimics component to PlayerAvatar: {__instance.name}");
        }
        else
        {
            LOG.Warning($"PlayerAvatar: {__instance.name} already has a WhispralMimics component.");
        }

        var photonView = __instance.GetComponent<PhotonView>();
        if (!photonView || !photonView.IsMine)
        {
            LOG.Warning("PhotonView is null or not owned by local player.");
            return;
        }

        VepFinder.LocalMimics = mimics;
        LOG.Info($"Set LocalMimics for local PlayerAvatar: {__instance.name}");
    }
}