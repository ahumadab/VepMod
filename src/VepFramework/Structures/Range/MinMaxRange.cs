using System;

namespace VepMod.VepFramework.Structures.Range;

/// <summary>
///     Represents a linked min/max pair where both values must maintain min &lt;= max.
///     Automatically corrects values if configured incorrectly.
/// </summary>
public sealed class MinMaxRange<T> where T : struct, IComparable<T>
{
    public MinMaxRange(RangeValue<T> minRange, RangeValue<T> maxRange)
    {
        MinRange = minRange;
        MaxRange = maxRange;
    }

    public RangeValue<T> MinRange { get; }
    public RangeValue<T> MaxRange { get; }

    /// <summary>
    ///     Gets the corrected min value, ensuring it's not greater than max.
    /// </summary>
    public T GetMin(T minValue, T maxValue)
    {
        if (minValue.CompareTo(maxValue) > 0)
        {
            return maxValue;
        }

        return minValue;
    }

    /// <summary>
    ///     Gets the corrected max value, ensuring it's not less than min.
    /// </summary>
    public T GetMax(T minValue, T maxValue)
    {
        if (maxValue.CompareTo(minValue) < 0)
        {
            return minValue;
        }

        return maxValue;
    }
}

/// <summary>
///     Factory methods for MinMaxRange.
/// </summary>
public static class MinMaxRange
{
    public static MinMaxRange<float> Float(
        float minLower, float minUpper, float minDefault,
        float maxLower, float maxUpper, float maxDefault)
    {
        return new MinMaxRange<float>(
            RangeValue.Float(minLower, minUpper, minDefault),
            RangeValue.Float(maxLower, maxUpper, maxDefault));
    }
}
