using UnityEngine;
using VepMod.VepFramework;

#pragma warning disable CS8618

namespace VepMod.Enemies.Whispral;

/// <summary>
///     Clone simplifié de PlayerEyes.EyeLookAt pour le droid AI : rotate
///     code_eye_left + code_eye_right vers le Player Look Target du MapTool
///     cloné quand DroidController.WantsGrabbing est true. Logique identique
///     au source (Quaternion.LookRotation + clamp Y) mais sans spring (rotation
///     directe pour MVP) et avec un lerp doux du retour à la pose de repos.
/// </summary>
public sealed class DroidEyeLookAtController : MonoBehaviour
{
    // Valeurs prises de PlayerEyes.LateUpdate ligne 375-376 du source.
    private const float ClampX = 50f;
    private const float ClampY = 30f;
    private const float ReturnSpeed = 8f;
    private static readonly VepLogger LOG = VepLogger.Create<DroidEyeLookAtController>();

    private DroidController _droid;
    private Transform _eyeLeft;

    private Quaternion _eyeLeftRest;
    private Transform _eyeRight;
    private Quaternion _eyeRightRest;
    private Transform _lookTarget;

    private void LateUpdate()
    {
        if (_droid == null) return;

        if (_droid.WantsGrabbing && _lookTarget != null)
        {
            ApplyLookAt(_eyeLeft);
            ApplyLookAt(_eyeRight);
        }
        else
        {
            // Retour doux à la rotation de repos.
            if (_eyeLeft != null)
            {
                _eyeLeft.localRotation =
                    Quaternion.Slerp(_eyeLeft.localRotation, _eyeLeftRest, ReturnSpeed * Time.deltaTime);
            }

            if (_eyeRight != null)
            {
                _eyeRight.localRotation =
                    Quaternion.Slerp(_eyeRight.localRotation, _eyeRightRest, ReturnSpeed * Time.deltaTime);
            }
        }
    }

    public void Initialize(DroidController droid, Transform eyeLeft, Transform eyeRight, Transform lookTarget)
    {
        _droid = droid;
        _eyeLeft = eyeLeft;
        _eyeRight = eyeRight;
        _lookTarget = lookTarget;

        if (eyeLeft != null) _eyeLeftRest = eyeLeft.localRotation;
        if (eyeRight != null) _eyeRightRest = eyeRight.localRotation;
    }

    private void ApplyLookAt(Transform eye)
    {
        if (eye == null) return;

        var dir = _lookTarget.position - eye.position;
        if (dir.sqrMagnitude < 0.0001f) return;

        // World rotation vers la cible, puis on extrait l'euler local pour clamper.
        eye.rotation = Quaternion.LookRotation(dir);

        var euler = eye.localEulerAngles;
        euler.y = ClampAngle(euler.y, ClampY);
        euler.x = ClampAngle(euler.x, ClampX);
        eye.localEulerAngles = euler;
    }

    private static float ClampAngle(float angle, float limit)
    {
        // Unity expose les eulers en [0, 360]. On veut clamper symétriquement
        // autour de 0 dans [-limit, +limit].
        if (angle > limit && angle < 180f) return limit;
        if (angle < 360f - limit && angle > 180f) return 360f - limit;
        return angle;
    }
}