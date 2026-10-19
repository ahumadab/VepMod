using HarmonyLib;
using VepMod.Enemies.Whispral;

namespace VepMod.Patchs;

// Every room the game creates or joins goes through NetworkConnect.
[HarmonyPatch(typeof(NetworkConnect), nameof(NetworkConnect.OnJoinedRoom))]
internal static class JoinedRoomPatch
{
    private static void Postfix()
    {
        LethalHallucinations.OnJoinedRoom();
    }
}
