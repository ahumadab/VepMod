using UnityEngine;
using VepMod.VepFramework;

#pragma warning disable CS8618

namespace VepMod.Enemies.Whispral;

/// <summary>
///     Clone de la logique de pose de PlayerAvatarRightArm pour le droid AI.
///     Interpole localEulerAngles de code_arm_r entre basePose et mapPose via
///     une AnimationCurve, sans dépendance à PlayerAvatar source ou MapToolController.
///     Pose cible pilotée par DroidController.WantsGrabbing.
/// </summary>
public sealed class DroidRightArmPoseController : MonoBehaviour
{
    private static readonly VepLogger LOG = VepLogger.Create<DroidRightArmPoseController>();
    private Vector3 _basePose;
    private DroidController _droid;
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
        if (_droid == null || _rightArmTransform == null) return;

        var target = _droid.WantsGrabbing ? _mapPose : _basePose;

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

    public void Initialize(DroidController droid, Transform rightArmTransform,
        Vector3 basePose, Vector3 mapPose, AnimationCurve poseCurve, float poseSpeed)
    {
        _droid = droid;
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