# The game's player death

What REPO does to a player's avatar when they die and when they are revived, as read in the decompiled game
(`../Repo_04/Assets/REPO/Game/Scripts/Assembly-CSharp/`). Line numbers are the game's.

- `PlayerDeath` (`PlayerAvatar.cs:1322`) sends `PlayerDeathRPC` (`:1336`), which sets `deadSet` and, for the local
  player, deactivates the `PlayerController`'s parent object (`:1374`), not the avatar.
- About half a second later, `deadTimer` runs out in `Update` (`:705`) and `PlayerDeathDone` (`:1380`) ends with
  `gameObject.SetActive(false)` on the avatar (`:1423`): every `MonoBehaviour` on it stops its `Update` and
  coroutines until the revive.
- `ReviveRPC` (`:1506`) reactivates the avatar (`:1537`).
- An RPC another object sends still reaches a component on the dead avatar: the Whispral's detach calls
  `Haunting.Detach` there, and the Haunting ends at the death. What that call starts and must run on (a fade, a
  release) runs on an object that stays active: `Whispers` lives on its own object for that reason.
