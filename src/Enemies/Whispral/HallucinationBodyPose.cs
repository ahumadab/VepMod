using UnityEngine;
using VepMod.Enemies.Whispral.Visuals;
using VepMod.VepFramework;

#pragma warning disable CS8618

namespace VepMod.Enemies.Whispral;

internal enum Gait
{
    Still,
    Walk,
    Sprint
}

/// <summary>
///     Poses the PlayerAvatar clone mounted on a Hallucination from what its states say it is doing, the way the game
///     scripts stripped from the clone pose a player: the animator parameters of PlayerAvatarVisuals.AnimationLogic, the
///     arm poses of PlayerAvatarRightArm and PlayerAvatarLeftArm, the eyes of PlayerEyes, and the map tool.
/// </summary>
internal sealed class HallucinationBodyPose : MonoBehaviour
{
    private const float TurningAngle = 7f; // °

    // From PlayerEyes.Update in the game.
    private const float EyeClampCone = 50f; // °
    private const float EyeClampYaw = 30f; // °
    private const float EyeReturnSpeed = 8f;

    private static readonly VepLogger LOG = VepLogger.Create<HallucinationBodyPose>();

    private static readonly int MovingKey = Animator.StringToHash("Moving");
    private static readonly int SprintingKey = Animator.StringToHash("Sprinting");
    private static readonly int TurningKey = Animator.StringToHash("Turning");
    private static readonly int GrabbingKey = Animator.StringToHash("Grabbing");
    private static readonly int CrouchingKey = Animator.StringToHash("Crouching");
    private static readonly int CrawlingKey = Animator.StringToHash("Crawling");
    private static readonly int TumblingKey = Animator.StringToHash("Tumbling");
    private static readonly int TumblingMoveKey = Animator.StringToHash("TumblingMove");
    private static readonly int SlidingKey = Animator.StringToHash("Sliding");
    private static readonly int JumpingKey = Animator.StringToHash("Jumping");
    private static readonly int FallingKey = Animator.StringToHash("Falling");
    private static readonly int StunKey = Animator.StringToHash("stun");
    private static readonly int SprintingImpulseKey = Animator.StringToHash("SprintingImpulse");

    private Animator? _animator;
    private Transform _controller;
    private HallucinationMovement _movement;
    private GameObject? _mapTool;
    private Transform? _mapLookTarget;

    private ArmPose? _rightArm;
    private Vector3 _basePose;
    private Vector3 _mapPose;
    private ArmPose? _leftArm;
    private Vector3 _flashlightPose;

    private Eye? _eyeLeft;
    private Eye? _eyeRight;

    private Situation _situation = Situation.Moving;
    private Gait _gait = Gait.Still;
    private bool _wasSprinting;

    // PROTOTYPE for #65: what the fake PlayerAvatar is fed.
    internal Gait PrototypeGait => _gait;
    internal bool PrototypeCheckingMap => _situation == Situation.CheckingMap;
    internal bool PrototypeStaring => _situation == Situation.Staring;

    public string DebugEyes => _eyeLeft == null || _eyeRight == null
        ? "no eyes"
        : $"{_situation}: left {_eyeLeft}, right {_eyeRight}";

    private void Update()
    {
        var checkingMap = _situation == Situation.CheckingMap;
        _rightArm?.Update(checkingMap ? _mapPose : _basePose);
        _leftArm?.Update(_flashlightPose);
        if (_animator != null) Animate(_animator, checkingMap);
    }

    // After the animator, which would otherwise overwrite the eye bones.
    private void LateUpdate()
    {
        if (_eyeLeft == null || _eyeRight == null) return;

        if (LookTarget() is { } target)
        {
            _eyeLeft.LookAt(target);
            _eyeRight.LookAt(target);
        }
        else
        {
            _eyeLeft.EaseToRest();
            _eyeRight.EaseToRest();
        }
    }

