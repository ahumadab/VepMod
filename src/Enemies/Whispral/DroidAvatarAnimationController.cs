using UnityEngine;

#pragma warning disable CS8618

namespace VepMod.Enemies.Whispral;

/// <summary>
///     Drives, every frame, the Animator parameters of the PlayerAvatar clone mounted on the Hallucination, the way
///     PlayerAvatarVisuals.AnimationLogic() does for a player.
/// </summary>
public sealed class DroidAvatarAnimationController : MonoBehaviour
{
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

    private DroidController _droid;
    private Animator _animator;
    private bool _wasSprinting;

    public float AnimationSpeedMultiplier { get; set; } = 1f;

    public void Initialize(DroidController droid, Animator animator)
    {
        _droid = droid;
        _animator = animator;
    }

    private void Update()
    {
        if (_animator == null || _droid == null) return;

        var isMovementState = _droid.IsInMovementState;

        var turning = false;
        if (isMovementState && _droid.ControllerTransform != null && _droid.Movement != null)
        {
            var angle = Quaternion.Angle(_droid.ControllerTransform.rotation, _droid.Movement.TargetRotation);
            turning = angle > 7f;
        }

        var sprinting = _droid.IsSprinting;
        var moving = _droid.IsWalking || sprinting;

        _animator.SetBool(MovingKey, moving);
        _animator.SetBool(SprintingKey, sprinting);
        _animator.SetBool(TurningKey, turning);

        // 'Grabbing' raises the hand during CheckMap (animator transition to grabberPose). Every other player bool the
        // Hallucination never uses stays false, or the animator transitions would not fall back to locomotion.
        _animator.SetBool(GrabbingKey, _droid.WantsGrabbing);
        _animator.SetBool(CrouchingKey, false);
        _animator.SetBool(CrawlingKey, false);
        _animator.SetBool(TumblingKey, false);
        _animator.SetBool(TumblingMoveKey, false);
        _animator.SetBool(SlidingKey, false);
        _animator.SetBool(JumpingKey, false);
        _animator.SetBool(FallingKey, _droid.IsFalling);
        _animator.SetBool(StunKey, false);

        // Fired on the rising edge, like PlayerAvatarVisuals.cs:767, for the sprint snap.
        if (sprinting && !_wasSprinting)
        {
            _animator.SetTrigger(SprintingImpulseKey);
        }
        _wasSprinting = sprinting;

        // Simplified from PlayerAvatarVisuals.cs:853-866: the Hallucination has no sprint stat bonus.
        _animator.speed = AnimationSpeedMultiplier;
    }
}
