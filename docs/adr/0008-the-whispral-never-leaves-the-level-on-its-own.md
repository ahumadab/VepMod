# The Whispral never leaves the level on its own

REPO keeps its enemies on a cycle: an enemy despawns after a few minutes spent away from players, stays away for minutes, and respawns 18 to 35 m from a player, which is how the game brings it back to the team. The Whispral leaves that cycle: the game's spawn timer never runs out for it and a flee no longer despawns it, so the Whispral keeps hunting all through the level. It despawns only when it is killed, for a fixed 2 to 3 min, and when the game's end-of-level rule moves it. Without respawns to bring it closer, each of its roams heads for a point at least 18 m from every alive player and within 35 m of one of them.

## Considered Options

- A shorter absence on the game's cycle: rejected, the Whispral would still be missing when the players expect it.
- An unseen relocation near the team whenever it has seen no one for a while: rejected for a roam that really crosses the level.

## Consequences

A team is haunted far more often than with any other enemy, and the Whispral skips the director's start pause. Code that reads like a bug (the spawn timer refilled before it runs out, `LeaveState` without `SpawnedTimerSet`) is this decision.
