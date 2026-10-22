using UnityEngine;
using VepMod.VepFramework;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

namespace VepMod.Enemies.Whispral;

/// <summary>
///     A Hallucination's face: angry eyelids and talking animation.
/// </summary>
public sealed class HallucinationFace : MonoBehaviour
{
    // Talking animation
    private const float TalkRotationMaxAngle = 25f;
    private const int SampleDataLength = 256;

    private static readonly VepLogger LOG = VepLogger.Create<HallucinationFace>();

    private readonly AngryEyes _angryEyes = new();
    private Transform _controllerTransform;
    private Hallucination _hallucination;
    private GameObject? _eyelidsLeft;
    private GameObject? _eyelidsRight;

    // Talking animation
    private Transform? _headTopTransform;
    private bool _wasAngry;
    private Transform? _leftLowerEyelidRotationX;
    private Transform? _leftUpperEyelidRotationX;
    private Transform? _leftUpperEyelidRotationZ;
    private Transform? _rightLowerEyelidRotationX;
    private Transform? _rightUpperEyelidRotationX;
    private Transform? _rightUpperEyelidRotationZ;
    private float[]? _sampleData;
    private AudioSource? _talkingAudioSource;

    public void Initialize(Hallucination hallucination)
    {
        _hallucination = hallucination;
        _controllerTransform = hallucination.ControllerTransform;
    }

    /// <summary>
    ///     Called after setup.
    /// </summary>
    public void SetupEyelids(
        GameObject? eyelidsLeft,
        GameObject? eyelidsRight,
        Transform? leftUpperX,
        Transform? leftUpperZ,
        Transform? leftLowerX,
        Transform? rightUpperX,
        Transform? rightUpperZ,
        Transform? rightLowerX)
    {
        _eyelidsLeft = eyelidsLeft;
        _eyelidsRight = eyelidsRight;
        _leftUpperEyelidRotationX = leftUpperX;
        _leftUpperEyelidRotationZ = leftUpperZ;
        _leftLowerEyelidRotationX = leftLowerX;
        _rightUpperEyelidRotationX = rightUpperX;
        _rightUpperEyelidRotationZ = rightUpperZ;
        _rightLowerEyelidRotationX = rightLowerX;
    }

    public void SetupTalking(Transform headTopTransform)
    {
        _headTopTransform = headTopTransform;
    }

    /// <summary>
    ///     Called from LateUpdate.
    /// </summary>
    public void UpdateAnimations()
    {
        UpdateAngryEyes();
        UpdateTalkingAnimation();
    }

    private void UpdateAngryEyes()
    {
        if (_eyelidsLeft == null || _eyelidsRight == null) return;
        if (_leftUpperEyelidRotationX == null || _rightUpperEyelidRotationX == null) return;

        _angryEyes.Update(_hallucination.Gaze.IsOn, _hallucination.IsStalkingBeforeFlee, Time.deltaTime);
        var isAngry = _angryEyes.IsAngry;
        if (isAngry != _wasAngry)
        {
            LOG.Debug($"Angry eyes {(isAngry ? "on" : "off")}: Hallucination of {_hallucination.ImitatedPlayer.playerName} " +
                      $"in {_hallucination.DebugStateName}");
            _wasAngry = isAngry;
        }

        if (isAngry && !_eyelidsLeft.activeSelf)
        {
            _eyelidsLeft.SetActive(true);
            _eyelidsRight.SetActive(true);
        }

        var targetUpperClosed = isAngry ? AngryExpression.UpperLidClosed : 0f;
        var targetLowerClosed = isAngry ? AngryExpression.LowerLidClosed : 0f;
        var targetLeftAngle = isAngry ? AngryExpression.UpperLidAngleLeft : 0f;
        var targetRightAngle = isAngry ? AngryExpression.UpperLidAngleRight : 0f;

        var lerpSpeed = Time.deltaTime * 8f;

        _leftUpperEyelidRotationX.localRotation = Quaternion.Slerp(
            _leftUpperEyelidRotationX.localRotation,
            Quaternion.Euler(targetUpperClosed, 0f, 0f),
            lerpSpeed);

        _rightUpperEyelidRotationX.localRotation = Quaternion.Slerp(
            _rightUpperEyelidRotationX.localRotation,
            Quaternion.Euler(targetUpperClosed, 0f, 0f),
            lerpSpeed);

        if (_leftUpperEyelidRotationZ != null)
        {
            _leftUpperEyelidRotationZ.localRotation = Quaternion.Slerp(
                _leftUpperEyelidRotationZ.localRotation,
                Quaternion.Euler(0f, 0f, targetLeftAngle),
                lerpSpeed);
        }

        if (_rightUpperEyelidRotationZ != null)
        {
            _rightUpperEyelidRotationZ.localRotation = Quaternion.Slerp(
                _rightUpperEyelidRotationZ.localRotation,
                Quaternion.Euler(0f, 0f, targetRightAngle),
                lerpSpeed);
        }

        if (_leftLowerEyelidRotationX != null)
        {
            _leftLowerEyelidRotationX.localRotation = Quaternion.Slerp(
                _leftLowerEyelidRotationX.localRotation,
                Quaternion.Euler(targetLowerClosed, 0f, 0f),
                lerpSpeed);
        }

        if (_rightLowerEyelidRotationX != null)
        {
            _rightLowerEyelidRotationX.localRotation = Quaternion.Slerp(
                _rightLowerEyelidRotationX.localRotation,
                Quaternion.Euler(targetLowerClosed, 0f, 0f),
                lerpSpeed);
        }

        if (!isAngry && _eyelidsLeft.activeSelf)
        {
            var currentAngle = Mathf.Abs(_leftUpperEyelidRotationX.localEulerAngles.x);
            if (currentAngle is < 10f or > 350f)
            {
                _eyelidsLeft.SetActive(false);
                _eyelidsRight.SetActive(false);
            }
        }
    }

    private void UpdateTalkingAnimation()
    {
        if (_headTopTransform == null) return;
        var targetRotation = 0f;
        if (_talkingAudioSource == null && _controllerTransform != null)
        {
            // Follows one Mimic at a time: the first emitter still under the Hallucination.
            var emitter = _controllerTransform.Find(Hallucination.MimicEmitterName);
            if (emitter != null) _talkingAudioSource = emitter.GetComponent<AudioSource>();
        }

        if (_talkingAudioSource != null && _talkingAudioSource.isPlaying)
        {
            _sampleData ??= new float[SampleDataLength];
            _talkingAudioSource.GetOutputData(_sampleData, 0);

            var loudness = 0f;
            foreach (var sample in _sampleData)
            {
                loudness += Mathf.Abs(sample);
            }

            loudness /= SampleDataLength;
            if (loudness > 0.01f)
            {
                targetRotation = Mathf.Lerp(0f, -TalkRotationMaxAngle, loudness * 10f);
            }
        }
        else
        {
            _talkingAudioSource = null;
        }

        _headTopTransform.localRotation = Quaternion.Slerp(
            _headTopTransform.localRotation,
            Quaternion.Euler(targetRotation, 0f, 0f),
            Time.deltaTime * 20f);
    }

    private static class AngryExpression
    {
        public const float UpperLidAngleLeft = -34f;
        public const float UpperLidAngleRight = 34f;
        public const float UpperLidClosed = 83f;
        public const float LowerLidClosed = -83f;
    }
}
