#nullable disable
using HarmonyLib;
using Photon.Voice;

namespace VepMod.Patchs;

// Runs on Photon's voice thread.
[HarmonyPatch(typeof(LocalVoiceFramed<short>), "PushDataAsync")]
internal class LocalVoiceFramedPatch
{
    private static void Prefix(short[] buf, LocalVoiceFramed<short> __instance)
    {
        // A reference check: Unity's == on a UnityEngine.Object is a Unity API.
        var voiceChat = PlayerVoiceChat.instance;
        if (ReferenceEquals(voiceChat, null)) return;
        // The microphone's format, not the encoder's: it is the real format of buf.
        var (sampleRate, channels) = VoiceFormatResolver.Resolve(__instance, buf.Length);
        VepMod.VoiceRecorder.PushFrame(voiceChat.isTalking, buf, sampleRate, channels);
    }
}
