using UnityEngine;
using VepMod.VepFramework;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

namespace VepMod.Enemies.Whispral;

/// <summary>
///     Relais pour les animation events du HallucinationDroid.
///     Doit être placé sur le même GameObject que l'Animator (Cube).
/// </summary>
public sealed class DroidFootstepsEventReceiver : MonoBehaviour
{
    private static readonly VepLogger LOG = VepLogger.Create<DroidFootstepsEventReceiver>(true);
    private DroidController _droid;

    public void Initialize(DroidController droidController)
    {
        _droid = droidController;
    }

    public void PlayMediumFootstep()
    {
        _droid.PlayMediumFootstep();
    }
}