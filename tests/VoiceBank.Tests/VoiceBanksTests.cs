using System;
using System.Collections.Generic;
using System.IO;
using VepMod.Enemies.Whispral;
using Xunit;

namespace VoiceBank.Tests;

public sealed class VoiceBanksTests : IDisposable
{
    private static readonly PlayerKey Bob = new("76561198000000001", "Bob");
    private static readonly PlayerKey OtherBob = new("76561198000000002", "Bob");
    private static readonly PlayerKey Alice = new("76561198000000003", "Alice");

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"VoiceBanksTests-{Guid.NewGuid():N}");
    private int _samplesPerPlayer = 3;
    private readonly VoiceBanks _banks;

    public VoiceBanksTests()
    {
        _banks = new VoiceBanks(_root, () => _samplesPerPlayer);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Fact]
    public void AnUnknownPlayerHasNoVoiceSample()
    {
        Assert.Null(_banks.PickRandom(Bob));
    }

    [Fact]
    public void PicksTheSavedVoiceSampleDecoded()
    {
        _banks.Save(Bob, Sample(0.5f, 16000));

        var picked = _banks.PickRandom(Bob)!;

        Assert.Equal([0.5f], picked.Samples);
        Assert.Equal(16000, picked.SampleRate);
    }

    [Fact]
    public void RotationDropsTheOldestAtTheCap()
    {
        _samplesPerPlayer = 2;

        _banks.Save(Bob, Sample(0.125f));
        _banks.Save(Bob, Sample(0.25f));
        _banks.Save(Bob, Sample(0.5f));

        Assert.Equal([0.25f, 0.5f], PickedValues(Bob));
    }

    [Fact]
    public void RandomPickReturnsOnlyThatPlayersVoiceSamples()
    {
        _banks.Save(Bob, Sample(0.125f));
        _banks.Save(Bob, Sample(0.25f));
        _banks.Save(Alice, Sample(0.5f));

        Assert.Equal([0.125f, 0.25f], PickedValues(Bob));
        Assert.Equal([0.5f], PickedValues(Alice));
    }

    [Fact]
    public void NamesakesWithSteamIdsHaveSeparateBanks()
    {
        _banks.Save(Bob, Sample(0.125f));

        Assert.Null(_banks.PickRandom(OtherBob));
    }

    [Fact]
    public void ASteamIdOfZeroFallsBackToTheNickname()
    {
        _banks.Save(new PlayerKey("0", "Bob"), Sample(0.125f));

        Assert.Equal([0.125f], PickedValues(new PlayerKey("", "Bob")));
        Assert.Null(_banks.PickRandom(Bob));
    }

    [Fact]
    public void ANicknameBankNeverCollidesWithASteamIdBank()
    {
        _banks.Save(Bob, Sample(0.125f));

        Assert.Null(_banks.PickRandom(new PlayerKey("0", $"steam_{Bob.SteamId}")));
        Assert.Null(_banks.PickRandom(new PlayerKey("0", Bob.SteamId)));
    }

    [Fact]
    public void TheFirstPlayerSeenWithALegacyNicknameTakesTheWholeBank()
    {
        WriteLegacySample("Bob", 2, 0.125f);
        WriteLegacySample("Bob", 1, 0.25f);

        Assert.Equal(2, _banks.MigrateLegacyBank(Bob));
        Assert.Null(_banks.MigrateLegacyBank(OtherBob));

        Assert.Equal([0.125f, 0.25f], PickedValues(Bob));
        Assert.Null(_banks.PickRandom(OtherBob));
    }

    [Fact]
    public void LegacyVoiceSamplesSavedAtTheSameTimeAreAllMigrated()
    {
        WriteLegacySample("Bob", 1, 0.125f);
        WriteLegacySample("Bob", 1, 0.25f);

        Assert.Equal(2, _banks.MigrateLegacyBank(Bob));
    }

    [Fact]
    public void ALegacyBankIsMergedIntoTheSteamIdBankThenCapped()
    {
        _banks.Save(Bob, Sample(0.5f));
        _banks.Save(Bob, Sample(0.75f));
        WriteLegacySample("Bob", 2, 0.125f);
        WriteLegacySample("Bob", 1, 0.25f);

        Assert.Equal(3, _banks.MigrateLegacyBank(Bob));

        Assert.Equal([0.25f, 0.5f, 0.75f], PickedValues(Bob));
    }

    [Fact]
    public void NothingToMigrateWithoutASteamIdOrALegacyBank()
    {
        WriteLegacySample("Bob", 1, 0.125f);

        Assert.Null(_banks.MigrateLegacyBank(Alice));
        Assert.Null(_banks.MigrateLegacyBank(new PlayerKey("0", "Bob")));
        Assert.Equal([0.125f], PickedValues(new PlayerKey("0", "Bob")));
    }

    private static VoiceSample Sample(float value, int sampleRate = 48000)
    {
        return new VoiceSample([value], sampleRate);
    }

    // Older versions wrote AudioFiles/player_{nickname}/sample_{DateTime.Now.Ticks}.wav.
    private void WriteLegacySample(string nickname, int daysAgo, float value)
    {
        var folder = Path.Combine(_root, $"player_{nickname}");
        Directory.CreateDirectory(folder);
        var savedAt = DateTime.Now.AddDays(-daysAgo);
        var path = Path.Combine(folder, $"sample_{savedAt.Ticks}{value}.wav");
        File.WriteAllBytes(path, WavCodec.Encode(Sample(value)));
        File.SetLastWriteTimeUtc(path, savedAt.ToUniversalTime());
    }

    private SortedSet<float> PickedValues(PlayerKey player)
    {
        var values = new SortedSet<float>();
        for (var i = 0; i < 200; i++) values.Add(_banks.PickRandom(player)!.Samples[0]);
        return values;
    }
}
