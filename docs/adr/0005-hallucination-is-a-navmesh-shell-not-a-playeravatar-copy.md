# A Hallucination is a NavMesh shell with a sanitized visual clone, never a PlayerAvatar copy

A Hallucination is built from the `MyDroid` prefab (NavMeshAgent + CharacterController, every Photon and enemy script stripped) onto which a live clone of the Imitated Player's visuals is grafted, keeping only renderers, animator, cosmetics and player materials. Copying the game's `PlayerAvatar` instead does not work: its `Awake` destroys the copy because it shares the real player's PhotonView owner, and if it survived it would join `GameDirector.PlayerList` (so enemies would target it), its `OnDestroy` would tell every enemy the real player left and destroy the whole player root, and it would drag along networked and input-driven parts (grabber, health, voice chat, flashlight, map tool) that ADR-0002 keeps off the network.

## Considered Options

- **Menu-avatar mode** (`PlayerAvatarMenu` / `isMenuAvatar`): `PlayerAvatarVisuals` can run without a networked avatar, but whether it supports locomotion is unverified; worth a look only if the visual clone becomes a maintenance burden.
