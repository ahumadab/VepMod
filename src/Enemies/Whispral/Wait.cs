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
    internal const float MinGlanceInterval = 1f; // s
    internal const float MaxGlanceInterval = 2f; // s
    internal const float MaxGlanceAngle = 120f; // °

    // PlayerAvatarVisuals clamps the head's look to 40° around the body's forward.
    private const float HeadReach = 40f; // °

    private readonly Func<float> _roll;
    private float _bodyAngle;
    private float _elapsed;
    private float _nextGlance;
    private bool _turnRoundNext;
    private bool _glanceToBoundNext;

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

    /// <summary>
    ///     The next Glance goes to the bound, on the side away from the body.
    /// </summary>
    public void GlanceToBoundNext()
    {
        _glanceToBoundNext = true;
    }

    /// <returns>The Glance that starts on this update, if any; none once the Wait is over.</returns>
    public Glance? Update(float deltaTime)
    {
        _elapsed += deltaTime;
        if (IsOver || _elapsed < _nextGlance) return null;

        _nextGlance += Between(MinGlanceInterval, MaxGlanceInterval);
        var head = Between(-MaxGlanceAngle, MaxGlanceAngle);
        if (_glanceToBoundNext)
        {
            head = _bodyAngle > 0f ? -MaxGlanceAngle : MaxGlanceAngle;
            _glanceToBoundNext = false;
        }

        var glance = Toward(head, _bodyAngle);
        _bodyAngle = glance.Body;
        return glance;
    }

    /// <summary>
    ///     The Glance at <paramref name="head" /> of a body facing <paramref name="body" />, both from the same facing.
    /// </summary>
    public static Glance Toward(float head, float body)
    {
        var offBody = head - body;
        var bodyFollows = Math.Abs(offBody) > HeadReach;
        if (bodyFollows) body = head - Math.Sign(offBody) * HeadReach;
        return new Glance(head, offBody, body, bodyFollows);
    }

    private float Between(float min, float max)
    {
        return min + (max - min) * _roll();
    }

    public readonly struct Glance
    {
        public readonly float Head;

        // From where the body faced before the Glance.
        public readonly float OffBody;
        public readonly float Body;
        public readonly bool BodyFollows;

        public Glance(float head, float offBody, float body, bool bodyFollows)
        {
            Head = head;
            OffBody = offBody;
            Body = body;
            BodyFollows = bodyFollows;
        }
    }
}
