# The game's `Sound`

How REPO plays a `Sound`, as read in the decompiled game, v0.5.0
(`../Repo0.5.0/Assets/REPO/Game/Scripts/Assembly-CSharp/`). The Whispers rely on it (`Whispers.cs`). Line numbers are
the game's.

## `Sound.Play`

- `Play(Vector3)` (`Sound.cs:99`) draws an `AudioClip` from `Sounds` at `:105`, names the new object after it and,
  for any `Type` but `Persistent`, destroys that object after `clip.length / pitch` (`:125`), then draws again for the
  clip it plays (`:141`). With
  several clips the two draws differ: the object's name and lifetime belong to another clip than the one heard. A
  `Sounds` of one clip, picked by the caller, keeps them in step.
- Without a `Source`, it instantiates the `AudioManager` prefab of its `Type` (`AudioPrefab()`, `:198`; `AudioDefault`
  by default) under `AudioManager.instance.SoundsParent` (`:115`): the source lives apart from the object that played
  it, and outlives it. Without an `AudioManager`, it plays nothing and returns null (`:111`).
- `Play(Transform, Vector3 contactPoint)` (`:181`) adds a `SoundFollowTransform` holding the contact point in the
  target's space: given the camera and a point beside it, the sound follows the head.

## `AudioLowPassLogic` owns the volume

The `AudioManager` prefabs carry an `AudioLowPassLogic`, set up by `Play` when `SpatialBlend > 0` (`StartLowPass`,
`:156`). `Setup()` (`AudioLowPassLogic.cs:71`) reads `AudioSource.volume` once into its `Volume` (`:101`); its
`UpdateMe` (`:108`), which `AudioManager.Update` calls every frame (`AudioManager.cs:147`) on each component registered
in `OnEnable` (`AudioLowPassLogic.cs:172`), then sets `AudioSource.volume` from `Volume`, lowered while an obstacle
muffles the sound. A volume written on the `AudioSource` is undone in the next frame: a fade writes
`AudioLowPassLogic.Volume` instead.
