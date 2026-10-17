using System;
using System.Collections.Generic;
using VepMod.Enemies.Whispral;
using Xunit;

namespace Haunting.Tests;

public class TripChainTests
{
    private static Func<float> Seeded(int seed)
    {
        var random = new Random(seed);
        return () => random.NextSingle();
    }

    [Fact]
    public void OneArrivalInThreeChains()
    {
        var chain = new TripChain(Seeded(1));
        var chains = 0;
        const int trips = 10000;
        for (var i = 0; i < trips; i++)
        {
            chain.Start();
            if (chain.Arrive().Chains) chains++;
        }

        Assert.InRange(chains / (float)trips, 0.31f, 0.36f);
    }

    [Theory]
    [InlineData(0.3f, true)]
    [InlineData(0.34f, false)]
    public void TheRollDecides(float roll, bool chains)
    {
        var chain = new TripChain(() => roll);
        chain.Start();

        var arrival = chain.Arrive();

        Assert.Equal(roll, arrival.Roll);
        Assert.Equal(chains, arrival.Chains);
    }

    [Fact]
    public void TwoTripsAreFollowedByAWait()
    {
        var chain = new TripChain(() => 0f);
        chain.Start();

        Assert.True(chain.Arrive().Chains);
        var second = chain.Arrive();

        Assert.False(second.Chains);
        Assert.True(second.Capped);
    }

    [Fact]
    public void ANewTripStartsTheCountAgain()
    {
        var chain = new TripChain(() => 0f);
        chain.Start();
        chain.Arrive();
        chain.Arrive();
        chain.Start();

        Assert.True(chain.Arrive().Chains);
    }

    [Fact]
    public void AChainAskedForChainsTheNextArrivalOnly()
    {
        var chain = new TripChain(() => 0.9f);
        chain.ChainNextArrival();
        chain.Start();

        var asked = chain.Arrive();
        chain.Start();

        Assert.True(asked.Chains);
        Assert.True(asked.Asked);
        Assert.False(chain.Arrive().Chains);
    }

    [Fact]
    public void AChainAskedForAtTheCapWaitsForTheNextTrip()
    {
        var chain = new TripChain(() => 0.9f);
        chain.ChainNextArrival();
        chain.ChainNextArrival();
        chain.Start();

        Assert.True(chain.Arrive().Chains);
        var second = chain.Arrive();
        chain.Start();
        var next = chain.Arrive();

        Assert.True(second.Capped);
        Assert.False(second.Chains);
        Assert.True(next.Asked);
        Assert.True(next.Chains);
    }

    [Fact]
    public void NoArrivalEverMakesAThirdTrip()
    {
        var chain = new TripChain(Seeded(2));
        var tripsInARow = new List<int>();
        for (var i = 0; i < 1000; i++)
        {
            chain.Start();
            var trips = 1;
            while (chain.Arrive().Chains) trips++;
            tripsInARow.Add(trips);
        }

        Assert.Equal(new HashSet<int> { 1, 2 }, new HashSet<int>(tripsInARow));
    }
}
