using System;

namespace VepMod.Enemies.Whispral;

/// <summary>
///     Whether a Hallucination reaching its point goes straight on to another Trip instead of a Wait: one time in
///     three, and never more than two Trips without a Wait.
/// </summary>
internal sealed class TripChain
{
    private const float ChainChance = 1f / 3f;
    private const int MaxTrips = 2;

    private readonly Func<float> _roll;
    private int _chainsAsked;
    private int _trips;

    /// <param name="roll">A random value in [0, 1].</param>
    public TripChain(Func<float> roll)
    {
        _roll = roll;
    }

    /// <summary>
    ///     A Trip starts after a Wait, or anything else that is not a Trip.
    /// </summary>
    public void Start()
    {
        _trips = 1;
    }

    /// <summary>
    ///     The next arrival chains, within the cap; each call asks for one arrival.
    /// </summary>
    public void ChainNextArrival()
    {
        _chainsAsked++;
    }

    public Arrival Arrive()
    {
        var asked = _chainsAsked > 0;
        if (asked) _chainsAsked--;

        var roll = _roll();
        var capped = _trips >= MaxTrips;
        var chains = !capped && (asked || roll < ChainChance);
        if (chains) _trips++;
        return new Arrival(roll, asked, capped, chains);
    }

    public readonly struct Arrival
    {
        public readonly float Roll;
        public readonly bool Asked;
        public readonly bool Capped;
        public readonly bool Chains;

        public Arrival(float roll, bool asked, bool capped, bool chains)
        {
            Roll = roll;
            Asked = asked;
            Capped = capped;
            Chains = chains;
        }
    }
}
