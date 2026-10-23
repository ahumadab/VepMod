## Game reference

REPO game source (decompiled, read-only): `../Repo_04/Assets/REPO/Game/Scripts/Assembly-CSharp/`, with Photon's beside it in `PhotonUnityNetworking/` and `PhotonRealtime/`. Read it before using, patching or imitating a game or Photon class (`PlayerAvatar`, `SemiFunc`, `EnemyParent`, a PUN callback…), and before stating how the game behaves in a question, ticket, spec or comment. The mod compiles against the `R.E.P.O.GameLibs.Steam` NuGet package, publicized: a private game member reads and calls directly. On a mismatch the compiler wins over this copy.

Game behaviours already traced, with their lines: `docs/game/` (an enemy's spawn and despawn timers, a `Sound` and who owns its volume, a player's death and revive). Read the note on the class you touch before its source.

Asset bundle sources (prefabs, animator controllers, readable YAML): `../Repo_04/Assets/VepMod/`. Read them before deciding whether a bundle references a class, field or clip. A script or clip GUID resolves through the `.meta` files under `../Repo_04/Assets/`. The bundles that ship, built from older sources, sit in `Ressources/`: `VepMod.repobundle` (REPOLib) holds the Whispral, `vepmod_prefabs` (embedded in the DLL) the Hallucination. Read them before stating a serialized value (a duration, a range) or what the game plays today: `python tooling/bundle-dump.py [--bundle <file>]` lists a bundle's assets, `--script <Script>` prints that script's fields on every object holding it, references by name (setup: `pip install -r tooling/requirements.txt`).

Adding an asset to `vepmod_prefabs` (a sound, a prefab): `docs/bundle-rebuild.md`.

Game prefabs (readable YAML): `../Repo_04/Assets/REPO/Game/Resources/` (`PlayerAvatar.prefab`, `Player Death Head.prefab`…). Read them before deciding where a game object's renderers, lights, audio sources or `Sound` sources live. `node tooling/prefab-tree.mjs <file.prefab>` prints the hierarchy with each object's components; `--refs <Script>` names the object each of that script's fields points to (`PlayerEyes.targetIdle → Eye Target Idle`).

Unity's modules as the game ships them (`UnityEngine.*Module.dll`): `../Repo_04/AssetRipperOutput/AuxiliaryFiles/GameAssemblies/`. Read them before relying on an attribute or a base class of a Unity type (`[RequireComponent]` on `AudioLowPassFilter`).

## In-game tests

The tester confirms what the game shows; only the BepInEx logs show the state behind it (a Hallucination on the floor
but off the NavMesh), and you read them after the test to confirm each acceptance criterion. So before asking for an
in-game test, map every acceptance criterion to the action that triggers it and the `Debug` line that proves it, with
the data behind it. An action the tester cannot trigger at will (a command the Master
sends, a state reached only by chance, a bound) gets a key in `src/Dev/HallucinationDevTools.cs`, whose HUD (`OnGUI`) lists the keys.
A criterion that depends on a mod option: read its value in the profile's `BepInEx/config/com.vep.vepMod.cfg` (the
plugin's GUID; other VepMod `.cfg` files there are stale), and the plan names the value each test needs, which the
tester switches in game through REPOConfig. Then deploy the build
(`./build-dev.ps1 -DeployTo <profile>`) and name the commit that went, so the tester runs what you ask about. Work with
criteria seen in game is handed back with that deploy and the test plan, in the same reply.
After the test, `node tooling/test-log.mjs [--grep <regex>] [--exclude <regex>]` prints VepMod's lines and the game's errors from every
instance's log of that test, one section per level, with its local player and Master.

## Coding standards

Before writing, changing or reviewing code, or a ticket's desired behaviour, read `CODING_STANDARDS.md` (module shape,
lifecycle, network contract, comments).

Run `npm run check` before handing work back: it runs what the git hooks and CI run, and `npm run format` fixes the
formatting it reports. The hooks are installed by
`npm install`, so a fresh clone or worktree has none until then.

## Git workflow

Gitflow, with `develop` as the default branch:

- Work on `<type>/<slug>` branched from `develop`, where `<type>` is the Conventional Commits type of the work (`feat`,
  `fix`, `refactor`, `docs`, `chore`…), and open the PR against `develop` once `/mattpocock-skills:code-review develop`
  has reviewed the branch.
- `release/<version>` and `hotfix/<slug>` merge into `main`, then back into `develop`.

To publish a version, run `npm run release:start [version]` from an up-to-date `develop` (or a `hotfix/<slug>` cut from
`main`): it commits the version bump and the `CHANGELOG.md` on `release/<version>` and opens its PR against `main`.
Merge that PR with a merge commit, never a squash, then run `npm run release:finish` from an up-to-date `main`: it
builds `dist/VieuxPoulpe-VepMod-<version>.zip`, tags the version and merges `main` back into `develop`. Uploading the zip
to Thunderstore stays manual. Both take `--dry-run`.

## Agent skills

### Issue tracker

Issues are tracked in GitHub Issues (ahumadab/VepMod) via the `gh` CLI. Before starting or finishing a ticket, or running
`gh` on an issue, read `docs/agents/issue-tracker.md`: it moves the ticket across the project board.

### Triage labels

Default canonical labels: needs-triage, needs-info, ready-for-agent, ready-for-human, wontfix. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: one `GLOSSARY.md` and `docs/adr/` at the repo root. See `docs/agents/domain.md`.
