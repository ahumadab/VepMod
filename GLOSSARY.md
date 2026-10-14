# VepMod

A mod for the game REPO that adds the Whispral, an enemy that makes a player hallucinate their teammates.

## Language

### The enemy and its prey

**Whispral**:
An invisible enemy that hunts players and attaches itself to one of them.

**Victim**:
The player a Whispral is attached to.
_Avoid_: Affected player, target, attached player, host

**Imitated Player**:
A teammate, alive or dead, who is hidden from the Victim and replaced, for the Victim only, by a Hallucination of themselves; a dead teammate's Hallucination keeps walking and keeps its name.
_Avoid_: Source player, hidden player

**Attachment**:
The link between one Whispral and its Victim, from the moment it attaches until it lets go for any reason: it detaches, is stunned or despawns, or the Victim is stunned, dies or extracts.

**Haunted**:
The state of a Victim while at least one Attachment is ongoing; it ends only when the last Whispral lets go, and a revived Victim is not Haunted again.

**Haunting**:
One period during which a Victim is Haunted, from the first Attachment to the end of the last.

**Effect**:
One of the consequences a Victim suffers while Haunted: Vanish, Hallucinations, or Dilated Pupils.
_Avoid_: Debuff

**Vanish**:
The Effect that hides every Imitated Player from the Victim's eyes and ears: body, shadows, nameplate, flashlight, map tool, grabber, voice chat and chat TTS, body and grab sounds, their death and death head.
_Avoid_: Invisibility, invisible player

**Imitation**:
What Vanish and Hallucinations do to one Imitated Player together: hide the real teammate and put their Hallucination in their place.

**Hallucination**:
A fake copy of an Imitated Player, seen only by the Victim, with the same look, colour and name.
_Avoid_: Fake player, droid, clone, LostDroid

**Stalking**:
A Hallucination approaching the Victim, staring at them, then fleeing a moment after the Victim's Gaze lands on it, even if the Victim looks away.

**Gaze**:
The Victim looking at a Hallucination they can see; a Hallucination behind a wall is never under the Gaze.

**Dilated Pupils**:
The only Effect visible to the Victim's teammates rather than to the Victim.

### Network

**Master**:
The client that runs the Whispral and decides when a Hallucination performs a Mimic.
_Avoid_: Host

### Voice

**Capture**:
A stretch of the local player's voice taken from the voice chat, from the moment they talk until a short silence or a length cap; it becomes a Voice Sample only if it passes the duration and speech checks.

**Voice Sample**:
A short recording of a player's real voice, kept only if it contains speech, and shared with every other player.
_Avoid_: Clip, recording, extract

**Voice Bank**:
The Voice Samples a client holds for one player, kept across sessions, capped in size, oldest dropped first.

**Mimic**:
A Hallucination speaking with a Voice Sample of its Imitated Player.
