#nullable disable
using HarmonyLib;
using Photon.Pun;
using Photon.Voice;
using VepMod.VepFramework;

namespace VepMod.Patchs;

[HarmonyPatch(typeof(LocalVoiceFramed<short>), "PushDataAsync")]
internal class LocalVoiceFramedPatch
{
    private static readonly VepLogger LOG = VepLogger.Create<LocalVoiceFramedPatch>();

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
        var photonIsNotMine = mimics.PhotonView == null || !mimics.PhotonView.IsMine;
        return isMimicsGameObjectNull || photonIsNotMine;
    }
}