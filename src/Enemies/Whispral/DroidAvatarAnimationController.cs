using UnityEngine;
using VepMod.VepFramework;

#pragma warning disable CS8618

namespace VepMod.Enemies.Whispral;

/// <summary>
///     Pilote chaque frame les paramètres de l'Animator du clone du PlayerAvatar
///     monté sur le LostDroid. Force à false tous les bools "joueur" que l'AI
///     n'utilise pas (Crouching, Crawling, Jumping, Falling, Tumbling, Sliding,
///     Grabbing, stun) pour débloquer les transitions du state machine, set les
///     bools actifs depuis le FSM (Moving/Sprinting/Turning), tire l'impulse de
///     sprint sur front montant, et module animator.speed comme
///     PlayerAvatarVisuals.AnimationLogic().
/// </summary>
public sealed class DroidAvatarAnimationController : MonoBehaviour
{
    private static readonly VepLogger LOG = VepLogger.Create<DroidAvatarAnimationController>();

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

        // Turning: calculé depuis l'angle entre la rotation actuelle et la cible.
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

        // États "joueur" jamais utilisés par l'AI : forcés à false chaque frame
        // pour que toutes les transitions retombent proprement sur la branche locomotion.
        _animator.SetBool(GrabbingKey, false);
        _animator.SetBool(CrouchingKey, false);
        _animator.SetBool(CrawlingKey, false);
        _animator.SetBool(TumblingKey, false);
        _animator.SetBool(TumblingMoveKey, false);
        _animator.SetBool(SlidingKey, false);
        _animator.SetBool(JumpingKey, false);
        _animator.SetBool(FallingKey, false);
        _animator.SetBool(StunKey, false);

        // Trigger d'impulse sur front montant : déclenche le snap de sprint
        // comme PlayerAvatarVisuals.cs:767.
        if (sprinting && !_wasSprinting)
        {
            _animator.SetTrigger(SprintingImpulseKey);
        }
        _wasSprinting = sprinting;

        // Modulation de animator.speed, simplifiée par rapport à
        // PlayerAvatarVisuals.cs:853-866 (pas de bonus stat sprint pour l'AI).
        _animator.speed = AnimationSpeedMultiplier;
    }
}
