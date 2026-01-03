using UnityEngine;
using VepMod.VepFramework;
using VepMod.VepFramework.Structures.FSM;
using Random = UnityEngine.Random;

namespace VepMod.Enemies.Whispral;

/// <summary>
///     États FSM pour HallucinationDroid (partial class).
/// </summary>
public sealed partial class HallucinationDroid
{
    private static readonly VepLogger LOG = VepLogger.Create<HallucinationDroid>(true);

    private class IdleState : StateMachineBase<StateMachine, StateId>.StateBaseTimed
    {
        private const string ClipName = "LostDroidStand";
        private const float IdleLoopStart = 0f;
        private const float IdleLoopEnd = 4.5f;
        private const float BlendDelay = 0.3f;

        private const float MinIdleTime = 2f;
        private const float MaxIdleTime = 4f;
        private const float PrecomputeRetryInterval = 1f;
        private const float CheckMapChance = 0.25f;

        private Animator _animator;
        private bool _animatorTakenOver;
        private AnimationClip _clip;
        private float _currentTime;

        private float _duration;
        private float _precomputeTimer;
        private GameObject _targetObject;

        public override void OnStateEnter(StateId previous)
        {
            base.OnStateEnter(previous);
            _duration = Random.Range(MinIdleTime, MaxIdleTime);
            _precomputeTimer = 0f;
            _animatorTakenOver = false;
            Machine.Owner.ResetPath();
            Machine.Owner.HasChangedMovementState = false;

            _animator = Machine.Owner.Animator;
            if (_animator != null)
            {
                _clip = DroidHelpers.GetAnimationClip(_animator, ClipName);
                if (_clip != null)
                {
                    _targetObject = _animator.gameObject;
                }
            }

            Machine.Owner.StartPrecomputeDestination();
        }

        public override void OnStateUpdate()
        {
            base.OnStateUpdate();

            if (!_animatorTakenOver && _clip != null && _targetObject != null)
            {
                if (TimeElapsed >= BlendDelay)
                {
                    _animator.enabled = false;
                    _currentTime = IdleLoopStart;
                    _animatorTakenOver = true;
                }
            }

            if (_animatorTakenOver && _clip != null && _targetObject != null)
            {
                _currentTime += Time.deltaTime;
                if (_currentTime >= IdleLoopEnd)
                {
                    _currentTime = IdleLoopStart;
                }

                _clip.SampleAnimation(_targetObject, _currentTime);
            }

            if (!Machine.Owner.HasPrecomputedDestination)
            {
                _precomputeTimer -= Time.deltaTime;
                if (_precomputeTimer <= 0f)
                {
                    Machine.Owner.StartPrecomputeDestination();
                    _precomputeTimer = PrecomputeRetryInterval;
                }
            }

            if (TimeElapsed >= _duration)
            {
                var distanceToPlayer = Machine.Owner.GetDistanceToPlayer();
                if (distanceToPlayer > StalkMinDistance && Random.value < StalkChance)
                {
                    Machine.NextStateStateId = StateId.StalkApproach;
                    return;
                }

                var roll = Random.value;
                if (roll < CheckMapChance)
                {
                    Machine.NextStateStateId = StateId.CheckMap;
                }
                else
                {
                    Machine.NextStateStateId = Random.value < SprintChance ? StateId.Sprint : StateId.Wander;
                }
            }
        }

        public override void OnStateExit(StateId next)
        {
            base.OnStateExit(next);

            if (_animator != null && next != StateId.CheckMap)
            {
                _animator.enabled = true;
            }
        }
    }

    private abstract class MovementState : StateMachineBase<StateMachine, StateId>.StateBaseTimed
    {
        private const float SwitchCheckInterval = 1f;
        private const float SwitchChance = 0.2f;

        private float _duration;
        private float _nextSwitchCheck;

        protected abstract float MinDuration { get; }
        protected abstract float MaxDuration { get; }
        protected abstract float Speed { get; }
        protected abstract StateId SwitchTargetState { get; }
        protected abstract void SetMovementFlag(bool value);

