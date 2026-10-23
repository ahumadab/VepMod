# The game's `Sound`

How REPO plays a `Sound`, as read in the decompiled game (`../Repo_04/Assets/REPO/Game/Scripts/Assembly-CSharp/`).
The Whispers rely on it (`Whispers.cs`). Line numbers are the game's.

## `Sound.Play`

- `Play(Vector3)` (`Sound.cs:99`) draws an `AudioClip` from `Sounds` at `:105`, names the new object after it and,
  for any `Type` but `Persistent`, destroys that object after `clip.length / pitch` (`:154`), then draws again for the
  clip it plays (`:170`). With
  several clips the two draws differ: the object's name and lifetime belong to another clip than the one heard. A
  `Sounds` of one clip, picked by the caller, keeps them in step.
- Without a `Source`, it instantiates the `AudioManager` prefab of its `Type` (`AudioDefault` by default) under
  `AudioManager.instance.SoundsParent` (`:144`): the source lives apart from the object that played it, and outlives
  it.
- `Play(Transform, Vector3 contactPoint)` (`:206`) adds a `SoundFollowTransform` holding the contact point in the
  target's space: given the camera and a point beside it, the sound follows the head.

## `AudioLowPassLogic` owns the volume

The `AudioManager` prefabs carry an `AudioLowPassLogic`, set up by `Play` when `SpatialBlend > 0` (`StartLowPass`,
`:185`). `Setup()`
(`AudioLowPassLogic.cs:69`) reads `AudioSource.volume` once into its `Volume` (`:99`); its `Update` (`:106`) then
sets `AudioSource.volume` from `Volume` every frame, lowered while an obstacle muffles the sound. A volume written on
the `AudioSource` is undone in the next frame: a fade writes `AudioLowPassLogic.Volume` instead.
