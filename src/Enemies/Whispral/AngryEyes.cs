namespace VepMod.Enemies.Whispral;

/// <summary>
///     A Hallucination's angry eyes: angry until the Victim's Gaze has held a moment, or for as long as Stalking holds
///     the anger.
/// </summary>
internal sealed class AngryEyes
{
    private const float CalmDelay = 0.3f; // s

    private float _gazeTime;

    public bool IsAngry { get; private set; }

    public void Update(bool isUnderGaze, bool holdsAnger, float deltaTime)
    {
        _gazeTime = isUnderGaze ? _gazeTime + deltaTime : 0f;
        IsAngry = !isUnderGaze || holdsAnger || _gazeTime < CalmDelay;
    }
}