    /// <summary>
    ///     Called once the rig is cloned; until then the body pose only records the situation.
    /// </summary>
    /// <param name="source">The Imitated Player's visuals, which hold the arm and eye scripts the pose is read from.</param>
    public void Initialize(Animator animator, HallucinationMovement movement, Transform controller,
        PlayerAvatarVisuals? source, Transform cloneRig, GameObject? mapTool)
    {
        _animator = animator;
        _movement = movement;
        _controller = controller;
        _mapTool = mapTool;
        _mapLookTarget = mapTool != null
            ? HallucinationVisualsClone.FindChildByName(mapTool.transform, "Player Look Target")
            : null;

        // Simplified from PlayerAvatarVisuals.cs:853-866: the Hallucination has no sprint stat bonus.
        animator.speed = 1f;

        if (source == null)
        {
            LOG.Warning($"{name}: the Imitated Player has no visuals, its arms and eyes stay as cloned");
            return;
        }

        var rightArm = source.GetComponent<PlayerAvatarRightArm>();
        if (FindClonedBone(cloneRig, rightArm != null ? rightArm.rightArmTransform : null, "right arm") is { } rightBone)
        {
            _rightArm = new ArmPose(rightBone, rightArm!.basePose, rightArm.poseCurve, rightArm.poseSpeed);
            _basePose = rightArm.basePose;
            _mapPose = rightArm.mapPose;
        }

        // The game raises the left arm while the flashlight is on and the player is not crawling: a Hallucination's
        // flashlight is always on and it never crawls. headRotation (the camera pitch) is ignored on both arms.
        var leftArm = source.GetComponent<PlayerAvatarLeftArm>();
        if (FindClonedBone(cloneRig, leftArm != null ? leftArm.leftArmTransform : null, "left arm") is { } leftBone)
        {
            _leftArm = new ArmPose(leftBone, leftArm!.basePose, leftArm.poseCurve, leftArm.poseSpeed);
            _flashlightPose = leftArm.flashlightPose;
        }

        var eyes = source.GetComponent<PlayerEyes>();
        var eyeLeft = FindClonedBone(cloneRig, eyes != null ? eyes.eyeLeft : null, "left eye");
        var eyeRight = FindClonedBone(cloneRig, eyes != null ? eyes.eyeRight : null, "right eye");
        var idleTarget = FindClonedBone(cloneRig, eyes != null ? eyes.targetIdle : null, "eye idle target");
        if (eyeLeft != null && eyeRight != null && idleTarget != null)
        {
            _eyeLeft = new Eye(eyeLeft, idleTarget);
            _eyeRight = new Eye(eyeRight, idleTarget);
        }
    }

    public void Move(Gait gait)
    {
        Set(Situation.Moving, gait);
    }

    /// <summary>
    ///     Raises the map in the right hand, shows it and turns the eyes to it, until the next situation.
    /// </summary>
    public void CheckMap()
    {
        Set(Situation.CheckingMap, Gait.Still);
    }

    /// <summary>
    ///     Turns the eyes to the Victim's camera, until the next situation.
    /// </summary>
    public void Stare(Gait gait)
    {
        Set(Situation.Staring, gait);
    }

    private void Set(Situation situation, Gait gait)
    {
        if (situation == _situation && gait == _gait) return;

        _situation = situation;
        _gait = gait;
        if (_mapTool != null) _mapTool.SetActive(situation == Situation.CheckingMap);
        LOG.Debug($"Body pose of {name}: {situation}, {gait}");
    }

    private void Animate(Animator animator, bool checkingMap)
    {
        var sprinting = _gait == Gait.Sprint;
        var turning = (_gait != Gait.Still || _situation == Situation.Staring) &&
                      Quaternion.Angle(_controller.rotation, _movement.TargetRotation) > TurningAngle;

        animator.SetBool(MovingKey, _gait != Gait.Still);
        animator.SetBool(SprintingKey, sprinting);
        animator.SetBool(TurningKey, turning);

        // 'Grabbing' raises the hand to the grabber pose. Every other player bool the Hallucination never uses stays
        // false, or the animator transitions would not fall back to locomotion.
        animator.SetBool(GrabbingKey, checkingMap);
        animator.SetBool(CrouchingKey, false);
        animator.SetBool(CrawlingKey, false);
        animator.SetBool(TumblingKey, false);
        animator.SetBool(TumblingMoveKey, false);
        animator.SetBool(SlidingKey, false);
        animator.SetBool(JumpingKey, false);
        animator.SetBool(FallingKey, _movement.IsFalling);
        animator.SetBool(StunKey, false);

        // Fired on the rising edge, like PlayerAvatarVisuals.cs:767, for the sprint snap.
        if (sprinting && !_wasSprinting) animator.SetTrigger(SprintingImpulseKey);
        _wasSprinting = sprinting;
    }

