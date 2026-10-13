using UnityEngine;
using VepMod.VepFramework;

#pragma warning disable CS8618

namespace VepMod.Enemies.Whispral;

/// <summary>
///     The arm pose logic of PlayerAvatarLeftArm, rebuilt for a Hallucination. In the game, the left arm, which
///     holds the flashlight, rises to flashlightPose while the flashlight is on and the player is not crawling.
///     A Hallucination's flashlight is always on and it never crawls, so the arm always aims at flashlightPose.
///     The game script is stripped when the visuals are cloned; without this controller the arm would keep the
///     rig's baked pose, usually the lowered basePose.
///     headRotation (the camera pitch offset) is ignored, as for the right arm.
/// </summary>
public sealed class DroidLeftArmPoseController : MonoBehaviour
{
    private static readonly VepLogger LOG = VepLogger.Create<DroidLeftArmPoseController>();

    private Transform _leftArmTransform;
    private Vector3 _flashlightPose;
    private AnimationCurve _poseCurve;
    private float _poseSpeed;

    private Vector3 _poseCurrent;
    private Vector3 _poseOld;
    private float _poseLerp;

    private void Update()
    {
        if (_leftArmTransform == null) return;

        if (_poseLerp < 1f)
        {
            _poseLerp += _poseSpeed * Time.deltaTime;
            var t = _poseCurve != null ? _poseCurve.Evaluate(_poseLerp) : Mathf.Clamp01(_poseLerp);
            _poseCurrent = Vector3.LerpUnclamped(_poseOld, _flashlightPose, t);
        }
        else
        {
            _poseCurrent = _flashlightPose;
        }

        _leftArmTransform.localEulerAngles = _poseCurrent;
    }

    public void Initialize(Transform leftArmTransform, Vector3 basePose, Vector3 flashlightPose,
        AnimationCurve poseCurve, float poseSpeed)
    {
        _leftArmTransform = leftArmTransform;
        _flashlightPose = flashlightPose;
        _poseCurve = poseCurve;
        _poseSpeed = poseSpeed;

        // Starts at basePose so the arm rises smoothly when the Hallucination spawns.
        _poseOld = basePose;
        _poseCurrent = basePose;
        _poseLerp = 0f;
    }
}
