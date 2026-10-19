# VepMod

A mod for the game REPO that adds the Whispral, an enemy that makes a player hallucinate their teammates.

## Language

### The enemy and its prey

**Whispral**:
An invisible enemy that hunts players and attaches itself to one of them.
_French_: le Whispral

**Victim**:
The player a Whispral is attached to.
_Avoid_: Affected player, target, attached player, host
_French_: le joueur hanté, or la victime

**Imitated Player**:
A teammate, alive or dead, who is hidden from the Victim and replaced, for the Victim only, by a Hallucination of themselves; a dead teammate's Hallucination keeps walking and keeps its name.
_Avoid_: Source player, hidden player
_French_: le coéquipier imité

**Attachment**:
The link between one Whispral and its Victim, from the moment it attaches until it lets go for any reason: it detaches, is stunned or despawns, or the Victim is stunned, dies or extracts.
_French_: l'emprise

**Haunted**:
The state of a Victim while at least one Attachment is ongoing; it ends only when the last Whispral lets go, and a revived Victim is not Haunted again.
_French_: hanté, hantée

**Haunting**:
One period during which a Victim is Haunted, from the first Attachment to the end of the last.
_French_: la période de hantise

**Effect**:
One of the consequences a Victim suffers while Haunted: Vanish, Hallucinations, or Dilated Pupils.
_Avoid_: Debuff
_French_: l'effet

**Vanish**:
The Effect that hides every Imitated Player from the Victim's eyes and ears: body, shadows, nameplate, flashlight, map tool, grabber, voice chat and chat TTS, body and grab sounds, their death and death head.
_Avoid_: Invisibility, invisible player
_French_: la Disparition

**Imitation**:
What Vanish and Hallucinations do to one Imitated Player together: hide the real teammate and put their Hallucination in their place.
_French_: l'imitation

**Hallucination**:
A fake copy of an Imitated Player, seen only by the Victim, with the same look, colour and name. It wears a clone of
the Imitated Player's visuals (`HallucinationVisualsClone`): _clone_ names that Unity copy, never the Hallucination.
_Avoid_: Fake player, droid, clone, LostDroid
_French_: la Hallucination

**Trip**:
A Hallucination walking or sprinting along a NavMesh path to a point, from its start to the stop it eases into at the end; a switch between walking and sprinting does not end it. One time in three it reaches its point and goes straight on to another Trip at the same gait, never more than two Trips without a Wait.
_French_: le trajet

**Wait**:
A Hallucination standing still for 2 to 4 s after a Trip, or two in a row, never during Stalking; one Wait in four starts with a turn round.
_French_: la halte

**Glance**:
A waiting Hallucination looking off in a random direction, up to 120° either side of where it faced as its Wait started; the head turns alone up to 40° off the body, and the body follows beyond. A Hallucination changing its mind glances once the same way.
_French_: le coup d'œil

**Change of mind**:
A Hallucination on a Trip easing to a stop, glancing, then heading to another point 5 to 15 m away at the same gait, still on the same Trip; a 3 % chance each second of a Trip, at most once per Trip, never during Stalking.
_French_: le changement d'avis

**Talker**:
The Victim, or a Hallucination playing a Mimic, as a Hallucination hears them: its eyes and head turn to the nearest one within 6 m, up to 40° off the body, and hold them 2 to 4 s after they stop; a hidden teammate is never one, and a Stalking stare keeps the head.
_French_: la personne qui parle

**Stalking**:
A Hallucination approaching the Victim, staring at them without a word, then fleeing a moment after the Victim's Gaze lands on it, even if the Victim looks away; one the Gaze never lands on ends in a Lunge, unless the Master's Lethal Hallucinations option is off. A Victim is stalked by one Hallucination at a time.
_French_: la traque

**Lunge**:
A Stalking Hallucination within 5 m of the Victim throwing itself at them once it has stared at them unseen for 5 s, counted from when it first stopped close to them and only while nothing stands between them, the Victim's screen glitching each time the count starts or resumes; it is too late to stop, and it kills the Victim outright a second later, their camera forced onto its face as it speaks a Mimic, whether it reached them or not.
_Avoid_: Attack, jump scare, pounce
_French_: la ruée

**Gaze**:
The Victim looking at a Hallucination they can see; a Hallucination behind a wall is never under the Gaze.
_French_: le regard

**Dilated Pupils**:
The only Effect visible to the Victim's teammates rather than to the Victim.
_French_: les pupilles dilatées

### Network

**Master**:
The client that runs the Whispral and decides when a Hallucination performs a Mimic, except during a Lunge, and whether a Stalking can end in a Lunge, through its Lethal Hallucinations option.
_Avoid_: Host
_French_: l'hôte

### Voice

**Capture**:
A stretch of the local player's voice taken from the voice chat, from the moment they talk until a short silence or a length cap; it becomes a Voice Sample only if it passes the duration and speech checks.
_French_: la capture de voix

**Voice Sample**:
A short recording of a player's real voice, kept only if it contains speech, and shared with every other player.
_Avoid_: Clip, recording, extract
_French_: l'extrait de voix

**Voice Bank**:
The Voice Samples a client holds for one player, kept across sessions, capped in size, oldest dropped first.
_French_: la banque de voix

**Mimic**:
A Hallucination speaking with a Voice Sample of its Imitated Player.
_French_: l'imitation vocale