    private Vector3? LookTarget()
    {
        switch (_situation)
        {
            case Situation.CheckingMap when _mapLookTarget != null:
                return _mapLookTarget.position;
            case Situation.Staring:
                var camera = Camera.main;
                return camera != null ? camera.transform.position : null;
            default:
                return null;
        }
    }

    private Transform? FindClonedBone(Transform cloneRig, Transform? sourceBone, string part)
    {
        var bone = sourceBone != null ? HallucinationVisualsClone.FindChildByName(cloneRig, sourceBone.name) : null;
        if (bone == null) LOG.Warning($"{name}: no {part} bone on the cloned rig, it stays as cloned");
        return bone;
    }

    private enum Situation
    {
        Moving,
        CheckingMap,
        Staring
    }

    /// <summary>
    ///     The pose lerp of PlayerAvatarRightArm and PlayerAvatarLeftArm: a bone eases to a new pose along the arm's
    ///     curve.
    /// </summary>
    private sealed class ArmPose
    {
        private readonly Transform _bone;
        private readonly AnimationCurve? _curve;
        private readonly float _speed;
        private Vector3 _current;
        private Vector3 _from;
        private float _lerp = 1f;
        private Vector3 _to;

        /// <param name="start">The pose the bone holds before its first target, eased from on spawn.</param>
        public ArmPose(Transform bone, Vector3 start, AnimationCurve? curve, float speed)
        {
            _bone = bone;
            _curve = curve;
            _speed = speed;
            _current = _from = _to = start;
        }

        public void Update(Vector3 target)
        {
            if (target != _to)
            {
                _from = _current;
                _to = target;
                _lerp = 0f;
            }

            if (_lerp < 1f)
            {
                _lerp += _speed * Time.deltaTime;
                var t = _curve != null ? _curve.Evaluate(_lerp) : Mathf.Clamp01(_lerp);
                _current = Vector3.LerpUnclamped(_from, _to, t);
            }
            else
            {
                _current = _to;
            }

            _bone.localEulerAngles = _current;
        }
    }

    private sealed class Eye
    {
        private readonly Transform _bone;
        private readonly Transform _idleTarget;
        private readonly Quaternion _rest;

        /// <param name="idleTarget">The clone of PlayerEyes.targetIdle, whose forward the look-at cone is centred on.</param>
        public Eye(Transform bone, Transform idleTarget)
        {
            _bone = bone;
            _idleTarget = idleTarget;
            _rest = bone.localRotation;
        }

        // PlayerEyes.EyeLookAt applied directly instead of through a spring.
        public void LookAt(Vector3 target)
        {
            var direction = target - _bone.position;
            if (direction.sqrMagnitude < 0.0001f) return;

            _bone.rotation =
                Quaternion.LookRotation(SemiFunc.ClampDirection(direction, _idleTarget.forward, EyeClampCone));

            // Unity reports euler angles in [0, 360]; the clamp is symmetric around 0.
            var euler = _bone.localEulerAngles;
            if (euler.y > EyeClampYaw && euler.y < 180f) euler.y = EyeClampYaw;
            else if (euler.y < 360f - EyeClampYaw && euler.y > 180f) euler.y = 360f - EyeClampYaw;
            _bone.localEulerAngles = euler;
        }

        public void EaseToRest()
        {
            _bone.localRotation = Quaternion.Slerp(_bone.localRotation, _rest, EyeReturnSpeed * Time.deltaTime);
        }

        public override string ToString()
        {
            var euler = _bone.localEulerAngles;
            return $"(x {Mathf.DeltaAngle(0f, euler.x):F0}°, y {Mathf.DeltaAngle(0f, euler.y):F0}°) " +
                   $"{Quaternion.Angle(_bone.localRotation, _rest):F0}° from rest";
        }
    }
}
