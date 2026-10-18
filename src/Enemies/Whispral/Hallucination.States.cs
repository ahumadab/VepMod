using UnityEngine;
using VepMod.VepFramework.Structures.FSM;
using Random = UnityEngine.Random;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

namespace VepMod.Enemies.Whispral;

public sealed partial class Hallucination
{
    private sealed class IdleState : StateMachineBase<StateMachine, StateId>.StateBaseTimed
    {
        private const float PrecomputeRetryInterval = 1f;
        private const float CheckMapChance = 0.25f;

        private float _facing;
        private int _glances;
        private float _precomputeTimer;

        private Wait Wait => Machine.Owner._wait;

        public override void OnStateEnter(StateId previous)
        {
            base.OnStateEnter(previous);
            _precomputeTimer = 0f;
            _glances = 0;
            Machine.Owner.Movement.Stop();
            Machine.Owner.BodyPose.Move(Gait.Still);
            Machine.Owner.Movement.StartPrecomputeDestination();

            _facing = Machine.Owner.Movement.TargetRotation.eulerAngles.y;
            var turnsRound = Wait.Start();
            LOG.Debug($"IdleState: {Machine.Owner.name} waits {Wait.Duration:F1}s" +
                      (turnsRound ? ", turning round first" : ""));
            if (!turnsRound) return;

            _facing += 180f;
            Machine.Owner.Movement.Face(_facing);
            Machine.Owner.BodyPose.Glance(_facing);
        }

        public override void OnStateExit(StateId next)
        {
            base.OnStateExit(next);
            Machine.Owner.BodyPose.Glance(null);
            LOG.Debug($"IdleState: {Machine.Owner.name} glanced {_glances} times in its wait of {Wait.Duration:F1}s");
        }

        public override void OnStateUpdate()
        {
            base.OnStateUpdate();

            if (Wait.Update(Time.deltaTime) is { } glance)
            {
                _glances++;
                Machine.Owner.Movement.Face(_facing + glance.Body);
                Machine.Owner.BodyPose.Glance(_facing + glance.Head);
                LOG.Debug($"IdleState: {Machine.Owner.name} glances {glance.Head:F0}° from its wait's facing, " +
                          $"{glance.OffBody:F0}° off the body, " +
                          (glance.BodyFollows ? $"the body following to {glance.Body:F0}°" : "the head alone"));
            }

            if (!Machine.Owner.Movement.HasPrecomputedDestination)
            {
                _precomputeTimer -= Time.deltaTime;
                if (_precomputeTimer <= 0f)
                {
                    Machine.Owner.Movement.StartPrecomputeDestination();
                    _precomputeTimer = PrecomputeRetryInterval;
                }
            }

            if (Wait.IsOver)
            {
                var distanceToPlayer = Machine.Owner.Movement.GetDistanceToPlayer();
                var forced = Machine.Owner._stalkingRollForced;
                if (distanceToPlayer > StalkMinDistance && (forced || Random.value < StalkChance))
                {
                    Machine.Owner._stalkingRollForced = false;
                    var how = forced ? "forced" : "rolled";
                    if (Machine.Owner.OtherStalker() is { } stalker)
                    {
                        LOG.Debug($"IdleState: {Machine.Owner.name} refused the Stalking it {how}: " +
                                  $"{stalker.name} is Stalking");
                    }
                    else
                    {
                        Machine.NextStateStateId = StateId.StalkApproach;
                        LOG.Debug($"IdleState: {Machine.Owner.name} -> StalkApproach, {how} " +
                                  $"(dist={distanceToPlayer:F1})");
                        return;
                    }
                }

                var roll = Random.value;
                if (roll < CheckMapChance)
                {
                    Machine.NextStateStateId = StateId.CheckMap;
                    LOG.Debug($"IdleState: {Machine.Owner.name} -> CheckMap (roll={roll:F2} < {CheckMapChance})");
                }
                else
                {
                    Machine.NextStateStateId = Random.value < SprintChance ? StateId.Sprint : StateId.Wander;
                    LOG.Debug($"IdleState: {Machine.Owner.name} -> {Machine.NextStateStateId}");
                }
            }
        }
    }

    private abstract class MovementState : StateMachineBase<StateMachine, StateId>.StateBaseTimed
    {
        private const float SwitchCheckInterval = 1f;
        private const float SwitchChance = 0.2f;
        private const float PrecomputeForwardInterval = 0.5f;

        private float _duration;
        private float _nextPrecomputeForward;
        private float _nextSwitchCheck;

