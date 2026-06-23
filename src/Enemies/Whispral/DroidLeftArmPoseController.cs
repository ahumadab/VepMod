using UnityEngine;
using VepMod.VepFramework;

#pragma warning disable CS8618

namespace VepMod.Enemies.Whispral;

/// <summary>
///     Clone de la logique de pose de PlayerAvatarLeftArm pour le droid AI.
///     En vanilla, le bras gauche (qui porte la flashlight) est levé en
///     'flashlightPose' tant que la lampe est allumée et que le joueur ne crawl
///     pas, sinon il retombe en 'basePose'. Le droid a sa lampe TOUJOURS allumée
///     et ne crawl jamais : on vise donc en permanence flashlightPose. Sans ce
///     controller, le bras reste à la pose bakée du rig (souvent basePose, bras
///     baissé) car le script source a été strippé au clonage.
///
///     On ignore le headRotation (offset pitch caméra) comme pour le bras droit.
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

        // Démarre à basePose et lerp vers flashlightPose (montée douce du bras au spawn).
        _poseOld = basePose;
        _poseCurrent = basePose;
        _poseLerp = 0f;
    }
}
