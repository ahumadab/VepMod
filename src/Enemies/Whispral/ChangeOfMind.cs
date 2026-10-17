using System;

namespace VepMod.Enemies.Whispral;

/// <summary>
///     Whether a Hallucination on a Trip changes its mind: a 3 % chance each second, at most once per Trip.
/// </summary>
internal sealed class ChangeOfMind
{
    private const float Chance = 0.03f;
    private const float RollInterval = 1f; // s

    private readonly Func<float> _roll;
    private int _changesAsked;
    private bool _changed;
    private bool _refusalReported;
    private float _sinceRoll;

    /// <param name="roll">A random value in [0, 1].</param>
    public ChangeOfMind(Func<float> roll)
    {
        _roll = roll;
    }

    public void Start()
    {
        _changed = false;
        _refusalReported = false;
        _sinceRoll = 0f;
    }

    /// <summary>
    ///     The next update of a Trip that has not changed its mind yet changes it; each call asks for one change.
    /// </summary>
    public void ChangeNow()
    {
        _changesAsked++;
    }

    /// <returns>
    ///     The change that starts on this update, or a change asked for that this Trip refuses, reported once per Trip.
    /// </returns>
    public Change? Update(float deltaTime)
    {
        if (_changed)
        {
            if (_changesAsked == 0 || _refusalReported) return null;
            _refusalReported = true;
            return new Change(null, true, true);
        }

        if (_changesAsked > 0)
        {
            _changesAsked--;
            _changed = true;
            return new Change(null, true, false);
        }

        _sinceRoll += deltaTime;
        if (_sinceRoll < RollInterval) return null;

        _sinceRoll -= RollInterval;
        var roll = _roll();
        if (roll >= Chance) return null;

        _changed = true;
        return new Change(roll, false, false);
    }

    public readonly struct Change
    {
        // Null when the change was asked for.
        public readonly float? Roll;
        public readonly bool Asked;
        public readonly bool Capped;

        public Change(float? roll, bool asked, bool capped)
        {
            Roll = roll;
            Asked = asked;
            Capped = capped;
        }
    }
}
