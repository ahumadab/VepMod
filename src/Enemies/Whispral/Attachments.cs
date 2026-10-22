using System.Collections.Generic;

namespace VepMod.Enemies.Whispral;

public enum HauntedTransition
{
    NoChange,
    BecameHaunted,
    StoppedBeingHaunted
}

/// <summary>
///     The Whisprals attached to one Victim, each keyed by an ID unique to it. Attaching and detaching are
///     idempotent per Whispral.
/// </summary>
public sealed class Attachments
{
    private readonly HashSet<int> _whispralViewIDs = new();

    public bool IsHaunted => _whispralViewIDs.Count > 0;

    public HauntedTransition Attach(int whispralViewID)
    {
        return _whispralViewIDs.Add(whispralViewID) && _whispralViewIDs.Count == 1
            ? HauntedTransition.BecameHaunted
            : HauntedTransition.NoChange;
    }

    public HauntedTransition Detach(int whispralViewID)
    {
        return _whispralViewIDs.Remove(whispralViewID) && _whispralViewIDs.Count == 0
            ? HauntedTransition.StoppedBeingHaunted
            : HauntedTransition.NoChange;
    }
}