        public override void OnStateEnter(StateId previous)
        {
            base.OnStateEnter(previous);
            _duration = Random.Range(MinDuration, MaxDuration);
            _nextSwitchCheck = SwitchCheckInterval;

            var isMovementSwitch = previous is StateId.Wander or StateId.Sprint;
            if (!isMovementSwitch)
            {
                if (!Machine.Owner.TrySetRandomDestination())
                {
                    Machine.NextStateStateId = StateId.Idle;
                    return;
                }
            }

            Machine.Owner.SetSpeed(Speed);
            SetMovementFlag(true);
        }

        public override void OnStateExit(StateId next)
        {
            base.OnStateExit(next);
            SetMovementFlag(false);
        }

        public override void OnStateUpdate()
        {
            base.OnStateUpdate();

            if (Machine.Owner.HasReachedDestination() || TimeElapsed >= _duration)
            {
                Machine.NextStateStateId = StateId.Idle;
                return;
            }

            if (!Machine.Owner.HasChangedMovementState && TimeElapsed >= _nextSwitchCheck)
            {
                _nextSwitchCheck += SwitchCheckInterval;
                if (Random.value < SwitchChance)
                {
                    Machine.Owner.HasChangedMovementState = true;
                    Machine.NextStateStateId = SwitchTargetState;
                }
            }
        }
    }

    private class WanderState : MovementState
    {
        protected override float MinDuration => 5f;
        protected override float MaxDuration => 15f;
        protected override float Speed => WalkSpeed;
        protected override StateId SwitchTargetState => StateId.Sprint;

        protected override void SetMovementFlag(bool value)
        {
            Machine.Owner.IsWalking = value;
        }
    }

    private class SprintState : MovementState
    {
        protected override float MinDuration => 3f;
        protected override float MaxDuration => 8f;
        protected override float Speed => SprintSpeed;
        protected override StateId SwitchTargetState => StateId.Wander;

        protected override void SetMovementFlag(bool value)
        {
            Machine.Owner.IsSprinting = value;
        }
    }

    private class CheckMapState : StateMachineBase<StateMachine, StateId>.StateBaseTimed
    {
        private const string ClipName = "LostDroidStand";

        private const float RaiseStartTime = 4.65f;
        private const float LookStartTime = 5.5f;
        private const float LookEndTime = 6.9333334f;
        private const float LowerEndTime = 7.15f;

        private const float MinLookDuration = 0.5f;
        private const float MaxLookDuration = 3f;

        private Animator _animator;
        private AnimationClip _clip;

        private Phase _currentPhase;
        private float _currentTime;
        private float _lookDuration;
        private float _lookTimer;
        private GameObject _targetObject;

        public override void OnStateEnter(StateId previous)
        {
            base.OnStateEnter(previous);

            _animator = Machine.Owner.Animator;
            if (_animator == null)
            {
                LOG.Warning("CheckMapState: Animator is null!");
                Machine.NextStateStateId = StateId.Idle;
                return;
            }

            _clip = DroidHelpers.GetAnimationClip(_animator, ClipName);
            if (_clip == null)
            {
                LOG.Warning($"CheckMapState: Clip '{ClipName}' not found!");
                Machine.NextStateStateId = StateId.Idle;
                return;
            }

            _targetObject = _animator.gameObject;

            Machine.Owner.IsWalking = false;
            Machine.Owner.IsSprinting = false;
            Machine.Owner.ResetPath();

            _animator.enabled = false;

            _lookDuration = Random.Range(MinLookDuration, MaxLookDuration);
            _lookTimer = 0f;

            _currentPhase = Phase.Raise;
            _currentTime = RaiseStartTime;

            LOG.Debug($"CheckMapState: ENTER - Clip length={_clip.length}s, starting at {_currentTime}s");
            LOG.Debug(
                $"CheckMapState: Thresholds - Raise={RaiseStartTime}s, Look={LookStartTime}s-{LookEndTime}s, Lower={LowerEndTime}s");
            LOG.Debug($"CheckMapState: LookDuration={_lookDuration:F1}s");

            _clip.SampleAnimation(_targetObject, _currentTime);
        }

