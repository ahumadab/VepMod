using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using VepMod.VepFramework;

// ReSharper disable Unity.NoNullCoalescing
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

namespace VepMod.Enemies.Whispral;

public sealed class HallucinationMovement : MonoBehaviour
{
    private const float RotationSpeed = 10f;
    private const float VisualYOffset = 0.25f;

    // The NavMesh drives horizontal motion only: gravity is simulated so a Hallucination falls from heights
    // instead of descending at constant speed, with the game's own Physics.gravity.
    private const float GroundedStickSpeed = 2f; // m/s, keeps the controller on the ground and slopes
    private const float MaxFallSpeed = 55f; // m/s, anti-tunnelling clamp, never reached in a level

    private const float ForwardMaxAngleDeviation = 30f;

    private const float SteeringLookahead = 1.25f; // m
    private const int MaxCorners = 64;
    private const float MinEdgeSteering = 0.5f; // m
    private const float StepAsideProbe = 0.5f; // m

    private static readonly VepLogger LOG = VepLogger.Create<HallucinationMovement>();

    // A NavMesh path's corners lie on the NavMesh edge, so rounding one cuts off the NavMesh: moved this far outside
    // its turn, the first margin that keeps the path on the NavMesh, a corner is rounded without leaving it.
    private static readonly float[] CornerMargins = { 1f, 0.5f }; // m

    private static readonly List<HallucinationMovement> All = new();

    private readonly Vector3[] _navMeshCorners = new Vector3[MaxCorners];
    private readonly Vector3[] _corners = new Vector3[MaxCorners];
    private readonly MovementFriction _friction = new();
    private readonly SteeringPath _steering = new(MaxCorners);

    private NavMeshPath _calculatedPath;
    private CharacterController _charController;
    private Transform _controllerTransform;
    private Vector3 _currentVelocity;
    private float _verticalVelocity;
    private Vector3 _destination;
    private Quaternion? _facing;
    private bool _isOnTrip;
    private string? _stepAsideFor;
    private float _stepAsideMaxAngle;
    private float _stepAsideClosest;
    private int _passingCorner = -1;
    private float _passClosest;
    private float _passSteeringPast;
    private bool _passCutShort;
    private float _tripSpeed;
    private bool _isPrecomputingDestination;
    private bool _isPrecomputingForward;
    private NavMeshAgent _navAgent;
    private Vector3? _precomputedDestination;
    private Vector3? _precomputedForwardDestination;
    private Transform? _rigidbodyTransform;
    private int _savedAreaMask = NavMesh.AllAreas;

    public bool HasPrecomputedDestination => _precomputedDestination.HasValue;

    public bool HasPrecomputedForwardDestination => _precomputedForwardDestination.HasValue;

    public Quaternion TargetRotation { get; private set; } = Quaternion.identity;

    public Vector3 CurrentVelocity => _currentVelocity;

    public float VerticalVelocity => _verticalVelocity;

    public bool IsGrounded => _charController != null && _charController.isGrounded;

    public bool IsMoving => _friction.IsMoving;

    internal bool IsOnNavMesh => _navAgent != null && _navAgent.isOnNavMesh;

    public event Action? OnNavMeshError;

    public float GetDistanceToPlayer()
    {
        var player = PlayerAvatar.instance;
        if (player == null || _controllerTransform == null) return float.MaxValue;
        return Vector3.Distance(_controllerTransform.position, player.transform.position);
    }

    /// <summary>
    ///     True once the trip is close enough to its end for the stop to ease into it.
    /// </summary>
    public bool HasReachedDestination()
    {
        return !_isOnTrip || _steering.RemainingDistance <= _friction.StoppingDistance;
    }

    public void Initialize(
        Transform controllerTransform,
        Transform? rigidbodyTransform,
        NavMeshAgent navAgent,
        CharacterController charController,
        int areaMask)
    {
        _controllerTransform = controllerTransform;
        _rigidbodyTransform = rigidbodyTransform;
        _navAgent = navAgent;
        _charController = charController;
        _savedAreaMask = areaMask;
        _calculatedPath = new NavMeshPath();
        All.Add(this);
    }

