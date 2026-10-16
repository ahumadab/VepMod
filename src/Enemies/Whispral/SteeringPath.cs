using System;

namespace VepMod.Enemies.Whispral;

/// <summary>
///     A NavMesh path on the horizontal plane, steered by pure pursuit: the steering point is where a circle of the
///     lookahead's radius around the Hallucination leaves the path, so the path's corners are rounded.
/// </summary>
internal sealed class SteeringPath
{
    private readonly float[] _x;
    private readonly float[] _z;
    private int _count;
    private int _next;
    private bool _isNew;

    public SteeringPath(int capacity)
    {
        _x = new float[capacity];
        _z = new float[capacity];
    }

    public bool IsEmpty => _count < 2;

    /// <summary>
    ///     The corner the last <see cref="Steer" /> began to round, if it did; the start is never one, nor a corner
    ///     already within reach of a new path, which a re-path to the same point would report again.
    /// </summary>
    public int? RoundedCorner { get; private set; }

    /// <summary>
    ///     The distance along the path from the last <see cref="Steer" /> position to the end, in metres.
    /// </summary>
    public float RemainingDistance { get; private set; }

    public void Clear()
    {
        _count = 0;
        _next = 1;
        RoundedCorner = null;
        RemainingDistance = 0f;
    }

    /// <summary>
    ///     Corners beyond the capacity are dropped: the path then ends early.
    /// </summary>
    public void Add(float x, float z)
    {
        if (_count == _x.Length) return;
        _x[_count] = x;
        _z[_count] = z;
        if (_count > 0) RemainingDistance += SegmentLength(_count);
        _count++;
        _next = 1;
        _isNew = true;
    }

    public (float X, float Z) Steer(float x, float z, float lookahead)
    {
        RoundedCorner = null;
        if (IsEmpty)
        {
            RemainingDistance = 0f;
            return (x, z);
        }

        while (_next < _count - 1 && Distance(x, z, _next) <= lookahead)
        {
            if (!_isNew) RoundedCorner = _next;
            _next++;
        }

        _isNew = false;

        RemainingDistance = Distance(x, z, _next);
        for (var i = _next + 1; i < _count; i++)
        {
            RemainingDistance += SegmentLength(i);
        }

        var t = SegmentFraction(x, z, lookahead);
        var start = _next - 1;
        return (_x[start] + (_x[_next] - _x[start]) * t, _z[start] + (_z[_next] - _z[start]) * t);
    }

    private float SegmentFraction(float x, float z, float lookahead)
    {
        if (Distance(x, z, _next) <= lookahead) return 1f;

        var start = _next - 1;
        var dx = _x[_next] - _x[start];
        var dz = _z[_next] - _z[start];
        var fx = _x[start] - x;
        var fz = _z[start] - z;

        var a = dx * dx + dz * dz;
        var b = fx * dx + fz * dz;
        var c = fx * fx + fz * fz - lookahead * lookahead;
        var discriminant = b * b - a * c;
        if (a <= 0f || discriminant < 0f) return 1f;

        return Math.Clamp((-b + MathF.Sqrt(discriminant)) / a, 0f, 1f);
    }

    private float Distance(float x, float z, int corner)
    {
        return MathF.Sqrt(Square(_x[corner] - x) + Square(_z[corner] - z));
    }

    private float SegmentLength(int end)
    {
        return Distance(_x[end - 1], _z[end - 1], end);
    }

    private static float Square(float value)
    {
        return value * value;
    }
}
