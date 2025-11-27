#nullable disable
using BepInEx.Logging;
using HarmonyLib;
using Photon.Pun;
using VepMod.Scripts.Enemies.Whispral;
using Logger = BepInEx.Logging.Logger;

namespace VepMod.Scripts.Patchs;

[HarmonyPatch(typeof(PlayerAvatar), "Awake")]
internal class PlayerAvatarPatch
{
    private static readonly ManualLogSource log = Logger.CreateLogSource("VepMod");

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
            log.LogInfo($"Added Mimics component to PlayerAvatar: {__instance.name}");
        }
        else
        {
            log.LogWarning($"PlayerAvatar: {__instance.name} already has a WhispralMimics component.");
        }

        var photonView = __instance.GetComponent<PhotonView>();
        if (!photonView || !photonView.IsMine)
        {
            log.LogWarning("PhotonView is null or not owned by local player.");
            return;
        }

        VepFinder.LocalMimics = mimics;
        log.LogInfo($"Set LocalMimics for local PlayerAvatar: {__instance.name}");
    }
}