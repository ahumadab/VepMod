# The game's player death

What REPO does to a player's avatar when they die and when they are revived, as read in the decompiled game, v0.5.0
(`../Repo0.5.0/Assets/REPO/Game/Scripts/Assembly-CSharp/`). Line numbers are the game's.

- `PlayerDeath` (`PlayerAvatar.cs:1544`) sends `PlayerDeathRPC` (`:1558`), which sets `deadSet` and, for the local
  player, deactivates the `PlayerController`'s parent object (`:1597`), not the avatar.
- About half a second later (`deadTime`, `:223`), `deadTimer` runs out in `Update` (`:877`) and `PlayerDeathDone`
  (`:1603`) ends with `gameObject.SetActive(false)` on the avatar (`:1646`): every `MonoBehaviour` on it stops its
  `Update` until the revive, and its coroutines for good.
- `ReviveRPC` (`:1738`) makes the player invincible for 1 s (`playerHealth.InvincibleSet(1f)`, `:1750`) and reactivates the
  avatar (`:1777`).
- A method another object calls still runs on a component of the deactivated avatar, an RPC handler included.
