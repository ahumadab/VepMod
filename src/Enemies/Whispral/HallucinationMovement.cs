using System;
using System.Collections;
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
    private const float FallAnimDelay = 0.12f; // s airborne before the fall animation starts

    private const float ForwardMaxAngleDeviation = 30f;

    private static readonly VepLogger LOG = VepLogger.Create<HallucinationMovement>();

    private CharacterController _charController;
    private Transform _controllerTransform;
    private Vector3 _currentVelocity;
    private float _verticalVelocity;
    private float _airborneTime;
    private Vector3 _destination;
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

    /// <summary>
    ///     Drives the animator's 'Falling' bool; the airborne delay keeps it from flickering on stairs.
    /// </summary>
    public bool IsFalling => _charController != null
                             && !_charController.isGrounded
                             && _airborneTime >= FallAnimDelay
                             && _verticalVelocity < 0f;

    public float DebugVerticalVelocity => _verticalVelocity;

    public bool DebugIsGrounded => _charController != null && _charController.isGrounded;

    internal bool IsOnNavMesh => _navAgent != null && _navAgent.isOnNavMesh;

    public event Action? OnNavMeshError;

    public float GetDistanceToPlayer()
    {
        var player = PlayerAvatar.instance;
        if (player == null || _controllerTransform == null) return float.MaxValue;
        return Vector3.Distance(_controllerTransform.position, player.transform.position);
    }

    public bool HasReachedDestination()
    {
        if (_navAgent == null || _navAgent.pathPending) return false;

        var controllerPos = _controllerTransform != null ? _controllerTransform.position : transform.position;
        var distance = Vector3.Distance(controllerPos, _destination);

        return !_navAgent.hasPath || distance <= _navAgent.stoppingDistance;
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
    }

    public void LookAtPlayer()
    {
        var player = PlayerAvatar.instance;
        if (player == null || _controllerTransform == null) return;

        var direction = (player.transform.position - _controllerTransform.position).normalized;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.01f)
        {
            TargetRotation = Quaternion.LookRotation(direction);
        }
    }

    public void ResetPath()
    {
        if (IsOnNavMesh)
        {
            _navAgent.ResetPath();
        }
    }

    public void SetSpeed(float speed)
    {
        if (_navAgent != null)
        {
            _navAgent.speed = speed;
        }
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

    public void SyncNavAgentPosition(bool isMovementState)
    {
        if (_navAgent == null || _controllerTransform == null) return;

        // A falling controller is off the NavMesh on purpose: resyncing before it lands would report a false
        // off-NavMesh error and send the Hallucination back to Idle.
        if (_charController != null && !_charController.isGrounded) return;

        var controllerPos = _controllerTransform.position;

        if (NavMesh.SamplePosition(controllerPos, out var hit, 2f, _savedAreaMask))
        {
            _navAgent.nextPosition = hit.position;

            if (Vector3.Distance(controllerPos, hit.position) > 0.5f && isMovementState && _navAgent.hasPath)
            {
                _navAgent.SetDestination(_destination);
            }
        }
        else
        {
            LOG.Warning($"Controller off NavMesh at {controllerPos}");
            OnNavMeshError?.Invoke();
        }
    }

    public void SyncVisualsToController(Transform? animatorTransform)
    {
        if (_rigidbodyTransform == null || _controllerTransform == null) return;

        var yOffset = new Vector3(0f, VisualYOffset, 0f);
        _rigidbodyTransform.position = _controllerTransform.position + yOffset;
        _rigidbodyTransform.rotation = _controllerTransform.rotation;

        if (animatorTransform != null && animatorTransform.localPosition != Vector3.zero)
        {
            animatorTransform.localPosition = Vector3.zero;
        }
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

    private bool TryCalculateCompletePath(Vector3 destination, NavMeshPath path)
    {
        return IsOnNavMesh
               && _navAgent.CalculatePath(destination, path)
               && path.status == NavMeshPathStatus.PathComplete;
    }

    private bool TrySetPath(Vector3 destination)
    {
        var path = new NavMeshPath();
        if (!TryCalculateCompletePath(destination, path))
        {
            LOG.Debug($"TrySetPath: No complete path to {destination}");
            return false;
        }

        _destination = destination;
        _navAgent.SetPath(path);
        return true;
    }

    /// <summary>
    ///     Applies gravity every frame, even outside movement states, so the Hallucination always lands.
    /// </summary>
    public void UpdateMovement(bool isMovementState)
    {
        if (_charController == null) return;

        var horizontalVelocity = Vector3.zero;
        if (isMovementState && _navAgent != null && _navAgent.hasPath)
        {
            _currentVelocity = Vector3.Lerp(_currentVelocity, _navAgent.desiredVelocity, 5f * Time.deltaTime);
            horizontalVelocity = _currentVelocity;
            horizontalVelocity.y = 0f;
        }
        else
        {
            _currentVelocity = Vector3.zero;
        }

        if (_charController.isGrounded)
        {
            _verticalVelocity = -GroundedStickSpeed;
            _airborneTime = 0f;
        }
        else
        {
            _verticalVelocity -= Mathf.Abs(Physics.gravity.y) * Time.deltaTime;
            if (_verticalVelocity < -MaxFallSpeed) _verticalVelocity = -MaxFallSpeed;
            _airborneTime += Time.deltaTime;
        }

        var motion = horizontalVelocity;
        motion.y = _verticalVelocity;
        _charController.Move(motion * Time.deltaTime);
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

        if (TryCalculateCompletePath(hit.position, new NavMeshPath()))
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

        if (!TryCalculateCompletePath(point, new NavMeshPath()))
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
