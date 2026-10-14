## Game reference

REPO game source (decompiled, read-only): `../Repo_04/Assets/REPO/Game/Scripts/Assembly-CSharp/`, with Photon's beside it in `PhotonUnityNetworking/` and `PhotonRealtime/`. Read it before using, patching or imitating a game or Photon class (`PlayerAvatar`, `SemiFunc`, `EnemyParent`, a PUN callback…), and before stating how the game behaves in a ticket, spec or comment. The mod compiles against the `R.E.P.O.GameLibs.Steam` NuGet package, so on a mismatch the compiler wins over this copy.

Asset bundle sources (prefabs, animator controllers, readable YAML): `../Repo_04/Assets/VepMod/`. Read them before deciding whether a bundle references a class, field or clip; the built bundles in `Ressources/` are compressed. A script or clip GUID resolves through the `.meta` files under `../Repo_04/Assets/`.

## In-game tests

The tester confirms what the game shows; only the BepInEx logs show the state behind it (a Hallucination on the floor
but off the NavMesh), and you read them after the test to confirm each acceptance criterion. So before asking for an
in-game test, log at `Debug` every transition under test with the data that proves it, and give every action the tester
cannot trigger at will a key in `src/Dev/DroidDevTools.cs`, whose header lists the keys.

## Coding standards

Before writing, changing or reviewing code, read `CODING_STANDARDS.md` (module shape, lifecycle, network contract,
comments).

Run `npm run check` before handing work back: it runs what the git hooks and CI run, and `npm run format` fixes the
formatting it reports. The hooks are installed by
`npm install`, so a fresh clone or worktree has none until then.

## Git workflow

Gitflow, with `develop` as the default branch:

- Work on `<type>/<slug>` branched from `develop`, where `<type>` is the Conventional Commits type of the work (`feat`,
  `fix`, `refactor`, `docs`, `chore`…), and open the PR against `develop`.
- `release/<version>` and `hotfix/<slug>` merge into `main`, then back into `develop`.

## Agent skills

### Issue tracker

Issues are tracked in GitHub Issues (ahumadab/VepMod) via the `gh` CLI. Before starting or finishing a ticket, or running
`gh` on an issue, read `docs/agents/issue-tracker.md`: it moves the ticket across the project board.

### Triage labels

Default canonical labels: needs-triage, needs-info, ready-for-agent, ready-for-human, wontfix. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: one `GLOSSARY.md` and `docs/adr/` at the repo root. See `docs/agents/domain.md`.