        // Walking and sprinting switch to each other at most once between two Idles.
        private bool _enteredFromSwitch;

        private MindChange _mindChange;
        private float _glanceEnd;

        private TripChain TripChain => Machine.Owner._tripChain;
        private ChangeOfMind ChangeOfMind => Machine.Owner._changeOfMind;

        protected abstract float MinDuration { get; }
        protected abstract float MaxDuration { get; }
        protected abstract float Speed { get; }
        protected abstract StateId SwitchTargetState { get; }
        protected abstract Gait Gait { get; }

        public override void OnStateEnter(StateId previous)
        {
            base.OnStateEnter(previous);
            _mindChange = MindChange.None;

            _enteredFromSwitch = previous is StateId.Wander or StateId.Sprint;
            if (_enteredFromSwitch)
            {
                // A failed extension keeps the current destination.
                Machine.Owner.Movement.TryExtendCurrentPath();
            }
            else
            {
                if (!Machine.Owner.Movement.TrySetRandomDestination())
                {
                    Machine.NextStateStateId = StateId.Idle;
                    return;
                }

                TripChain.Start();
                ChangeOfMind.Start();
            }

            SetOff();
        }

        public override void OnStateExit(StateId next)
        {
            base.OnStateExit(next);
            Machine.Owner.BodyPose.Glance(null);

            if (next is not (StateId.Wander or StateId.Sprint))
            {
                Machine.Owner.Movement.ClearPrecomputedForwardDestination();
            }
        }

        public override void OnStateUpdate()
        {
            base.OnStateUpdate();

            if (_mindChange != MindChange.None)
            {
                UpdateMindChange();
                return;
            }

            if (Machine.Owner.Movement.HasReachedDestination())
            {
                if (!ChainsTrip()) Machine.NextStateStateId = StateId.Idle;
                return;
            }

            if (TimeElapsed >= _duration)
            {
                Machine.NextStateStateId = StateId.Idle;
                return;
            }

            if (ChangesMind()) return;

            _nextPrecomputeForward -= Time.deltaTime;
            if (_nextPrecomputeForward <= 0f)
            {
                _nextPrecomputeForward = PrecomputeForwardInterval;
                Machine.Owner.Movement.StartPrecomputeForwardDestination();
            }

            if (!_enteredFromSwitch && TimeElapsed >= _nextSwitchCheck)
            {
                _nextSwitchCheck += SwitchCheckInterval;
                if (Random.value < SwitchChance)
                {
                    Machine.NextStateStateId = SwitchTargetState;
                }
            }
        }

        private bool ChainsTrip()
        {
            var arrival = TripChain.Arrive();
            var chains = arrival.Chains && Machine.Owner.Movement.TrySetNearbyDestination();
            LOG.Debug($"{GetType().Name}: {Machine.Owner.name} reached its point, roll={arrival.Roll:F2}" +
                      (arrival.Asked ? " (chain asked)" : "") + ", " +
                      (chains ? "chains a Trip" :
                          arrival.Capped ? "waits after two Trips in a row" :
                          arrival.Chains ? "waits, no point to chain to" : "waits"));
            if (!chains) return false;

            ChangeOfMind.Start();
            SetOff();
            return true;
        }

        private bool ChangesMind()
        {
            if (ChangeOfMind.Update(Time.deltaTime) is not { } change) return false;

            if (change.Capped)
            {
                LOG.Debug($"{GetType().Name}: {Machine.Owner.name} was asked to change its mind, but already did on " +
                          "this Trip: kept for its next Trip");
                return false;
            }

            LOG.Debug($"{GetType().Name}: {Machine.Owner.name} changes its mind" +
                      (change.Asked ? " (asked)" : $", roll={change.Roll:F3}") + ", easing to a stop");
            _mindChange = MindChange.Stopping;
            Machine.Owner.Movement.Stop();
            Machine.Owner.BodyPose.Move(Gait.Still);
            return true;
        }