        public override void OnStateUpdate()
        {
            base.OnStateUpdate();

            if (_clip == null || _targetObject == null)
            {
                Machine.NextStateStateId = StateId.Idle;
                return;
            }

            _currentTime += Time.deltaTime;

            switch (_currentPhase)
            {
                case Phase.Raise:
                    if (_currentTime >= LookStartTime)
                    {
                        LOG.Debug("CheckMapState: Transitioning to Phase.Look");
                        _currentPhase = Phase.Look;
                        _lookTimer = 0f;
                    }

                    break;

                case Phase.Look:
                    _lookTimer += Time.deltaTime;

                    if (_currentTime >= LookEndTime)
                    {
                        if (_lookTimer < _lookDuration)
                        {
                            _currentTime = LookStartTime;
                        }
                        else
                        {
                            LOG.Debug("CheckMapState: Transitioning to Phase.Lower");
                            _currentPhase = Phase.Lower;
                        }
                    }

                    break;

                case Phase.Lower:
                    if (_currentTime >= LowerEndTime)
                    {
                        LOG.Debug("CheckMapState: Animation complete, returning to Idle");
                        Machine.NextStateStateId = StateId.Idle;
                        return;
                    }

                    break;
            }

            _clip.SampleAnimation(_targetObject, _currentTime);
        }

        public override void OnStateExit(StateId next)
        {
            base.OnStateExit(next);
            LOG.Debug($"CheckMapState: EXIT -> {next}");

            if (_animator != null)
            {
                _animator.enabled = true;
            }
        }

        private enum Phase
        {
            Raise,
            Look,
            Lower
        }
    }

    /// <summary>
    ///     État de stalk : le droid court vers le joueur.
    /// </summary>
    private class StalkApproachState : StateMachineBase<StateMachine, StateId>.StateBaseTimed
    {
        private const float MaxApproachTime = 30f;
        private const float DestinationUpdateInterval = 1f;
        private float _nextDestinationUpdate;

        public override void OnStateEnter(StateId previous)
        {
            base.OnStateEnter(previous);
            LOG.Debug("=== STALK: Approach started - running towards player ===");

            Machine.Owner.IsSprinting = true;
            Machine.Owner.SetSpeed(SprintSpeed);
            _nextDestinationUpdate = 0f;

            if (!Machine.Owner.TrySetDestinationToPlayer())
            {
                LOG.Debug("STALK: Failed to set destination to player, aborting");
                Machine.NextStateStateId = StateId.Idle;
            }
        }

        public override void OnStateUpdate()
        {
            base.OnStateUpdate();

            var distance = Machine.Owner.GetDistanceToPlayer();
            var reachedNavDest = Machine.Owner.HasReachedDestination();

            if (TimeElapsed >= MaxApproachTime)
            {
                LOG.Debug($"STALK: Approach timeout, dist={distance:F1}m, aborting");
                Machine.NextStateStateId = StateId.Idle;
                return;
            }

            if (distance <= StalkArrivalDistance)
            {
                LOG.Debug($"STALK: Arrived near player (distance={distance:F1}m)");
                Machine.NextStateStateId = StateId.StalkStare;
                return;
            }

            if (reachedNavDest)
            {
                LOG.Debug($"STALK: Reached nav destination but player still at {distance:F1}m, starting stare anyway");
                Machine.NextStateStateId = StateId.StalkStare;
                return;
            }

            _nextDestinationUpdate -= Time.deltaTime;
            if (_nextDestinationUpdate <= 0f)
            {
                Machine.Owner.TrySetDestinationToPlayer();
                _nextDestinationUpdate = DestinationUpdateInterval;
            }
        }

