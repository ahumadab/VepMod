namespace VepMod.Enemies.Whispral;

/// <summary>
///     The rules that keep the Whispral in the level (ADR-0008): its spawn timer never runs out, a death keeps it away
///     2 to 3 min, and each roam heads for the team.
/// </summary>
internal static class Presence
{
    private const float RefillBelow = 1f; // s
    private const float RefilledTimer = 20f; // s
    private const float MinReturnDelay = 120f; // s
    private const float MaxReturnDelay = 180f; // s

    internal enum RoamPointSource
    {
        NearTheTeam,
        AroundItself,
        Anywhere
    }

    /// <param name="spawnTimer">The game's spawn timer (<c>EnemyParent.SpawnedTimer</c>), in seconds.</param>
    /// <returns>The spawn timer to put back, or null to leave the game's.</returns>
    public static float? RefilledSpawnTimer(float spawnTimer)
    {
        // Only a direct SpawnedTimerSet(0f), the game's end-of-level rule, leaves the timer at exactly zero: the
        // Whispral obeys it. Refilled each frame, the timer never ticks down that far.
        if (spawnTimer == 0f || spawnTimer >= RefillBelow) return null;
        return RefilledTimer;
    }

    /// <param name="roll">A random value in [0, 1].</param>
    /// <returns>How long a killed Whispral stays away, in seconds.</returns>
    public static float ReturnDelay(float roll)
    {
        return MinReturnDelay + (MaxReturnDelay - MinReturnDelay) * roll;
    }

    /// <returns>The first of the points that exists, with where it came from; null when none does.</returns>
    public static (T Point, RoamPointSource Source)? RoamPoint<T>(T? nearTheTeam, T? aroundItself, T? anywhere)
        where T : class
    {
        if (nearTheTeam != null) return (nearTheTeam, RoamPointSource.NearTheTeam);
        if (aroundItself != null) return (aroundItself, RoamPointSource.AroundItself);
        if (anywhere != null) return (anywhere, RoamPointSource.Anywhere);
        return null;
    }
}
