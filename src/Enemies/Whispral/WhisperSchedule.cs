using System;

namespace VepMod.Enemies.Whispral;

/// <summary>
///     When the Victim hears each Whisper, from which side and how loud, driven frame by frame with the time in seconds
///     and whether the Victim is Haunted.
/// </summary>
internal sealed class WhisperSchedule
{
    internal const float MinSilence = 8f; // s
    internal const float MaxSilence = 15f; // s
    internal const float FadeIn = 1f; // s
    internal const float FadeOut = 1.5f; // s
    internal const float Release = 1f; // s

    private static readonly int SideCount = Enum.GetValues(typeof(Side)).Length;

    private readonly Func<float> _roll;
    private bool _hasPlayed;
    private float _length;
    private Phase _phase;
    private float _releaseFrom;
    private float _releaseStart;
    private float _silenceEnd;
    private float _start;

    /// <param name="roll">A random value in [0, 1].</param>
    public WhisperSchedule(Func<float> roll)
    {
        _roll = roll;
    }

    public enum Side
    {
        Left,
        Right,
        Behind,
        LeftEar,
        RightEar
    }

    public enum Step
    {
        None,
        Start,
        End,
        Release
    }

    private enum Phase
    {
        Idle,
        Playing,
        Releasing,
        Silent
    }

    /// <summary>The side of the Whisper playing, or of the last one.</summary>
    public Side LastSide { get; private set; }

    /// <summary>The envelope of the Whisper playing, in [0, 1].</summary>
    public float Volume { get; private set; }

    public bool IsPlaying => _phase is Phase.Playing or Phase.Releasing;

    public bool IsReleasing => _phase == Phase.Releasing;

    /// <summary>How long the fade that ended the last Whisper lasted, in seconds.</summary>
    public float FadeLength { get; private set; }

    /// <summary>Seconds left before the next Whisper, while silent between two.</summary>
    public float SilenceLeft { get; private set; }

    /// <returns>
    ///     <see cref="Step.Start" />: the host plays a Whisper from <see cref="LastSide" /> and calls <see cref="Started" />.
    ///     <see cref="Step.End" />: the Whisper ended on its own. <see cref="Step.Release" />: the end of the Haunting
    ///     faded it out.
    /// </returns>
    public Step Update(float time, bool haunted)
    {
        switch (_phase)
        {
            case Phase.Idle:
                return haunted ? Begin(time) : Step.None;

            case Phase.Silent:
                if (!haunted)
                {
                    _phase = Phase.Idle;
                    return Step.None;
                }

                SilenceLeft = _silenceEnd - time;
                return SilenceLeft <= 0f ? Begin(time) : Step.None;

            case Phase.Playing:
                var elapsed = time - _start;
                if (elapsed >= _length)
                {
                    Volume = 0f;
                    FadeLength = Math.Min(FadeOut, _length);
                    SilenceLeft = MinSilence + (MaxSilence - MinSilence) * _roll();
                    _silenceEnd = time + SilenceLeft;
                    _phase = Phase.Silent;
                    return Step.End;
                }

                Volume = Envelope(elapsed);
                if (!haunted)
                {
                    _releaseFrom = Volume;
                    _releaseStart = time;
                    _phase = Phase.Releasing;
                }

                return Step.None;

            case Phase.Releasing:
                var released = time - _releaseStart;
                if (released >= Release || time - _start >= _length)
                {
                    Volume = 0f;
                    FadeLength = released;
                    _phase = Phase.Idle;
                    return Step.Release;
                }

                Volume = _releaseFrom * (1f - released / Release);
                return Step.None;

            default:
                throw new InvalidOperationException($"Unknown phase {_phase}");
        }
    }

    /// <param name="soundLength">How long the Whisper sound just started plays, in seconds.</param>
    public void Started(float soundLength)
    {
        _length = soundLength;
    }

    /// <returns>Where a Whisper from that side plays, in metres in the camera's space (x right, y up, z forward).</returns>
    public static (float X, float Y, float Z) Offset(Side side)
    {
        return side switch
        {
            Side.Left => (-0.5f, 0f, 0f),
            Side.Right => (0.5f, 0f, 0f),
            Side.Behind => (0f, 0f, -0.5f),
            Side.LeftEar => (-0.25f, 0.1f, -0.2f),
            Side.RightEar => (0.25f, 0.1f, -0.2f),
            _ => throw new ArgumentOutOfRangeException(nameof(side), side, null)
        };
    }

    private Step Begin(float time)
    {
        LastSide = NextSide();
        _hasPlayed = true;
        _start = time;
        _length = float.PositiveInfinity;
        Volume = 0f;
        _phase = Phase.Playing;
        return Step.Start;
    }

    private Side NextSide()
    {
        if (!_hasPlayed) return (Side)Pick(SideCount);

        var side = Pick(SideCount - 1);
        return (Side)(side >= (int)LastSide ? side + 1 : side);
    }

    private int Pick(int count)
    {
        return Math.Min((int)(_roll() * count), count - 1);
    }

    private float Envelope(float elapsed)
    {
        return Math.Clamp(Math.Min(elapsed / FadeIn, (_length - elapsed) / FadeOut), 0f, 1f);
    }
}
