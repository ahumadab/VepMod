#nullable disable
using BepInEx.Logging;
using HarmonyLib;
using Photon.Pun;
using Photon.Voice;

namespace VepMod.Scripts.Patchs;

[HarmonyPatch(typeof(LocalVoiceFramed<short>), "PushDataAsync")]
internal class LocalVoiceFramedPatch
{
    private static readonly ManualLogSource LOG = Logger.CreateLogSource("VepMod.LocalVoiceFramedPatch");

    private static void Prefix(short[] buf, LocalVoiceFramed<short> __instance)
    {
        VepFinder.EnsureInitialized();
        if (IsNotReady()) return;
        VepFinder.LocalMimics.ProcessVoiceData(buf);
    }

    private static bool IsNotReady()
    {
        var isNotConnectedAndReady = !PhotonNetwork.IsConnectedAndReady;
        var mimics = VepFinder.LocalMimics;
        var isLocalMimicsNull = mimics == null;
        if (isNotConnectedAndReady || isLocalMimicsNull) return true;
        var isMimicsGameObjectNull = mimics.gameObject == null;
        var photonIsNotMine = mimics.photonView == null || !mimics.photonView.IsMine;
        return isMimicsGameObjectNull || photonIsNotMine;
    }
}