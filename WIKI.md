# VepMod – Wiki (1.0.3)

> **WARNING – SPOILER CONTENT**  
> This page is intended for players who want to understand in detail how the mod works.  
> **Reading this wiki may spoil the discovery experience** of the Whispral and its effects.  
> If you prefer to discover the mod by yourself, close this page now.

---

## Table of Contents

1. [Overview](#overview)
2. [The Whispral](#the-whispral)
3. [Fake Players (Hallucinations)](#fake-Players-hallucinations)
4. [Audio System](#audio-system)
5. [Configuration Settings](#configuration-settings)
6. [Effects on the Player](#effects-on-the-player)
7. [FAQ](#faq)

---
## Overview

VepMod adds a new enemy called **Whispral** to the game REPO.  
This unique enemy causes hallucinations in the player it attacks, making them see fake copies of their teammates.

The mod records players’ voices during the game and uses them to make hallucinations more convincing.

---

## The Whispral

### Description

The Whispral is an invisible creature that moves through the level searching for players.  
When it detects a player, it approaches and attaches itself to them for a set duration.

### Behavior

1. **Spawn**: The Whispral appears in the level
2. **Idle / Roam**: It wanders around the level, exploring areas
3. **Investigate**: It investigates sounds it hears
4. **Notice Player**: It spots and locks onto a player
5. **Go To Player**: It moves toward the detected player
6. **Attached**: It attaches to the player for ~240 seconds
7. **Detach**: It detaches and moves away
8. **Leave**: It leaves the area

### Interactions

- The Whispral can be **stunned** by throwing objects at it
- When stunned, it releases the player and all debuffs are cleared
- If the player is stunned, the Whispral automatically detaches

---

## Fake Player-Hallucinations

### Description

When a Whispral is attached to a player, they see **Fake Player** — hallucinatory copies of their teammates. These Fake Player:

- Have the same **color** as the player they imitate
- Display the same **name** (nameplate)
- Reproduce realistic **footstep sounds**
- Can **speak** using the imitated player’s voice

### Fake Player States

| State | Description |
|------|-------------|
| **Idle** | Stays still, waiting |
| **Wander** | Walks randomly (chances to switch to Sprint) |
| **Sprint** | Runs randomly (chances to switch to Wander) |
| **CheckMap** | Map-checking animation  |
| **StalkApproach** | Approaches the player (10% chance if distance > 15m) |
| **StalkStare** | Stares at the player for 2 seconds |
| **StalkFlee** | Flees if the player stares too long |

### “Angry Eyes” System

Fake Players have an animated eyelid system:

- When the player **don't looks at** a Fake Players, its eyes become “angry”
- If the player looks the Fake Player, it stops being angry
- When Stalk States are active, the Fake Player maintain the angry emote for 2 seconds before running away after the player see them.

### Gaze Detection

- The mod detects whether the player is looking at a Fake Players using the **camera dot product**
- Detection threshold: viewing angle < 30 degrees
- Limited detection distance (30 units)

---

## Audio System

### Voice Recording

The mod automatically records player voices during the game:

- Audio files are stored locally in WAV format
- Each player can have up to **20 samples** stored (configurable)
- Samples are shared between clients at regular intervals

### Audio Sharing Loop

| Parameter | Min | Max | Default |
|---------|-----|-----|---------|
| Share Min Delay | 10s | 60s | 10s |
| Share Max Delay | 10s | 120s | 30s |
| Samples Per Player | 5 | 20 | 20 |

### Voice Playback

While the Whispral is attached, Fake Playerss play voices at regular intervals:

| Parameter | Min | Max | Default |
|---------|-----|-----|---------|
| Voice Min Delay | 6s | 30s | 8s |
| Voice Max Delay | 6s | 30s | 15s |

---

## Configuration Settings

All settings can be modified via the BepInEx configuration file.

### General

| Parameter | Default | Description |
|---------|---------|-------------|
| Volume | 100% | Volume of hallucination voices |

### Audio Sharing

| Parameter | Default | Description |
|---------|---------|-------------|
| Share Min Delay | 10s | Minimum delay between audio sharing |
| Share Max Delay | 30s | Maximum delay between audio sharing |
| Samples Per Player | 20 | Maximum number of samples per player |

### Voice Playback

| Parameter | Default | Description |
|---------|---------|-------------|
| Voice Min Delay | 8s | Minimum delay between voice playback |
| Voice Max Delay | 15s | Maximum delay between voice playback |
| Voice Filter Enabled | false | Enables audio filters on voices |

### Experimental

| Parameter | Default | Description |
|---------|---------|-------------|
| Sampling Rate | 48000 Hz | Audio sampling rate (change if microphone issues occur) |

---

## Effects on the Player

### Affected Player (Local)

When a Whispral attaches to you:

1. **Teammate Invisibility**: Real players become invisible
    - Their body mesh disappears
    - Their nameplate disappears
    - Their flashlight becomes invisible
    - Their voice chat is muted
    - Their map tool animation becomes invisible

2. **Fake Players Appearance**: Hallucinations appear instead
    - One Fake Players per invisible player
    - Spawned 5–15m from the affected player
    - Precomputed positions to avoid freezes

### Other Players (Unaffected)

Players who are not affected see:
- The **dilated pupils** of the affected player (DilatedPupilsDebuff)

---
## FAQ

### Q: Do other players see my hallucinations?

No. Fake Players only exist for the affected player.

### Q: How can I tell if a teammate is a hallucination?

Some clues:
- Strange behavior (stalking, sudden fleeing)
- No response to voice chat

### Q: Does the mod work in solo?

No. The mod requires multiplayer, as hallucinations copy other players.

### Q: Are the voices really my teammates’ voices?

Yes. The mod records voices during the session and replays them through Fake Players.

### Q: How do I get rid of the Whispral?

- Throw objects at it to stun it
- Wait for the attachment duration to end (~240s)
- Stun yourself (pressing Q works)

### Q: Is audio data saved?

Audio samples are stored temporarily during the session.

---

## Credits

- **Developer**: VieuxPoulpe
- **Dependency**: [REPOLib](https://thunderstore.io/c/repo/p/Zehs/REPOLib/)
- **LostDroid Prefab**: Based on WesleysEnemies