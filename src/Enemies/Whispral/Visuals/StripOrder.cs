using System;
using System.Collections.Generic;

namespace VepMod.Enemies.Whispral.Visuals;

// Unity refuses to remove a component that another one on its GameObject requires ([RequireComponent]), and logs an
// error instead: a strip destroys dependents before what they require.
internal static class StripOrder
{
    /// <summary>
    ///     <paramref name="items" /> reordered so that each comes before every item it requires, otherwise in their
    ///     original order. Items in a requirement cycle, or required by one, come last, in their original order.
    /// </summary>
    public static List<T> DependentsFirst<T>(IReadOnlyList<T> items, Func<T, T, bool> requires)
    {
        var remaining = new List<T>(items);
        var ordered = new List<T>(items.Count);

        while (remaining.Count > 0)
        {
            var next = remaining.FindIndex(candidate => !IsRequiredByAnother(candidate, remaining, requires));
            if (next < 0) break;
            ordered.Add(remaining[next]);
            remaining.RemoveAt(next);
        }

        ordered.AddRange(remaining);
        return ordered;
    }

    private static bool IsRequiredByAnother<T>(T candidate, List<T> remaining, Func<T, T, bool> requires)
    {
        foreach (var other in remaining)
        {
            if (!ReferenceEquals(other, candidate) && requires(other, candidate)) return true;
        }

        return false;
    }
}
