using UnityEngine;
using VepMod.VepFramework;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

namespace VepMod.Enemies.Whispral;

/// <summary>
///     Contrôleur d'animation pour HallucinationDroid.
///     Gère les paupières fâchées, l'animation de parole et la détection du regard du joueur.
/// </summary>
public sealed class DroidFaceAnimationController : MonoBehaviour
{
    // Detection settings
    private const float LookAtAngle = 50f;
    private const float LookAtMaxDistance = 30f;
    private const float AngryEyesCooldown = 0.6f;

    // Talking animation
    private const float TalkRotationMaxAngle = 25f;
    private const int SampleDataLength = 256;
    private const float StalkStareBeforeFlee = DroidController.StalkStareBeforeFlee / 0.66f;

    private static readonly VepLogger LOG = VepLogger.Create<DroidFaceAnimationController>();

    // Angry eyes state
    private float _angryTimer;
    private Transform _controllerTransform;
    private DroidController _droidController;
    private GameObject? _eyelidsLeft;
    private GameObject? _eyelidsRight;

    // Talking animation
    private Transform? _headTopTransform;
    private bool _isAngry;
    private Transform? _leftLowerEyelidRotationX;
    private Transform? _leftUpperEyelidRotationX;
    private Transform? _leftUpperEyelidRotationZ;
    private Transform? _rightLowerEyelidRotationX;
    private Transform? _rightUpperEyelidRotationX;
    private Transform? _rightUpperEyelidRotationZ;
    private float[]? _sampleData;
    private AudioSource? _talkingAudioSource;
    private bool _wasPlayerLooking;

    /// <summary>
    ///     Indique si le joueur regarde actuellement le droid.
    /// </summary>
    public bool IsPlayerLookingAtMe { get; private set; }

    /// <summary>
    ///     Initialise le contrôleur d'animation.
    /// </summary>
    public void Initialize(DroidController droidController, Transform controllerTransform)
    {
        _droidController = droidController;
        _controllerTransform = droidController.ControllerTransform;
    }

    /// <summary>
    ///     Configure le système des paupières (appelé après setup).
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

    /// <summary>
    ///     Configure le système de talking animation.
    /// </summary>
    public void SetupTalking(Transform headTopTransform)
    {
        _headTopTransform = headTopTransform;
    }

    /// <summary>
    ///     Met à jour les animations (appelé dans LateUpdate).
    /// </summary>
    public void UpdateAnimations()
    {
        UpdatePlayerLookDetection();
        UpdateAngryEyes();
        UpdateTalkingAnimation();
    }

    private void UpdateAngryEyes()
    {
        if (_eyelidsLeft == null || _eyelidsRight == null) return;
        if (_leftUpperEyelidRotationX == null || _rightUpperEyelidRotationX == null) return;

        // Transition de "pas regardé" à "regardé"
        if (IsPlayerLookingAtMe && !_wasPlayerLooking)
        {
            _angryTimer = _droidController.IsStalking ? StalkStareBeforeFlee : AngryEyesCooldown;
        }

        _wasPlayerLooking = IsPlayerLookingAtMe;

        // Gestion du timer et de l'état angry
        if (IsPlayerLookingAtMe)
        {
            _angryTimer -= Time.deltaTime;
            if (_angryTimer <= 0f)
            {
                _isAngry = false;
            }
        }
        else
        {
            _isAngry = true;
        }

        // Activer les paupières quand on devient angry
        if (_isAngry && !_eyelidsLeft.activeSelf)
        {
            _eyelidsLeft.SetActive(true);
            _eyelidsRight.SetActive(true);
        }

        // Valeurs cibles
        var targetUpperClosed = _isAngry ? AngryExpression.UpperLidClosed : 0f;
        var targetLowerClosed = _isAngry ? AngryExpression.LowerLidClosed : 0f;
        var targetLeftAngle = _isAngry ? AngryExpression.UpperLidAngleLeft : 0f;
        var targetRightAngle = _isAngry ? AngryExpression.UpperLidAngleRight : 0f;

        var lerpSpeed = Time.deltaTime * 8f;

        // Animer les paupières supérieures
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

        // Animer les paupières inférieures
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

        // Désactiver les paupières quand l'animation est terminée
        if (!_isAngry && _eyelidsLeft.activeSelf)
        {
            var currentAngle = Mathf.Abs(_leftUpperEyelidRotationX.localEulerAngles.x);
            if (currentAngle is < 10f or > 350f)
            {
                _eyelidsLeft.SetActive(false);
                _eyelidsRight.SetActive(false);
            }
        }
    }

    private void UpdatePlayerLookDetection()
    {
        var camera = Camera.main;
        if (camera == null || _controllerTransform == null)
        {
            IsPlayerLookingAtMe = false;
            return;
        }

        var droidPosition = _controllerTransform.position + Vector3.up;
        var cameraPosition = camera.transform.position;

        var distance = Vector3.Distance(cameraPosition, droidPosition);
        if (distance > LookAtMaxDistance)
        {
            IsPlayerLookingAtMe = false;
            return;
        }

        var directionToDroid = (droidPosition - cameraPosition).normalized;
        var dot = Vector3.Dot(camera.transform.forward, directionToDroid);
        var threshold = Mathf.Cos(LookAtAngle * Mathf.Deg2Rad);

        IsPlayerLookingAtMe = dot >= threshold;
    }

    private void UpdateTalkingAnimation()
    {
        if (_headTopTransform == null) return;
        var targetRotation = 0f;
        if (_talkingAudioSource == null && _controllerTransform != null)
        {
            _talkingAudioSource = _controllerTransform.GetComponent<AudioSource>();
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