        private void UpdateMindChange()
        {
            var movement = Machine.Owner.Movement;
            switch (_mindChange)
            {
                case MindChange.Stopping when movement.IsStill:
                    var facing = movement.TargetRotation.eulerAngles.y;
                    var glance = Wait.Toward(Random.Range(-Wait.MaxGlanceAngle, Wait.MaxGlanceAngle), 0f);
                    movement.Face(facing + glance.Body);
                    Machine.Owner.BodyPose.Glance(facing + glance.Head);
                    _glanceEnd = TimeElapsed + Random.Range(Wait.MinGlanceInterval, Wait.MaxGlanceInterval);
                    _mindChange = MindChange.Glancing;
                    LOG.Debug($"{GetType().Name}: {Machine.Owner.name} stopped, glances {glance.Head:F0}° from where " +
                              "it faced, " + (glance.BodyFollows ? $"the body following to {glance.Body:F0}°" : "the head alone"));
                    break;

                case MindChange.Glancing when TimeElapsed >= _glanceEnd:
                    _mindChange = MindChange.None;
                    Machine.Owner.BodyPose.Glance(null);
                    if (!movement.TrySetNearbyDestination())
                    {
                        LOG.Debug($"{GetType().Name}: {Machine.Owner.name} finds no other point to head to, waits");
                        Machine.NextStateStateId = StateId.Idle;
                        return;
                    }

                    LOG.Debug($"{GetType().Name}: {Machine.Owner.name} heads to another point " +
                              $"{Vector3.Distance(Machine.Owner.ControllerTransform.position, movement.Destination):F1} m " +
                              $"away, at a {Gait}");
                    SetOff();
                    break;
            }
        }

        private void SetOff()
        {
            _duration = TimeElapsed + Random.Range(MinDuration, MaxDuration);
            _nextSwitchCheck = TimeElapsed + SwitchCheckInterval;
            _nextPrecomputeForward = PrecomputeForwardInterval;
            Machine.Owner.Movement.SetSpeed(Speed);
            Machine.Owner.BodyPose.Move(Gait);
            Machine.Owner.Movement.StartPrecomputeForwardDestination();
        }

        private enum MindChange
        {
            None,
            Stopping,
            Glancing
        }
    }

    private sealed class WanderState : MovementState
    {
        protected override float MinDuration => 5f;
        protected override float MaxDuration => 15f;
        protected override float Speed => WalkSpeed;
        protected override StateId SwitchTargetState => StateId.Sprint;
        protected override Gait Gait => Gait.Walk;
    }

    private sealed class SprintState : MovementState
    {
        protected override float MinDuration => 3f;
        protected override float MaxDuration => 8f;
        protected override float Speed => SprintSpeed;
        protected override StateId SwitchTargetState => StateId.Wander;
        protected override Gait Gait => Gait.Sprint;
    }

    private sealed class CheckMapState : StateMachineBase<StateMachine, StateId>.StateBaseTimed
    {
        // Phase lengths only: the animator plays the raise and the lower through its Grabbing transitions.
        private const float RaiseDuration = 0.85f;
        private const float LowerDuration = 0.22f;
        private const float MinLookDuration = 0.5f;
        private const float MaxLookDuration = 3f;

        private Phase _currentPhase;
        private float _lookDuration;
        private float _phaseTimer;

        public override void OnStateEnter(StateId previous)
        {
            base.OnStateEnter(previous);

            Machine.Owner.Movement.Stop();
            Machine.Owner.BodyPose.CheckMap();

            _lookDuration = Random.Range(MinLookDuration, MaxLookDuration);
            _currentPhase = Phase.Raise;
            _phaseTimer = 0f;

            LOG.Debug($"CheckMapState: ENTER - LookDuration={_lookDuration:F1}s");
        }

        public override void OnStateUpdate()
        {
            base.OnStateUpdate();

            _phaseTimer += Time.deltaTime;

            switch (_currentPhase)
            {
                case Phase.Raise:
                    if (_phaseTimer >= RaiseDuration)
                    {
                        _currentPhase = Phase.Look;
                        _phaseTimer = 0f;
                        LOG.Debug("CheckMapState: -> Look");
                    }

                    break;

                case Phase.Look:
                    if (_phaseTimer >= _lookDuration)
                    {
                        // Lowering now lets the animator ease back to the neutral pose during Lower.
                        Machine.Owner.BodyPose.Move(Gait.Still);
                        _currentPhase = Phase.Lower;
                        _phaseTimer = 0f;
                        LOG.Debug("CheckMapState: -> Lower");
                    }

                    break;

                case Phase.Lower:
                    if (_phaseTimer >= LowerDuration)
                    {
                        LOG.Debug("CheckMapState: Done, back to Idle");
                        Machine.NextStateStateId = StateId.Idle;
                    }

                    break;
            }
        }

        public override void OnStateExit(StateId next)
        {
            base.OnStateExit(next);
            LOG.Debug($"CheckMapState: EXIT -> {next}");
        }

        private enum Phase
        {
            Raise,
            Look,
            Lower
        }
    }

