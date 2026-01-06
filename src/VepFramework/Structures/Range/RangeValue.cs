using System;

namespace VepMod.VepFramework.Structures.Range;

/// <summary>
///     Utility class for defining a value with min/max bounds and a default value.
///     Automatically clamps values that fall outside the valid range.
/// </summary>
public sealed class RangeValue<T> where T : struct, IComparable<T>
{
    public RangeValue(T min, T max, T defaultValue)
    {
        if (min.CompareTo(max) > 0)
        {
            throw new ArgumentException($"Min ({min}) cannot be greater than Max ({max})");
        }

        Min = min;
        Max = max;
        Default = Clamp(defaultValue);
    }

    public T Min { get; }
    public T Max { get; }
    public T Default { get; }

    /// <summary>
    ///     Clamps the value to be within [Min, Max].
    /// </summary>
    public T Clamp(T value)
    {
        if (value.CompareTo(Min) < 0) return Min;
        if (value.CompareTo(Max) > 0) return Max;
        return value;
    }
}

/// <summary>
///     Factory methods for RangeValue.
/// </summary>
public static class RangeValue
{
    public static RangeValue<float> Float(float min, float max, float defaultValue)
    {
        return new RangeValue<float>(min, max, defaultValue);
    }

    public static RangeValue<int> Int(int min, int max, int defaultValue)
    {
        return new RangeValue<int>(min, max, defaultValue);
    }

    public static RangeValue<float> Percentage(float defaultValue = 1f)
    {
        return new RangeValue<float>(0f, 1f, defaultValue);
    }
}
