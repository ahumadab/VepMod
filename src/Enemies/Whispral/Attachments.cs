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
    private readonly HashSet<int> whispralViewIDs = new();

    public bool IsHaunted => whispralViewIDs.Count > 0;

    public HauntedTransition Attach(int whispralViewID)
    {
        return whispralViewIDs.Add(whispralViewID) && whispralViewIDs.Count == 1
            ? HauntedTransition.BecameHaunted
            : HauntedTransition.NoChange;
    }

    public HauntedTransition Detach(int whispralViewID)
    {
        return whispralViewIDs.Remove(whispralViewID) && whispralViewIDs.Count == 0
            ? HauntedTransition.StoppedBeingHaunted
            : HauntedTransition.NoChange;
    }
}
