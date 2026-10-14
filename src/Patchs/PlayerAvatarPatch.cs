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

        var mimics = __instance.gameObject.AddComponent<WhispralMimics>();
        __instance.gameObject.AddComponent<VoiceSampleSharing>();
        LOG.Debug($"Mimics and Voice Sample sharing added to PlayerAvatar: {__instance.name}");

        var photonView = __instance.GetComponent<PhotonView>();
        if (!photonView || !photonView.IsMine)
        {
            LOG.Warning("PhotonView is null or not owned by local player.");
            return;
        }

        VepFinder.EnsureInitialized();
        VepFinder.LocalMimics = mimics;
        LOG.Info($"Set LocalMimics for local PlayerAvatar: {__instance.name}");
    }
}
