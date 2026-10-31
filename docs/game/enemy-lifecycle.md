# The game's enemy lifecycle

How REPO spawns, despawns and brings back an enemy, as read in the decompiled game, v0.5.0
(`../Repo0.5.0/Assets/REPO/Game/Scripts/Assembly-CSharp/`). The Whispral departs from it on purpose: see
[ADR-0008](../adr/0008-the-whispral-never-leaves-the-level-on-its-own.md). Line numbers are the game's.

## Two timers on `EnemyParent`

- `Logic()` (`EnemyParent.cs:157`) is a coroutine resumed after every `Update`. While `Spawned`, it counts
  `SpawnedTimer` down, only when no player is close (`playerClose`) and no pause runs (`spawnedTimerPauseTimer`). At
  `SpawnedTimer <= 0` with no player close, it sets `Enemy.CurrentState = Despawn`. While despawned, it counts
  `DespawnedTimer` down and calls `Spawn()` at zero.
- `SpawnedTimerSet(t)` (`:267`) works only while `Spawned`; `t == 0` on the Master also sets the state to `Despawn` at
  once. `DespawnedTimerSet(t)` (`:279`) works only while despawned.
- `SpawnRPC` (`:363`) adds a 3 to 4 min `SpawnedTimerPause` (`:386`). `SpawnedTimerPause(0f)` (`:306`) clears the
  pause.
- `Despawn()` (`:392`) draws `DespawnedTimer` in `DespawnedTimeMin..Max` (480 to 500 s for the Whispral) times the
  director's `despawnedDecreaseMultiplier` and `despawnedTimeMultiplier`; a death multiplies it by 3 (`:449`), whether
  or not a valuable drops. On a death, `PlayerCloseCheck()` (`:409`) first refreshes `playerClose`, which decides the
  valuable. `DespawnRPC` (`:453`) sets `Spawned = false`; sent to `RpcTarget.All`, it runs on the sender at once.

## `EnemyDirector`

- Start pause: `spawnIdlePauseTimer` (`EnemyDirector.cs:127`), 2 to 3 min scaled by difficulty, read through
  `SemiFunc.EnemySpawnIdlePause()`. An enemy's Idle state waits for it to end.
- `SetInvestigate` with a radius of 15 m or more shortens every despawned enemy's timer by 5 s (`DisableDecrease`).
- End-of-level rule, once `RoundDirector.allExtractionPointsCompleted` (set in `ExtractionCompletedAllRPC`): every
  frame, a despawned enemy with `DespawnedTimer > 30` gets `DespawnedTimerSet(0f)` and respawns (`:188`); once, every
  spawned enemy with no player close gets `SpawnedTimerPause(0f)` and `SpawnedTimerSet(0f)` (`:205`). VepMod logs the
  flag's flip (`EnemyLoggerPatch`), and the dev key `Ctrl+Num8` replays the rule on the Whisprals.

## Where an enemy respawns

`SemiFunc.LevelPointGetPlayerDistance(pos, 18, 35)` (`SemiFunc.cs:1999`) returns a level point 18 to 35 m in a
straight line from a player, outside the truck, skipping disabled players. `SemiFunc.LevelPointGet(pos, min, max)`
(`:2302`) measures from `pos` alone.
