using System;

namespace VepMod.Enemies.Whispral;

/// <summary>
///     A player's movement friction on a Hallucination's speed: PlayerController lerps its velocity twice towards the
///     input's, at MoveFriction per second each time.
/// </summary>
internal sealed class MovementFriction
{
    private const float Friction = 10f; // /s, PlayerController.MoveFriction in Main.unity
    private const float MovingSpeed = 0.1f; // m/s, PlayerController's "moving" threshold on the first lerp
    private const float SettledFraction = 0.05f; // of the ease's change, below which it counts as done

    private float _lead;
    private float _target;
    private float _easeFrom;
    private float _easeTime;
    private bool _isEasing;

    public float Speed { get; private set; }

    public bool IsMoving => _lead > MovingSpeed;

    /// <summary>
    ///     The distance, in metres, covered by easing from now to a stop.
    /// </summary>
    public float StoppingDistance => (Speed + _lead) / Friction;

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
        _lead += (target - _lead) * step;
        Speed += (_lead - Speed) * step;

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
