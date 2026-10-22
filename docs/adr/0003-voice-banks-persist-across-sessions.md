# Voice Banks persist across sessions

Voice Samples are written as WAV files under the game's data folder and are never wiped at the end of a session; each Voice Bank is only capped in size, dropping its oldest Voice Sample first. This is deliberate: Hallucinations can Mimic a teammate from the very start of a new session instead of waiting for fresh Voice Samples to be recorded and shared.

Each Voice Bank is keyed by the player's Steam ID (ADR-0006).

## Consequences

Teammates' voices stay on each player's disk indefinitely. Player-facing documentation must say so rather than describe the storage as temporary.