        public override void OnStateExit(StateId next)
        {
            base.OnStateExit(next);
            Machine.Owner.IsSprinting = false;
            Machine.Owner.ResetPath();
        }
    }

    /// <summary>
    ///     État de stalk : le droid fixe le joueur tout en s'approchant.
    ///     Si le joueur le regarde, il maintient le regard X secondes puis fuit.
    /// </summary>
    private class StalkStareState : StateMachineBase<StateMachine, StateId>.StateBaseTimed
    {
        private const float MaxStareTime = 30f;
        private const float DestinationUpdateInterval = 0.5f;

        private float _fleeCountdown;
        private bool _hasBeenSeen;
        private float _nextDestinationUpdate;

        public override void OnStateEnter(StateId previous)
        {
            base.OnStateEnter(previous);
            LOG.Debug("=== STALK: Staring at player ===");

            _hasBeenSeen = false;
            _fleeCountdown = StalkStareBeforeFlee;
            _nextDestinationUpdate = 0f;

            Machine.Owner.IsWalking = true;
            Machine.Owner.IsSprinting = false;
            Machine.Owner.SetSpeed(WalkSpeed);
        }

        public override void OnStateUpdate()
        {
            base.OnStateUpdate();

            Machine.Owner.LookAtPlayer();

            var distance = Machine.Owner.GetDistanceToPlayer();
            var isPlayerLooking = Machine.Owner.IsPlayerLookingAtMe;

            if (isPlayerLooking && !_hasBeenSeen)
            {
                _hasBeenSeen = true;
                LOG.Debug("=== STALK: Player sees me! Maintaining eye contact... ===");

                Machine.Owner.IsWalking = false;
                Machine.Owner.ResetPath();
            }

            if (_hasBeenSeen)
            {
                _fleeCountdown -= Time.deltaTime;
                if (_fleeCountdown <= 0f)
                {
                    LOG.Debug("=== STALK: Eye contact over, fleeing! ===");
                    Machine.NextStateStateId = StateId.StalkFlee;
                    return;
                }
            }
            else
            {
                if (distance > StalkMinKeepDistance)
                {
                    _nextDestinationUpdate -= Time.deltaTime;
                    if (_nextDestinationUpdate <= 0f)
                    {
                        Machine.Owner.TrySetDestinationToPlayer();
                        _nextDestinationUpdate = DestinationUpdateInterval;
                    }
                }
                else
                {
                    Machine.Owner.ResetPath();
                }
            }

            if (TimeElapsed >= MaxStareTime)
            {
                LOG.Debug("STALK: Stare timeout, fleeing");
                Machine.NextStateStateId = StateId.StalkFlee;
            }
        }

        public override void OnStateExit(StateId next)
        {
            base.OnStateExit(next);
            Machine.Owner.IsWalking = false;
        }
    }

    /// <summary>
    ///     État de stalk : le droid fuit le joueur.
    /// </summary>
    private class StalkFleeState : StateMachineBase<StateMachine, StateId>.StateBaseTimed
    {
        private const float MaxFleeTime = 10f;

        public override void OnStateEnter(StateId previous)
        {
            base.OnStateEnter(previous);
            LOG.Debug("=== STALK: Fleeing from player! ===");

            Machine.Owner.IsSprinting = true;
            Machine.Owner.SetSpeed(SprintSpeed);

            if (!Machine.Owner.TrySetFleeDestination())
            {
                LOG.Debug("STALK: Failed to set flee destination, going to idle");
                Machine.NextStateStateId = StateId.Idle;
            }
        }

        public override void OnStateUpdate()
        {
            base.OnStateUpdate();

            if (Machine.Owner.HasReachedDestination() || TimeElapsed >= MaxFleeTime)
            {
                LOG.Debug("=== STALK: Flee complete, returning to idle ===");
                Machine.NextStateStateId = StateId.Idle;
            }
        }

        public override void OnStateExit(StateId next)
        {
            base.OnStateExit(next);
            Machine.Owner.IsSprinting = false;
        }
    }
}
