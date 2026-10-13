## Game reference

REPO game source (decompiled, read-only): `../Repo_04/Assets/REPO/Game/Scripts/Assembly-CSharp/`. Read it before using, patching or imitating a game class (`PlayerAvatar`, `SemiFunc`, `EnemyParent`…). The mod compiles against the `R.E.P.O.GameLibs.Steam` NuGet package, so on a mismatch the compiler wins over this copy.

## Coding standards

Before writing, changing or reviewing code, read `CODING_STANDARDS.md` (module shape, lifecycle, network contract,
comments).

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
