# Coding standards

Judgement calls a reviewer applies to every change. Anything a tool can check (formatting, analyzers, build, tests)
belongs to the tooling, not here.

## Domain language

- Types, members, log messages and comments use the terms of `GLOSSARY.md`. A word the glossary lists under _Avoid_
  (droid, debuff, target, source player, clip…) is a finding.
- A change that introduces a domain concept adds its glossary entry in the same change.

## Module shape

- Split by concept (Haunting, Imitations, Voice Bank), not by technical role (Controller, Helper, Manager). One feature
  is understood from one module; a feature that has to touch five files is the smell.
- A member that only forwards to another object is a finding: callers talk to the module that owns the behaviour.
- Other modules read state through the owner's interface. Public mutable flags written by one class and read by its
  siblings are a finding.
- Each fact has one home: a distance, a timing, a file format, a sample rate is defined once.
- Logic that can run without Unity or Photon lives in plain C# behind the MonoBehaviour, and is tested there.

## Lifecycle

- Every state and every resource has one exit that cleans it up, reached whatever the reason for leaving. Cleanup copied
  into several exit paths is a finding.
- A call that deactivates or destroys the object running a state machine (`EnemyParent.Despawn`, `SetActive(false)`,
  `Destroy`) skips its state exits: the state machine leaves its state before that call.
- A MonoBehaviour that changes other objects (hides a player, spawns a Hallucination) undoes it in `OnDestroy`.
- Undoing a change to a game object gives back the value the game would hold now: recorded before the change, or
  read from its owner's state (`FlashlightController.currentState`), not a hard-coded one.
- Per-frame code does not allocate, and does not retry the same failure every frame.

## Network contract

- RPC method names and parameters are a wire contract: changing them breaks players on another mod version, and the PR
  says so.
- Classes and serialized fields referenced by an asset bundle keep their names (`EnemyWhispral`, `EnemyWhispralAnim`).
- The Master decides, every client applies, and handlers that run on every client are idempotent.
- Hallucinations never go on the network (ADR-0002).

## Threads

- Code reached from the Photon voice thread calls no Unity API and hands data to the main thread through a synchronised
  hand-off.

## Decisions

- A change that contradicts an ADR in `docs/adr/` supersedes that ADR in the same change; otherwise it is a finding.

## Comments

The default is none. Comments drift into narration that repeats the line underneath and rots as soon as the code moves.

- Never comment what the code does. If removing the comment loses no information, it should not exist.
- Names carry intent. Code that seems to need a comment usually needs a rename or a split.
- Comment only what the code cannot show:
  - a game, Photon or Unity behaviour, bug or version quirk being worked around;
  - a constraint from outside the file: the REPO game, the asset bundle, the network contract, a thread;
  - a deliberate omission and its reason;
  - a choice that looks wrong and that someone would otherwise "fix".
- Half a comment is no comment: keep the justification, cut the narration.
- Describe only what exists.
- Comments are written in English.

### XML documentation

`///` documents a surface for its callers. The default is none here too: it earns its place only by carrying what the
signature cannot (a unit, what `null` means, an invariant, an ordering constraint, a thread requirement). It never
restates the name, and a maintainer's rationale is a comment, judged as one. A note that fails its test is deleted, not
moved to the other form.

### Tool directives

None of the above applies to analyzer suppressions and the justification they require.
