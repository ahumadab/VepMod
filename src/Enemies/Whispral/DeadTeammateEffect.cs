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

    // R.E.P.O. v0.5.0 added an optional _priority to VignetteOverride, and its November 2026 update to
    // SaturationOverride and ContrastOverride; R.E.P.O.GameLibs.Steam stops at 0.4.4: a direct call compiles to the
    // signature without it, which throws MissingMethodException on every Update. Reflection fills the optional
    // parameters with their defaults whatever the game's version. Call them directly again once the package catches up.
    private static readonly MethodInfo VignetteOverride =
        AccessTools.Method(typeof(PostProcessing), nameof(PostProcessing.VignetteOverride));

    private static readonly MethodInfo SaturationOverride =
        AccessTools.Method(typeof(PostProcessing), nameof(PostProcessing.SaturationOverride));

    private static readonly MethodInfo ContrastOverride =
        AccessTools.Method(typeof(PostProcessing), nameof(PostProcessing.ContrastOverride));

    private float _left;
    private object?[] _vignetteArgs = null!;
    private object?[] _saturationArgs = null!;
    private object?[] _contrastArgs = null!;

    private void Awake()
    {
        _vignetteArgs = WithDefaults(VignetteOverride, [Color.black, 0.4f, 1f, 1f, 0.5f, 0.1f, gameObject]);
        _saturationArgs = WithDefaults(SaturationOverride, [-50f, 1f, 0.5f, 0.1f, gameObject]);
        _contrastArgs = WithDefaults(ContrastOverride, [5f, 1f, 0.5f, 0.1f, gameObject]);
    }

    private static object?[] WithDefaults(MethodInfo method, object[] args)
    {
        return method.GetParameters()
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
        SaturationOverride.Invoke(PostProcessing.Instance, _saturationArgs);
        ContrastOverride.Invoke(PostProcessing.Instance, _contrastArgs);
        GameDirector.instance.CameraImpact.Shake(10f * Time.deltaTime, 0.1f);
        GameDirector.instance.CameraShake.Shake(10f * Time.deltaTime, 1f);
    }
}