    private abstract class StalkingState : StateMachineBase<StateMachine, StateId>.StateBaseTimed
    {
        public override void OnStateEnter(StateId previous)
        {
            base.OnStateEnter(previous);
            if (!IsStalkingState(previous))
            {
                LOG.Debug($"=== STALK: {Machine.Owner.name} starts Stalking ===");
            }
        }

        public override void OnStateExit(StateId next)
        {
            base.OnStateExit(next);
            if (!IsStalkingState(next))
            {
                LOG.Debug($"=== STALK: {Machine.Owner.name} ends its Stalking, -> {next} ===");
            }
        }
    }

    private sealed class StalkApproachState : StalkingState
    {
        private const float MaxApproachTime = 30f;
        private const float DestinationUpdateInterval = 1f;
        private float _nextDestinationUpdate;

        public override void OnStateEnter(StateId previous)
        {
            base.OnStateEnter(previous);
            LOG.Debug("=== STALK: Approach started - running towards player ===");

            Machine.Owner.BodyPose.Move(Gait.Sprint);
            Machine.Owner.Movement.SetSpeed(SprintSpeed);
            _nextDestinationUpdate = 0f;

            if (!Machine.Owner.Movement.TrySetDestinationToPlayer())
            {
                LOG.Debug("STALK: Failed to set destination to player, aborting");
                Machine.NextStateStateId = StateId.Idle;
            }
        }

        public override void OnStateUpdate()
        {
            base.OnStateUpdate();

            var distance = Machine.Owner.Movement.GetDistanceToPlayer();
            var reachedNavDest = Machine.Owner.Movement.HasReachedDestination();

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
                Machine.Owner.Movement.TrySetDestinationToPlayer();
                _nextDestinationUpdate = DestinationUpdateInterval;
            }
        }

