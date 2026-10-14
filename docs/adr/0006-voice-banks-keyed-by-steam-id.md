# Voice Banks are keyed by Steam ID, migrated lazily from nicknames

A Voice Bank is keyed by the player's Steam ID, not their nickname: two teammates with the same nickname would share one bank (so a Mimic could speak with the wrong voice), and a renamed player would lose theirs. The nickname is kept only as a fallback when the game has no Steam ID for the player (`"0"` or empty, as in a local test without Steam), so Mimics stay testable there.

Banks written under the nickname by older versions are migrated when a player with that nickname is first seen: the first one seen takes the whole folder, even if it may mix the voices of namesakes, and it is merged into any bank already under their Steam ID, then capped like any bank (oldest Voice Sample dropped first, ADR-0003).

No player identity travels in the Voice Sample sharing or Mimic RPCs: the sender of a Voice Sample is the owner of the PhotonView it arrives on, and the Imitated Player of a Mimic is named by their avatar's ViewID. The Steam ID is only a local storage key.

## Considered Options

- **Delete the nickname banks once**: simpler, and the banks refill within a session, but every player would lose the head start ADR-0003 exists for.
- **Drop a nickname bank when two players with that nickname are present**: avoids handing someone mixed voices, at the cost of a rule nobody would guess; rejected as not worth it for a rare case.

## Consequences

Banks of players never seen again stay under their nickname on disk. The migration code stays until a release decides to remove it and delete what is left.