    private void OnDestroy()
    {
        All.Remove(this);
    }

    /// <summary>
    ///     Dev tools only: replaces the current trip's destination.
    /// </summary>
    public bool DebugSetDestination(Vector3 point)
    {
        return TrySetPath(point);
    }

    /// <summary>
    ///     Dev tools only: the destination of the next trip.
    /// </summary>
    public void DebugQueueDestination(Vector3 point)
    {
        _precomputedDestination = point;
    }

    public void LookAtPlayer()
    {
        var player = PlayerAvatar.instance;
        if (player == null || _controllerTransform == null) return;

        var direction = (player.transform.position - _controllerTransform.position).normalized;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.01f)
        {
            _facing = Quaternion.LookRotation(direction);
        }
    }

    /// <summary>
    ///     Turns the Hallucination to <paramref name="yaw" />, in degrees, once it stands still, until its next trip.
    /// </summary>
    public void Face(float yaw)
    {
        _facing = Quaternion.Euler(0f, yaw, 0f);
    }

    /// <summary>
    ///     Ends the trip: the Hallucination eases to a stop along its path.
    /// </summary>
    public void Stop()
    {
        _isOnTrip = false;
    }

    public void SetSpeed(float speed)
    {
        _tripSpeed = speed;
    }

    public void StartPrecomputeDestination()
    {
        if (_isPrecomputingDestination || _precomputedDestination.HasValue) return;
        StartCoroutine(PrecomputeDestinationCoroutine());
    }

    public void StartPrecomputeForwardDestination()
    {
        if (_isPrecomputingForward) return;
        _precomputedForwardDestination = null;
        StartCoroutine(PrecomputeForwardDestinationCoroutine());
    }

    public void ClearPrecomputedForwardDestination()
    {
        _precomputedForwardDestination = null;
    }

    public void SyncNavAgentPosition()
    {
        if (_navAgent == null || _controllerTransform == null) return;

        // A falling controller is off the NavMesh on purpose: resyncing before it lands would report a false
        // off-NavMesh error and send the Hallucination back to Idle.
        if (_charController != null && !_charController.isGrounded) return;

        var controllerPos = _controllerTransform.position;

        if (NavMesh.SamplePosition(controllerPos, out var hit, 2f, _savedAreaMask))
        {
            _navAgent.nextPosition = hit.position;

            if (Vector3.Distance(controllerPos, hit.position) > 0.5f && _isOnTrip)
            {
                TrySetPath(_destination);
            }
        }
        else
        {
            LOG.Warning($"Controller off NavMesh at {controllerPos}");
            OnNavMeshError?.Invoke();
        }
    }

    public void SyncRigidbodyToController()
    {
        if (_rigidbodyTransform == null || _controllerTransform == null) return;

        var yOffset = new Vector3(0f, VisualYOffset, 0f);
        _rigidbodyTransform.position = _controllerTransform.position + yOffset;
        _rigidbodyTransform.rotation = _controllerTransform.rotation;
    }

    public bool TrySetDestinationToPlayer()
    {
        var player = PlayerAvatar.instance;
        if (player == null || !IsOnNavMesh) return false;

        var playerPos = player.transform.position;
        return NavMesh.SamplePosition(playerPos, out var hit, 5f, _savedAreaMask) && TrySetPath(hit.position);
    }

    public bool TrySetFleeDestination(float fleeDistance)
    {
        var player = PlayerAvatar.instance;
        if (player == null || _controllerTransform == null) return false;

        var directionAway = (_controllerTransform.position - player.transform.position).normalized;
        var fleeTarget = _controllerTransform.position + directionAway * fleeDistance;

        return NavMesh.SamplePosition(fleeTarget, out var hit, 10f, _savedAreaMask) && TrySetPath(hit.position);
    }

    public bool TrySetRandomDestination()
    {
        if (_precomputedDestination.HasValue)
        {
            var destination = _precomputedDestination.Value;
            _precomputedDestination = null;
            return TrySetPath(destination);
        }

        LOG.Warning("No precomputed destination, falling back to sync search");
        return TrySetDestinationSync();
    }

    public bool TryExtendCurrentPath()
    {
        var precomputed = _precomputedForwardDestination;
        _precomputedForwardDestination = null;

        Vector3 destination;
        if (precomputed.HasValue) destination = precomputed.Value;
        else if (!TryFindForwardPoint(out destination)) return false;

        var extended = TrySetPath(destination);
        LOG.Debug($"TryExtendCurrentPath: {(precomputed.HasValue ? "precomputed" : "sync")} destination " +
                  $"{destination}, extended={extended}");
        return extended;
    }

    private bool TryFindForwardPoint(out Vector3 point)
    {
        point = default;
        if (_controllerTransform == null || !IsOnNavMesh) return false;

        var direction = _currentVelocity.normalized;
        var directionSource = "velocity";

        if (direction.sqrMagnitude < 0.01f)
        {
            direction = (_destination - _controllerTransform.position).normalized;
            directionSource = "destination";
        }

        if (direction.sqrMagnitude < 0.01f)
        {
            LOG.Debug("TryFindForwardPoint: No valid direction found");
            return false;
        }

        var angleDeviation = UnityEngine.Random.Range(-ForwardMaxAngleDeviation, ForwardMaxAngleDeviation);
        direction = Quaternion.Euler(0f, angleDeviation, 0f) * direction;

        var distance = UnityEngine.Random.Range(Hallucination.SpawnDistanceMin, Hallucination.SpawnDistanceMax);
        var aheadPos = _controllerTransform.position + direction * distance;

        LOG.Debug($"TryFindForwardPoint: source={directionSource}, angle={angleDeviation:F1}°, dist={distance:F1}m");

        if (!NavMesh.SamplePosition(aheadPos, out var hit, 10f, _savedAreaMask))
        {
            LOG.Debug($"TryFindForwardPoint: No NavMesh position found near {aheadPos}");
            return false;
        }

        point = hit.position;
        return true;
    }

    private bool TryCalculateCompletePath(Vector3 destination)
    {
        return IsOnNavMesh
               && _navAgent.CalculatePath(destination, _calculatedPath)
               && _calculatedPath.status == NavMeshPathStatus.PathComplete;
    }

    private bool TrySetPath(Vector3 destination)
    {
        if (!TryCalculateCompletePath(destination)) return false;

        _destination = destination;
        _facing = null;
        _navAgent.SetPath(_calculatedPath);

        var count = _calculatedPath.GetCornersNonAlloc(_navMeshCorners);
        Array.Copy(_navMeshCorners, _corners, count);
        MoveCornersOutsideTurns(count);
        _passingCorner = -1;
        _steering.Clear();
        for (var i = 0; i < count; i++)
        {
            _steering.Add(_corners[i].x, _corners[i].z);
        }

        _isOnTrip = true;
        return true;
    }

    private void MoveCornersOutsideTurns(int count)
    {
        var moved = 0;
        for (var i = 1; i < count - 1; i++)
        {
            var previous = _corners[i - 1];
            var corner = _corners[i];
            var next = _corners[i + 1];
            foreach (var margin in CornerMargins)
            {
                if (SteeringPath.OutsideOfTurn(previous.x, previous.z, corner.x, corner.z, next.x, next.z, margin)
                    is not { } outside)
                {
                    break;
                }

                var candidate = new Vector3(outside.X, corner.y, outside.Z);
                if (NavMesh.Raycast(previous, candidate, out _, _savedAreaMask) ||
                    NavMesh.Raycast(candidate, next, out _, _savedAreaMask))
                {
                    continue;
                }

                _corners[i] = candidate;
                moved++;
                break;
            }
        }

        if (count > 2) LOG.Debug($"{name}: path of {count} corners, {moved} moved off the NavMesh edge");
    }

    /// <summary>
    ///     Applies gravity every frame, even outside a trip, so the Hallucination always lands.
    /// </summary>
    public void UpdateMovement()
    {
        if (_charController == null || _controllerTransform == null) return;

        if (_friction.Update(_isOnTrip ? _tripSpeed : 0f, Time.deltaTime) is { } ease)
        {
            LOG.Debug($"{name}: speed eased from {ease.From:F1} to {ease.To:F1} m/s in {ease.Duration:F2}s");
        }

        _currentVelocity = SteeringVelocity();

        if (_charController.isGrounded)
        {
            _verticalVelocity = -GroundedStickSpeed;
        }
        else
        {
            _verticalVelocity -= Mathf.Abs(Physics.gravity.y) * Time.deltaTime;
            if (_verticalVelocity < -MaxFallSpeed) _verticalVelocity = -MaxFallSpeed;
        }

        var motion = _currentVelocity;
        motion.y = _verticalVelocity;
        _charController.Move(motion * Time.deltaTime);
    }

    private Vector3 SteeringVelocity()
    {
        var position = _controllerTransform.position;
        var (x, z) = _steering.Steer(position.x, position.z, SteeringLookahead);
        if (_steering.IsEmpty || _friction.Speed <= 0f) return Vector3.zero;

        var steeringPoint = new Vector3(x, position.y, z);

        // Rounding a corner cuts inside it: where the NavMesh edge is in the way, the Hallucination steers to the
        // edge, or close to it to the agent's next corner, which is always in the clear.
        var cutShort = _navAgent.Raycast(steeringPoint, out var edge);
        if (cutShort)
        {
            steeringPoint = HorizontalDistance(position, edge.position) >= MinEdgeSteering
                ? edge.position
                : _navAgent.steeringTarget;
        }

        if (_steering.RoundedCorner is { } rounded)
        {
            LogCornerPass();
            _passingCorner = rounded;
            _passClosest = float.MaxValue;
        }

        TrackCornerPass(position, steeringPoint, cutShort);

        var toPoint = steeringPoint - position;
        toPoint.y = 0f;
        var distance = toPoint.magnitude;
        if (distance < 0.01f) return Vector3.zero;

        // Capped so the end of the path is never overshot.
        return StepAside(position, toPoint / distance) * Mathf.Min(_friction.Speed, distance / Time.deltaTime);
    }

    private Vector3 StepAside(Vector3 position, Vector3 heading)
    {
        HallucinationMovement? steppingAsideFor = null;
        var push = Vector3.zero;
        foreach (var other in All)
        {
            if (other == this || other._controllerTransform == null) continue;

            var toOther = other._controllerTransform.position - position;
            var (x, z) = Avoidance.Deflection(heading.x, heading.z, toOther.x, toOther.z);
            if (x == 0f && z == 0f) continue;

            push.x += x;
            push.z += z;
            steppingAsideFor = other;
        }

        var deflected = (heading + push).normalized;
        // Probed a short step ahead: a lookahead's length would reach the walls of a narrow corridor.
        if (steppingAsideFor == null || _navAgent.Raycast(position + deflected * StepAsideProbe, out _))
        {
            if (_stepAsideFor != null)
            {
                LOG.Debug($"{name} stepped aside for {_stepAsideFor}: up to {_stepAsideMaxAngle:F0}° off its path, " +
                          $"{_stepAsideClosest:F1} m from it at the closest");
                _stepAsideFor = null;
            }

            return heading;
        }

        if (_stepAsideFor == null)
        {
            _stepAsideFor = steppingAsideFor.name;
            _stepAsideMaxAngle = 0f;
            _stepAsideClosest = float.MaxValue;
        }

        _stepAsideMaxAngle = Mathf.Max(_stepAsideMaxAngle, Vector3.Angle(heading, deflected));
        _stepAsideClosest = Mathf.Min(_stepAsideClosest,
            HorizontalDistance(position, steppingAsideFor._controllerTransform.position));
        return deflected;
    }

    // Logged at the closest approach: when the corner starts being rounded, the steering point is still on it.
    private void TrackCornerPass(Vector3 position, Vector3 steeringPoint, bool cutShort)
    {
        if (_passingCorner < 0) return;

        var corner = _navMeshCorners[_passingCorner];
        var distance = HorizontalDistance(position, corner);
        if (distance < _passClosest)
        {
            _passClosest = distance;
            _passSteeringPast = HorizontalDistance(corner, steeringPoint);
            _passCutShort = cutShort;
        }
        else if (distance > _passClosest + 0.1f)
        {
            LogCornerPass();
        }
    }

    private void LogCornerPass()
    {
        if (_passingCorner < 0) return;

        var corner = _navMeshCorners[_passingCorner];
        LOG.Debug($"{name} passes the NavMesh corner at {corner} {_passClosest:F1} m from it, steering " +
                  $"{_passSteeringPast:F1} m past it, the path {HorizontalDistance(corner, _corners[_passingCorner]):F1} m " +
                  "outside it" + (_passCutShort ? ", cut short by the NavMesh edge" : ""));
        _passingCorner = -1;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = b.y;
        return Vector3.Distance(a, b);
    }

    public void UpdateRotation()
    {
        if (_controllerTransform == null) return;

        if (_currentVelocity.magnitude > 0.1f)
        {
            var velocityDirection = _currentVelocity.normalized;
            velocityDirection.y = 0f;
            if (velocityDirection.sqrMagnitude > 0.01f)
            {
                TargetRotation = Quaternion.LookRotation(velocityDirection);
            }
        }
        else if (_facing is { } facing)
        {
            TargetRotation = facing;
        }

        _controllerTransform.rotation = Quaternion.Slerp(
            _controllerTransform.rotation,
            TargetRotation,
            RotationSpeed * Time.deltaTime);
    }

    private IEnumerator PrecomputeDestinationCoroutine()
    {
        _isPrecomputingDestination = true;

        var controllerPos = _controllerTransform != null ? _controllerTransform.position : transform.position;
        var levelPoint = SemiFunc.LevelPointGet(controllerPos, Hallucination.SpawnDistanceMin,
                             Hallucination.SpawnDistanceMax);
        if (levelPoint == null) levelPoint = SemiFunc.LevelPointGet(controllerPos, 0f, 20f);

        if (levelPoint == null)
        {
            _isPrecomputingDestination = false;
            yield break;
        }

        yield return null;

        var targetPos = levelPoint.transform.position;
        if (!NavMesh.SamplePosition(targetPos, out var hit, 5f, _savedAreaMask))
        {
            _isPrecomputingDestination = false;
            yield break;
        }

        yield return null;

        if (!Physics.Raycast(hit.position + Vector3.up, Vector3.down, 5f, LayerMask.GetMask("Default")))
        {
            _isPrecomputingDestination = false;
            yield break;
        }

        if (TryCalculateCompletePath(hit.position))
        {
            _precomputedDestination = hit.position;
        }

        _isPrecomputingDestination = false;
    }

    private bool TrySetDestinationSync()
    {
        var controllerPos = _controllerTransform != null ? _controllerTransform.position : transform.position;

        var levelPoint = SemiFunc.LevelPointGet(controllerPos, Hallucination.SpawnDistanceMin,
                             Hallucination.SpawnDistanceMax);
        if (levelPoint == null) levelPoint = SemiFunc.LevelPointGet(controllerPos, 0f, 20f);

        if (levelPoint == null) return false;

        var targetPos = levelPoint.transform.position;

        if (!NavMesh.SamplePosition(targetPos, out var hit, 5f, _savedAreaMask))
        {
            return false;
        }

        if (!Physics.Raycast(hit.position + Vector3.up, Vector3.down, 5f, LayerMask.GetMask("Default")))
        {
            return false;
        }

        return TrySetPath(hit.position);
    }

    private IEnumerator PrecomputeForwardDestinationCoroutine()
    {
        _isPrecomputingForward = true;

        if (!TryFindForwardPoint(out var point))
        {
            _isPrecomputingForward = false;
            yield break;
        }

        yield return null;

        if (!TryCalculateCompletePath(point))
        {
            LOG.Debug($"PrecomputeForward: No complete path to {point}");
            _isPrecomputingForward = false;
            yield break;
        }

        _precomputedForwardDestination = point;
        LOG.Debug($"PrecomputeForward: SUCCESS - precomputed destination at {point}");
        _isPrecomputingForward = false;
    }
}
