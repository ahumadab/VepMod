using System;
using BepInEx.Configuration;
using VepMod.VepFramework.Structures.Range;

namespace VepMod.VepFramework.Config;

/// <summary>
///     Extension methods to adapt RangeValue/MinMaxRange to BepInEx ConfigFile.
/// </summary>
public static class ConfigExtensions
{
    /// <summary>
    ///     Creates a BepInEx AcceptableValueRange from a RangeValue.
    /// </summary>
    private static AcceptableValueRange<T> ToAcceptableValueRange<T>(this RangeValue<T> range)
        where T : struct, IComparable, IComparable<T>
    {
        return new AcceptableValueRange<T>(range.Min, range.Max);
    }

    /// <summary>
    ///     Creates a BepInEx ConfigDescription from a RangeValue.
    /// </summary>
    private static ConfigDescription ToConfigDescription<T>(this RangeValue<T> range, string description)
        where T : struct, IComparable, IComparable<T>
    {
        return new ConfigDescription(description, range.ToAcceptableValueRange());
    }

    /// <summary>
    ///     Binds a config entry using a RangeValue for validation.
    /// </summary>
    public static ConfigEntry<T> BindRange<T>(
        this ConfigFile config,
        string section,
        string key,
        RangeValue<T> range,
        string description) where T : struct, IComparable, IComparable<T>
    {
        return config.Bind(section, key, range.Default, range.ToConfigDescription(description));
    }

    /// <summary>
    ///     Binds both min and max config entries for a MinMaxRange.
    ///     Returns a BoundMinMaxRange that exposes corrected .Min and .Max properties.
    /// </summary>
    public static BoundMinMaxRange<T> BindMinMax<T>(
        this ConfigFile config,
        string section,
        string minKey,
        string maxKey,
        MinMaxRange<T> range,
        string minDescription,
        string maxDescription) where T : struct, IComparable, IComparable<T>
    {
        var minEntry = config.Bind(section, minKey, range.MinRange.Default,
            range.MinRange.ToConfigDescription(minDescription));
        var maxEntry = config.Bind(section, maxKey, range.MaxRange.Default,
            range.MaxRange.ToConfigDescription(maxDescription));
        return new BoundMinMaxRange<T>(range, minEntry, maxEntry);
    }
}