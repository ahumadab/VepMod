using System.Linq;
using System.Reflection;
using HarmonyLib;
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

    // R.E.P.O. v0.5.0 added an optional _priority to VignetteOverride, and R.E.P.O.GameLibs.Steam stops at 0.4.4: a
    // direct call compiles to the 7-parameter signature, which throws MissingMethodException in v0.5.0 on every
    // Update. Call it directly again once the package ships 0.5.0.
    private static readonly MethodInfo VignetteOverride =
        AccessTools.Method(typeof(PostProcessing), nameof(PostProcessing.VignetteOverride));

    private float _left;
    private object?[] _vignetteArgs = null!;

    private void Awake()
    {
        object[] args = [Color.black, 0.4f, 1f, 1f, 0.5f, 0.1f, gameObject];
        _vignetteArgs = VignetteOverride.GetParameters()
            .Select((parameter, i) => i < args.Length ? args[i] : parameter.DefaultValue)
            .ToArray();
    }

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
        VignetteOverride.Invoke(PostProcessing.Instance, _vignetteArgs);
        PostProcessing.Instance.SaturationOverride(-50f, 1f, 0.5f, 0.1f, gameObject);
        PostProcessing.Instance.ContrastOverride(5f, 1f, 0.5f, 0.1f, gameObject);
        GameDirector.instance.CameraImpact.Shake(10f * Time.deltaTime, 0.1f);
        GameDirector.instance.CameraShake.Shake(10f * Time.deltaTime, 1f);
    }
}
