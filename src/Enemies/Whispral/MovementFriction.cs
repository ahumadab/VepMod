using System;

namespace VepMod.Enemies.Whispral;

/// <summary>
///     A player's movement friction on a Hallucination's speed: PlayerController lerps a stopping player's velocity
///     twice, at MoveFriction per second each time.
/// </summary>
// A player starting from rest skips the first lerp (PlayerController.VelocityIdle), which starts it in about 0.27 s:
// both lerps run here on a start too, so a start eases over 0.3 to 0.5 s like a stop (#65).
internal sealed class MovementFriction
{
    private const float Friction = 10f; // /s, PlayerController.MoveFriction in Main.unity
    private const float MovingSpeed = 0.1f; // m/s, PlayerController's "moving" threshold on the first lerp
    private const float RestSpeed = 0.02f; // m/s
    private const float SettledFraction = 0.05f; // of the ease's change, below which it counts as done

    // PlayerController.VelocityRelativeNew's speed.
    private float _firstLerpSpeed;
    private float _target;
    private float _easeFrom;
    private float _easeTime;
    private bool _isEasing;

    public float Speed { get; private set; }

    public bool IsMoving => _firstLerpSpeed > MovingSpeed;

    /// <summary>
    ///     The distance, in metres, covered by easing from now to a stop.
    /// </summary>
    public float StoppingDistance => (Speed + _firstLerpSpeed) / Friction;

    /// <param name="target">The speed to ease towards, in m/s.</param>
    /// <returns>The ease that settles on this update, if any: one superseded by a new target is never returned.</returns>
    public Ease? Update(float target, float deltaTime)
    {
        if (target != _target)
        {
            _target = target;
            _easeFrom = Speed;
            _easeTime = 0f;
            _isEasing = true;
        }

        var step = Math.Min(1f, Friction * deltaTime);
        _firstLerpSpeed += (target - _firstLerpSpeed) * step;
        Speed += (_firstLerpSpeed - Speed) * step;

        // The game zeroes a stopping player's velocity once it falls below "moving"; zeroing it lower avoids the jolt
        // without crawling along the path for seconds.
        if (target == 0f && Speed < RestSpeed) Speed = _firstLerpSpeed = 0f;

        if (!_isEasing) return null;

        _easeTime += deltaTime;
        if (Math.Abs(Speed - target) > SettledFraction * Math.Abs(target - _easeFrom)) return null;

        _isEasing = false;
        return new Ease(_easeFrom, target, _easeTime);
    }

    public readonly struct Ease
    {
        public readonly float From;
        public readonly float To;
        public readonly float Duration;

        public Ease(float from, float to, float duration)
        {
            From = from;
            To = to;
            Duration = duration;
        }
    }
}
