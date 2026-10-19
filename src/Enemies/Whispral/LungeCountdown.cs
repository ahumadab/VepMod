namespace VepMod.Enemies.Whispral;

/// <summary>
///     The count of a Stalking stare towards its Lunge: 5 s while nothing stands between the Hallucination and the
///     Victim, or has for less than 0.3 s, then the Lunge once the Victim is within reach, then the kill a second later.
/// </summary>
internal sealed class LungeCountdown
{
    private const float Count = 5f; // s
    private const float Reach = 5f; // m
    private const float KillDelay = 1f; // s

    // A line of sight at an obstacle's edge flips every few frames: the count pauses once it has been lost this long.
    private const float HiddenGrace = 0.3f; // s

    private float _hiddenFor;
    private bool _overOutOfReachSaid;
    private float _sinceLunge;

    public Phase Current { get; private set; }

    /// <summary>
    ///     How long the count has run, in s.
    /// </summary>
    public float Elapsed { get; private set; }

    public bool IsOver => Elapsed >= Count;

    public bool IsCounting => Current is Phase.AwaitingSight or Phase.Running or Phase.Paused;

    public void Reset()
    {
        Current = Phase.NotStarted;
        Elapsed = 0f;
    }

    /// <summary>
    ///     The count starts on the next update in sight.
    /// </summary>
    public void Start()
    {
        Current = Phase.AwaitingSight;
        Elapsed = 0f;
        _overOutOfReachSaid = false;
    }

    /// <param name="distance">To the Victim, in m.</param>
    /// <param name="isHidden">Whether something stands between the Hallucination and the Victim.</param>
    public Event? Update(float deltaTime, float distance, bool isHidden, bool isGazeOn)
    {
        switch (Current)
        {
            case Phase.NotStarted or Phase.Gazed or Phase.Killed:
                return null;
            case Phase.Lunging:
                _sinceLunge += deltaTime;
                if (_sinceLunge < KillDelay) return null;
                Current = Phase.Killed;
                return Event.Kill;
        }

        if (isGazeOn)
        {
            Current = Phase.Gazed;
            return Event.Gazed;
        }

        if (isHidden)
        {
            if (Current != Phase.Running) return null;
            Elapsed += deltaTime;
            _hiddenFor += deltaTime;
            if (_hiddenFor < HiddenGrace) return null;
            Current = Phase.Paused;
            return Event.Paused;
        }

        _hiddenFor = 0f;
        switch (Current)
        {
            case Phase.AwaitingSight:
                Current = Phase.Running;
                return Event.Started;
            case Phase.Paused:
                Current = Phase.Running;
                return Event.Resumed;
        }

        Elapsed += deltaTime;
        if (!IsOver) return null;

        if (distance <= Reach)
        {
            Current = Phase.Lunging;
            _sinceLunge = 0f;
            return Event.Lunge;
        }

        if (_overOutOfReachSaid) return null;
        _overOutOfReachSaid = true;
        return Event.OverOutOfReach;
    }

    public override string ToString()
    {
        return Current switch
        {
            Phase.NotStarted => "not started",
            Phase.AwaitingSight => "not started, waiting for sight",
            Phase.Running when IsOver => "over, the Victim out of reach",
            Phase.Running => $"running {Elapsed:F1}s",
            Phase.Paused => $"paused {Elapsed:F1}s" + (IsOver ? ", over" : ""),
            Phase.Gazed => $"ended by the Gaze at {Elapsed:F1}s",
            Phase.Lunging => "Lunge",
            Phase.Killed => "kill",
            _ => Current.ToString()
        };
    }

    public enum Phase
    {
        NotStarted,
        AwaitingSight,
        Running,
        Paused,
        Gazed,
        Lunging,
        Killed
    }

    public enum Event
    {
        Started,
        Paused,
        Resumed,
        OverOutOfReach,
        Gazed,
        Lunge,
        Kill
    }
}
