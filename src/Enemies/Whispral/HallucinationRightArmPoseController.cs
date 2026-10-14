using UnityEngine;

#pragma warning disable CS8618

namespace VepMod.Enemies.Whispral;

/// <summary>
///     The arm pose logic of PlayerAvatarRightArm, rebuilt for a Hallucination without PlayerAvatar or
///     MapToolController: code_arm_r goes from basePose to mapPose along an AnimationCurve while
///     Hallucination.WantsGrabbing is set.
/// </summary>
public sealed class HallucinationRightArmPoseController : MonoBehaviour
{
    private Vector3 _basePose;
    private Hallucination _hallucination;
    private Vector3 _mapPose;
    private Vector3 _poseCurrent;
    private AnimationCurve _poseCurve;
    private float _poseLerp;

    private Vector3 _poseNew;
    private Vector3 _poseOld;
    private float _poseSpeed;
    private Transform _rightArmTransform;

    private void Update()
    {
        if (_hallucination == null || _rightArmTransform == null) return;

        var target = _hallucination.WantsGrabbing ? _mapPose : _basePose;

        if (target != _poseNew)
        {
            _poseOld = _poseCurrent;
            _poseNew = target;
            _poseLerp = 0f;
        }

        if (_poseLerp < 1f)
        {
            _poseLerp += _poseSpeed * Time.deltaTime;
            var t = _poseCurve != null ? _poseCurve.Evaluate(_poseLerp) : Mathf.Clamp01(_poseLerp);
            _poseCurrent = Vector3.LerpUnclamped(_poseOld, _poseNew, t);
        }
        else
        {
            _poseCurrent = _poseNew;
        }

        _rightArmTransform.localEulerAngles = _poseCurrent;
    }

    public void Initialize(Hallucination hallucination, Transform rightArmTransform,
        Vector3 basePose, Vector3 mapPose, AnimationCurve poseCurve, float poseSpeed)
    {
        _hallucination = hallucination;
        _rightArmTransform = rightArmTransform;
        _basePose = basePose;
        _mapPose = mapPose;
        _poseCurve = poseCurve;
        _poseSpeed = poseSpeed;
        _poseCurrent = basePose;
        _poseNew = basePose;
        _poseOld = basePose;
        _poseLerp = 1f;
    }
}
