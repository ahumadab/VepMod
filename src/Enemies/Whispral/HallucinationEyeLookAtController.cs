using UnityEngine;

#pragma warning disable CS8618

namespace VepMod.Enemies.Whispral;

/// <summary>
///     PlayerEyes.EyeLookAt rebuilt for a Hallucination: while Hallucination.WantsGrabbing is set, code_eye_left
///     and code_eye_right turn towards the Player Look Target of the cloned map tool. Same LookRotation and Y clamp
///     as the game, but the rotation is applied directly instead of through a spring, and eases back to rest.
/// </summary>
public sealed class HallucinationEyeLookAtController : MonoBehaviour
{
    // From PlayerEyes.LateUpdate in the game.
    private const float ClampX = 50f;
    private const float ClampY = 30f;
    private const float ReturnSpeed = 8f;

    private Hallucination _hallucination;
    private Transform _eyeLeft;

    private Quaternion _eyeLeftRest;
    private Transform _eyeRight;
    private Quaternion _eyeRightRest;
    private Transform _lookTarget;

    private void LateUpdate()
    {
        if (_hallucination == null) return;

        if (_hallucination.WantsGrabbing && _lookTarget != null)
        {
            ApplyLookAt(_eyeLeft);
            ApplyLookAt(_eyeRight);
        }
        else
        {
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

    public void Initialize(Hallucination hallucination, Transform eyeLeft, Transform eyeRight, Transform lookTarget)
    {
        _hallucination = hallucination;
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

        eye.rotation = Quaternion.LookRotation(dir);

        var euler = eye.localEulerAngles;
        euler.y = ClampAngle(euler.y, ClampY);
        euler.x = ClampAngle(euler.x, ClampX);
        eye.localEulerAngles = euler;
    }

    private static float ClampAngle(float angle, float limit)
    {
        // Unity reports euler angles in [0, 360]; the clamp is symmetric around 0.
        if (angle > limit && angle < 180f) return limit;
        if (angle < 360f - limit && angle > 180f) return 360f - limit;
        return angle;
    }
}