        public override void OnStateExit(StateId next)
        {
            base.OnStateExit(next);
            Machine.Owner.Movement.Stop();
        }
    }

    /// <summary>
    ///     The Hallucination stares at the Victim while closing in; once the Victim's Gaze lands on it, it holds the
    ///     stare for a while, then flees.
    /// </summary>
    private sealed class StalkStareState : StalkingState
    {
        private const float MaxStareTime = 30f;
        private const float DestinationUpdateInterval = 0.5f;

        private float? _gazeLandedAt;
        private float _nextDestinationUpdate;

        public override void OnStateEnter(StateId previous)
        {
            base.OnStateEnter(previous);
            LOG.Debug("=== STALK: Staring at player ===");

            _gazeLandedAt = null;
            _nextDestinationUpdate = 0f;
            Machine.Owner.LungeCountdown.Reset();

            Machine.Owner.BodyPose.Stare(Gait.Walk);
            Machine.Owner.Movement.SetSpeed(WalkSpeed);
        }

        public override void OnStateUpdate()
        {
            base.OnStateUpdate();

            Machine.Owner.Movement.LookAtPlayer();

            var distance = Machine.Owner.Movement.GetDistanceToPlayer();

            if (Machine.Owner.Gaze.IsOn && _gazeLandedAt == null)
            {
                _gazeLandedAt = TimeElapsed;
                LOG.Debug($"=== STALK: Gaze landed, fleeing in {StalkStareBeforeFlee}s ===");

                Machine.Owner.BodyPose.Stare(Gait.Still);
                Machine.Owner.Movement.Stop();
            }

            if (_gazeLandedAt is { } landedAt)
            {
                var sinceGaze = TimeElapsed - landedAt;
                if (sinceGaze >= StalkStareBeforeFlee)
                {
                    LOG.Debug($"=== STALK: Fleeing {sinceGaze:F2}s after the Gaze landed, Gaze now " +
                              $"{(Machine.Owner.Gaze.IsOn ? "on" : "off")} ===");
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
                        Machine.Owner.Movement.TrySetDestinationToPlayer();
                        _nextDestinationUpdate = DestinationUpdateInterval;
                    }
                }
                else
                {
                    Machine.Owner.Movement.Stop();
                }

                if (Machine.Owner.LungeCountdown.Current == LungeCountdown.Phase.NotStarted &&
                    Machine.Owner.Movement.HasReachedDestination())
                {
                    LOG.Debug($"STALK: {Machine.Owner.name} stopped {distance:F1} m from the Victim, " +
                              "its count starts once nothing stands between them");
                    Machine.Owner.LungeCountdown.Start();
                }
            }

            if (Lunges(distance)) return;

            if (TimeElapsed >= MaxStareTime)
            {
                LOG.Debug("STALK: Stare timeout, fleeing");
                Machine.NextStateStateId = StateId.StalkFlee;
            }
        }

        private bool Lunges(float distance)
        {
            var owner = Machine.Owner;
            var countdown = owner.LungeCountdown;
            var e = countdown.Update(Time.deltaTime, distance, owner.Gaze.VisiblePart == null, owner.Gaze.IsOn);
            switch (e)
            {
                case LungeCountdown.Event.Started or LungeCountdown.Event.Resumed:
                    CameraGlitch.Instance.PlayShort();
                    LOG.Debug($"STALK: {owner.name}'s count {(e == LungeCountdown.Event.Started ? "starts" : "resumes")} " +
                              $"at {countdown.Elapsed:F1}s, {distance:F1} m from the Victim, the Victim's screen glitches");
                    break;
                case LungeCountdown.Event.Paused:
                    LOG.Debug($"STALK: {owner.name}'s count pauses at {countdown.Elapsed:F1}s, {distance:F1} m from " +
                              "the Victim: something stands between them");
                    break;
                case LungeCountdown.Event.Over:
                    LOG.Debug($"STALK: {owner.name}'s count is over, the Victim {distance:F1} m away is out of " +
                              "reach: the Lunge waits for them");
                    break;
                case LungeCountdown.Event.Gazed:
                    LOG.Debug($"STALK: {owner.name}'s count ends at {countdown.Elapsed:F1}s: the Gaze landed");
                    break;
                case LungeCountdown.Event.Lunge:
                    LOG.Debug($"=== STALK: {owner.name} lunges at the Victim {distance:F1} m away ===");
                    Machine.NextStateStateId = StateId.StalkLunge;
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    ///     The Hallucination sprints at the Victim with the stare's face, and kills them when its count says so,
    ///     whether it reached them or not.
    /// </summary>
    private sealed class StalkLungeState : StalkingState
    {
        private const float DestinationUpdateInterval = 0.5f;

        private float _closest;
        private float _nextDestinationUpdate;

        public override void OnStateEnter(StateId previous)
        {
            base.OnStateEnter(previous);
            _closest = float.MaxValue;
            _nextDestinationUpdate = 0f;
            Machine.Owner.BodyPose.Stare(Gait.Sprint);
            Machine.Owner.Movement.SetSpeed(SprintSpeed);
        }

        public override void OnStateUpdate()
        {
            base.OnStateUpdate();

            var owner = Machine.Owner;
            owner.Movement.LookAtPlayer();
            var distance = owner.Movement.GetDistanceToPlayer();
            _closest = Mathf.Min(_closest, distance);

            _nextDestinationUpdate -= Time.deltaTime;
            if (_nextDestinationUpdate <= 0f)
            {
                owner.Movement.TrySetDestinationToPlayer();
                _nextDestinationUpdate = DestinationUpdateInterval;
            }

            if (owner.LungeCountdown.Update(Time.deltaTime, distance, owner.Gaze.VisiblePart == null,
                    owner.Gaze.IsOn) != LungeCountdown.Event.Kill)
            {
                return;
            }

            var health = PlayerAvatar.instance.playerHealth;
            health.Hurt(int.MaxValue / 2, false);
            LOG.Debug($"=== STALK: {owner.name} kills the Victim {distance:F1} m away, {_closest:F1} m at the " +
                      $"closest, the Victim's health now {health.health} ===");

            // The death ends the Haunting, which removes the Hallucination; a Victim the game keeps alive
            // (invincible a moment, god mode) is left alone.
            Machine.NextStateStateId = StateId.StalkFlee;
        }
    }

    private sealed class StalkFleeState : StalkingState
    {
        private const float MaxFleeTime = 10f;

        public override void OnStateEnter(StateId previous)
        {
            base.OnStateEnter(previous);
            LOG.Debug("=== STALK: Fleeing from player! ===");

            Machine.Owner.BodyPose.Move(Gait.Sprint);
            Machine.Owner.Movement.SetSpeed(SprintSpeed);

            if (!Machine.Owner.Movement.TrySetFleeDestination(StalkFleeDistance))
            {
                LOG.Debug("STALK: Failed to set flee destination, going to idle");
                Machine.NextStateStateId = StateId.Idle;
            }
        }

        public override void OnStateUpdate()
        {
            base.OnStateUpdate();

            if (Machine.Owner.Movement.HasReachedDestination() || TimeElapsed >= MaxFleeTime)
            {
                LOG.Debug("=== STALK: Flee complete, returning to idle ===");
                Machine.NextStateStateId = StateId.Idle;
            }
        }
    }
}
