namespace VepMod.Enemies.Whispral;

// Beside VoiceBanks.cs, which stays plain C# for its tests.
internal static class VoiceBankKeys
{
    public static PlayerKey VoiceBankKey(this PlayerAvatar player)
    {
        return new PlayerKey(player.steamID, player.playerName);
    }
}
