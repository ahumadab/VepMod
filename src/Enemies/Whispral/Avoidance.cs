using System;

namespace VepMod.Enemies.Whispral;

/// <summary>
///     How a moving Hallucination steps aside for another one ahead of it, on the horizontal plane.
/// </summary>
internal static class Avoidance
{
    public const float Reach = 1.5f; // m
    private const float Strength = 1.5f; // sideways push at contact, per unit of heading

    /// <param name="dirX">The heading, normalised.</param>
    /// <param name="toOtherX">From this Hallucination to the other.</param>
    /// <returns>
    ///     A sideways push to add to the heading, away from the other, or to the right when it is dead ahead so that two
    ///     Hallucinations meeting head-on pass each other; zero when it is behind or out of reach.
    /// </returns>
    public static (float X, float Z) Deflection(float dirX, float dirZ, float toOtherX, float toOtherZ)
    {
        var distance = MathF.Sqrt(toOtherX * toOtherX + toOtherZ * toOtherZ);
        if (toOtherX * dirX + toOtherZ * dirZ <= 0f || distance >= Reach) return (0f, 0f);

        var otherOnTheRight = dirX * toOtherZ - dirZ * toOtherX < 0f;
        var push = Strength * (1f - distance / Reach) * (otherOnTheRight ? -1f : 1f);
        return (dirZ * push, -dirX * push);
    }
}
