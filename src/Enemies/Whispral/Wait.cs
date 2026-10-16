using System;

namespace VepMod.Enemies.Whispral;

/// <summary>
///     A Hallucination's Wait: how long it lasts, whether it starts with a turn round, and its Glances. Angles are yaws
///     in degrees from the Wait's facing: where the Hallucination faced as it started, turned round if it did.
/// </summary>
internal sealed class Wait
{
    private const float MinDuration = 2f; // s
    private const float MaxDuration = 4f; // s
    private const float TurnRoundChance = 0.25f;
    private const float MinGlanceInterval = 1f; // s
    private const float MaxGlanceInterval = 2f; // s
    private const float MaxGlanceAngle = 120f; // °

    // PlayerAvatarVisuals clamps the head's look to 40° around the body's forward.
    private const float HeadReach = 40f; // °

    private readonly Func<float> _roll;
    private float _bodyAngle;
    private float _elapsed;
    private float _nextGlance;
    private bool _turnRoundNext;

    /// <param name="roll">A random value in [0, 1].</param>
    public Wait(Func<float> roll)
    {
        _roll = roll;
    }

    public float Duration { get; private set; }

    public bool IsOver => _elapsed >= Duration;

    /// <returns>Whether the Wait starts with a turn round.</returns>
    public bool Start()
    {
        Duration = Between(MinDuration, MaxDuration);
        _elapsed = 0f;
        _bodyAngle = 0f;
        _nextGlance = Between(MinGlanceInterval, MaxGlanceInterval);

        var turnRound = _turnRoundNext || _roll() < TurnRoundChance;
        _turnRoundNext = false;
        return turnRound;
    }

    public void TurnRoundNextWait()
    {
        _turnRoundNext = true;
    }

    /// <returns>The Glance that starts on this update, if any; none once the Wait is over.</returns>
    public Glance? Update(float deltaTime)
    {
        _elapsed += deltaTime;
        if (IsOver || _elapsed < _nextGlance) return null;

        _nextGlance += Between(MinGlanceInterval, MaxGlanceInterval);
        var head = Between(-MaxGlanceAngle, MaxGlanceAngle);
        var offBody = head - _bodyAngle;
        var bodyFollows = Math.Abs(offBody) > HeadReach;
        if (bodyFollows) _bodyAngle = head - Math.Sign(offBody) * HeadReach;
        return new Glance(head, _bodyAngle, bodyFollows);
    }

    private float Between(float min, float max)
    {
        return min + (max - min) * _roll();
    }

    public readonly struct Glance
    {
        public readonly float Head;
        public readonly float Body;
        public readonly bool BodyFollows;

        public Glance(float head, float body, bool bodyFollows)
        {
            Head = head;
            Body = body;
            BodyFollows = bodyFollows;
        }
    }
}
