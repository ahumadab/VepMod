using UnityEngine;

namespace VepMod.Enemies.Whispral;

/// <summary>
///     The visual part of the game's effect when the local player sees a dead teammate's head
///     (PlayerDeathHead, its localSeenEffect).
/// </summary>
// A component of its own: the effect runs to its end whatever state the Hallucination goes to.
internal sealed class DeadTeammateEffect : MonoBehaviour
{
    private const float Duration = 2f; // s

    private float _left;

    /// <summary>
    ///     Restarts the effect from its beginning when it is already playing.
    /// </summary>
    // Without the game's seenSound and AudioScare.PlayCustom, which also cuts the level music for 60 s: the Lunge
    // count stays silent. PlayerController.deathSeenTimer is left alone so that a real dead teammate's head still
    // plays the game's own effect.
    public void Play()
    {
        _left = Duration;
        GameDirector.instance.CameraImpact.Shake(2f, 0.5f);
        GameDirector.instance.CameraShake.Shake(2f, 1f);
    }

    private void Update()
    {
        if (_left <= 0f) return;

        _left -= Time.deltaTime;
        CameraZoom.Instance.OverrideZoomSet(75f, 0.1f, 0.25f, 0.25f, gameObject, 150);
        PostProcessing.Instance.VignetteOverride(Color.black, 0.4f, 1f, 1f, 0.5f, 0.1f, gameObject);
        PostProcessing.Instance.SaturationOverride(-50f, 1f, 0.5f, 0.1f, gameObject);
        PostProcessing.Instance.ContrastOverride(5f, 1f, 0.5f, 0.1f, gameObject);
        GameDirector.instance.CameraImpact.Shake(10f * Time.deltaTime, 0.1f);
        GameDirector.instance.CameraShake.Shake(10f * Time.deltaTime, 1f);
    }
}
