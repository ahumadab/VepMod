using System.Collections.Generic;
using VepMod.Enemies.Whispral.Visuals;
using Xunit;

namespace Haunting.Tests;

public class StripOrderTests
{
    // The Map Tool's Controller: AudioLowPassLogic requires AudioLowPassFilter, which requires AudioSource.
    private static readonly Dictionary<string, string> Requirements = new()
    {
        ["AudioLowPassLogic"] = "AudioLowPassFilter",
        ["AudioLowPassFilter"] = "AudioSource"
    };

    private static bool Requires(string dependent, string required)
    {
        return Requirements.TryGetValue(dependent, out var r) && r == required;
    }

    [Fact]
    public void DependentsComeBeforeWhatTheyRequire()
    {
        var order = StripOrder.DependentsFirst(new[] { "AudioSource", "AudioLowPassFilter", "AudioLowPassLogic" },
            Requires);

        Assert.Equal(new[] { "AudioLowPassLogic", "AudioLowPassFilter", "AudioSource" }, order);
    }

    [Fact]
    public void UnrelatedItemsKeepTheirOrder()
    {
        var order = StripOrder.DependentsFirst(new[] { "PhotonView", "AudioSource", "PlayerAvatar", "AudioLowPassFilter" },
            Requires);

        Assert.Equal(new[] { "PhotonView", "PlayerAvatar", "AudioLowPassFilter", "AudioSource" }, order);
    }

    [Fact]
    public void ACycleEndsInItsOriginalOrder()
    {
        var order = StripOrder.DependentsFirst(new[] { "A", "B", "C" },
            (dependent, required) => (dependent, required) is ("A", "B") or ("B", "A"));

        Assert.Equal(new[] { "C", "A", "B" }, order);
    }
}
