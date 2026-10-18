using Photon.Pun;
using VepMod.VepFramework;

namespace VepMod.Enemies.Whispral;

/// <summary>
///     The Master's "Lethal Hallucinations" option, which every client follows: whether a Stalking the Victim never
///     looks at ends in a Lunge.
/// </summary>
internal static class LethalHallucinations
{
    // A wire contract: clients of other mod versions read it.
    private const string RoomPropertyKey = "VepMod.LethalHallucinations";

    private static readonly VepLogger LOG = VepLogger.Create(nameof(LethalHallucinations));

    /// <summary>
    ///     The Master's value; on when the Master publishes none (an older mod version), the local value outside a
    ///     room (singleplayer).
    /// </summary>
    public static bool IsOn => PhotonNetwork.CurrentRoom is { } room
        ? room.CustomProperties[RoomPropertyKey] is not false
        : VepMod.ConfigLethalHallucinations.Value;

    public static string Summary =>
        $"{OnOff(IsOn)} as the Master set it, local value {OnOff(VepMod.ConfigLethalHallucinations.Value)}";

    public static void Init()
    {
        VepMod.ConfigLethalHallucinations.SettingChanged += (_, _) => PublishIfMaster("its setting changed");
    }

    // The game ends the session when the Master leaves (NetworkManager.OnMasterClientSwitched): no client ever takes
    // a room over, so joining a room and changing the setting are the only times the Master publishes.
    public static void OnJoinedRoom()
    {
        if (PhotonNetwork.IsMasterClient) PublishIfMaster("it joined the room");
        else LOG.Debug($"Lethal Hallucinations read on joining the room: {Summary}");
    }

    private static void PublishIfMaster(string reason)
    {
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient) return;

        var value = VepMod.ConfigLethalHallucinations.Value;
        SemiFunc.SetCurrentRoomProperty(RoomPropertyKey, value);
        LOG.Debug($"The Master publishes Lethal Hallucinations {OnOff(value)}: {reason}");
    }

    private static string OnOff(bool value)
    {
        return value ? "on" : "off";
    }
}
