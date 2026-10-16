using System;

namespace VepMod.Enemies.Whispral;

/// <summary>
///     Which Talker a Hallucination's head turns to, as the game's PlayerEyes picks one for a player: the nearest
///     within 6 m, held 2 to 4 s after they stop talking.
/// </summary>
internal sealed class TalkerLook<T> where T : class
{
    private const float Reach = 6f; // m
    private const float MinHold = 2f; // s
    private const float MaxHold = 4f; // s

    private readonly Func<float> _roll;
    private float _hold;
    private T? _nearest;
    private float _nearestDistance;

    /// <param name="roll">A random value in [0, 1].</param>
    public TalkerLook(Func<float> roll)
    {
        _roll = roll;
    }

    public T? Talker { get; private set; }

    /// <summary>
    ///     Seconds since the last Talker heard within reach stopped talking.
    /// </summary>
    public float Silence { get; private set; }

    public static bool InReach(float distance)
    {
        return distance < Reach;
    }

    /// <summary>
    ///     Called before <see cref="Update" /> for each Talker talking this frame.
    /// </summary>
    /// <param name="distance">In metres.</param>
    public void Hear(T talker, float distance)
    {
        if (!InReach(distance) || (_nearest != null && distance >= _nearestDistance)) return;
        _nearest = talker;
        _nearestDistance = distance;
    }

    public void Update(float deltaTime)
    {
        if (_nearest != null)
        {
            Talker = _nearest;
            _nearest = null;
            _hold = MinHold + (MaxHold - MinHold) * _roll();
            Silence = 0f;
            return;
        }

        if (Talker == null) return;
        Silence += deltaTime;
        if (Silence >= _hold) Talker = null;
    }
}
