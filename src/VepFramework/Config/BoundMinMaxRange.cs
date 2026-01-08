using System;
using BepInEx.Configuration;
using VepMod.VepFramework.Structures.Range;

namespace VepMod.VepFramework.Config;

/// <summary>
///     A MinMaxRange bound to ConfigEntries. Exposes Min/Max properties that are always corrected.
/// </summary>
public sealed class BoundMinMaxRange<T> where T : struct, IComparable<T>
{
    private readonly MinMaxRange<T> _range;
    private readonly ConfigEntry<T> _minEntry;
    private readonly ConfigEntry<T> _maxEntry;

    public BoundMinMaxRange(MinMaxRange<T> range, ConfigEntry<T> minEntry, ConfigEntry<T> maxEntry)
    {
        _range = range;
        _minEntry = minEntry;
        _maxEntry = maxEntry;
    }

    /// <summary>
    ///     Gets the corrected min value (always &lt;= Max).
    /// </summary>
    public T Min => _range.GetMin(_minEntry.Value, _maxEntry.Value);

    /// <summary>
    ///     Gets the corrected max value (always &gt;= Min).
    /// </summary>
    public T Max => _range.GetMax(_minEntry.Value, _maxEntry.Value);
}
