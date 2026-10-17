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
                if (distanceToPlayer > StalkMinDistance && Random.value < StalkChance)
                {
                    Machine.NextStateStateId = StateId.StalkApproach;
                    LOG.Debug($"IdleState: -> StalkApproach (dist={distanceToPlayer:F1})");
                    return;
                }

                var roll = Random.value;
                if (roll < CheckMapChance)
                {
                    Machine.NextStateStateId = StateId.CheckMap;
                    LOG.Debug($"IdleState: -> CheckMap (roll={roll:F2} < {CheckMapChance})");
                }
                else
                {
                    Machine.NextStateStateId = Random.value < SprintChance ? StateId.Sprint : StateId.Wander;
                    LOG.Debug($"IdleState: -> {Machine.NextStateStateId}");
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

        private TripChain TripChain => Machine.Owner._tripChain;

        protected abstract float MinDuration { get; }
        protected abstract float MaxDuration { get; }
        protected abstract float Speed { get; }
        protected abstract StateId SwitchTargetState { get; }
        protected abstract Gait Gait { get; }

        public override void OnStateEnter(StateId previous)
        {
            base.OnStateEnter(previous);
            _duration = Random.Range(MinDuration, MaxDuration);
            _nextSwitchCheck = SwitchCheckInterval;
            _nextPrecomputeForward = PrecomputeForwardInterval;

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
            }

            Machine.Owner.Movement.SetSpeed(Speed);
            Machine.Owner.BodyPose.Move(Gait);

            Machine.Owner.Movement.StartPrecomputeForwardDestination();
        }

        public override void OnStateExit(StateId next)
        {
            base.OnStateExit(next);

            if (next is not (StateId.Wander or StateId.Sprint))
            {
                Machine.Owner.Movement.ClearPrecomputedForwardDestination();
            }
        }

        public override void OnStateUpdate()
        {
            base.OnStateUpdate();

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
            var chains = arrival.Chains && Machine.Owner.Movement.TrySetDestinationSync();
            LOG.Debug($"{GetType().Name}: {Machine.Owner.name} reached its point, roll={arrival.Roll:F2}" +
                      (arrival.Asked ? " (chain asked)" : "") + ", " +
                      (chains ? "chains a Trip" :
                          arrival.Capped ? "waits after two Trips in a row" :
                          arrival.Chains ? "waits, no point to chain to" : "waits"));
            if (chains) _duration = TimeElapsed + Random.Range(MinDuration, MaxDuration);
            return chains;
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

    private sealed class StalkApproachState : StateMachineBase<StateMachine, StateId>.StateBaseTimed
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
    private sealed class StalkStareState : StateMachineBase<StateMachine, StateId>.StateBaseTimed
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
            }

            if (TimeElapsed >= MaxStareTime)
            {
                LOG.Debug("STALK: Stare timeout, fleeing");
                Machine.NextStateStateId = StateId.StalkFlee;
            }
        }
    }

    private sealed class StalkFleeState : StateMachineBase<StateMachine, StateId>.StateBaseTimed
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
