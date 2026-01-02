using UnityEngine;
using VepMod.VepFramework;

namespace VepMod.Enemies.Whispral;

/// <summary>
///     Relais pour les animation events du HallucinationDroid.
///     Doit être placé sur le même GameObject que l'Animator (Cube).
/// </summary>
public class HallucinationAnimEvents : MonoBehaviour
{
    private static readonly VepLogger LOG = VepLogger.Create<HallucinationAnimEvents>();
    private HallucinationDroid _droid;

    private void Awake()
    {
        _droid = GetComponentInParent<HallucinationDroid>();
    }

    public void PlayMediumFootstep()
    {
        _droid?.PlayMediumFootstep();
    }
}