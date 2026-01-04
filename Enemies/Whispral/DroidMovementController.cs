using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using VepMod.VepFramework;

// ReSharper disable Unity.NoNullCoalescing
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

namespace VepMod.Enemies.Whispral;

/// <summary>
///     Contrôleur de mouvement pour HallucinationDroid.
///     Gère la navigation, le pathfinding, la rotation et la synchronisation des visuels.
/// </summary>
public sealed class DroidMovementController : MonoBehaviour
{
    private const float RotationSpeed = 10f;
    private const float VisualYOffset = 0.25f;

    private static readonly VepLogger LOG = VepLogger.Create<DroidMovementController>();

    private CharacterController _charController;
    private Transform _controllerTransform;
    private Vector3 _currentVelocity;
    private Vector3 _destination;
    private bool _isPrecomputingDestination;
    private bool _isPrecomputingForward;
    private NavMeshAgent _navAgent;
    private Vector3? _precomputedDestination;
    private Vector3? _precomputedForwardDestination;
    private Transform? _rigidbodyTransform;
    private int _savedAreaMask = NavMesh.AllAreas;

    /// <summary>
    ///     Indique si une destination pré-calculée est disponible.
    /// </summary>
    public bool HasPrecomputedDestination => _precomputedDestination.HasValue;

    /// <summary>
    ///     Indique si une destination forward pré-calculée est disponible.
    /// </summary>
    public bool HasPrecomputedForwardDestination => _precomputedForwardDestination.HasValue;

    /// <summary>
    ///     La rotation cible actuelle (pour le calcul de IsTurning).
    /// </summary>
    public Quaternion TargetRotation { get; private set; } = Quaternion.identity;

    /// <summary>
    ///     Vélocité actuelle du mouvement.
    /// </summary>
    public Vector3 CurrentVelocity => _currentVelocity;

    /// <summary>
    ///     Événement déclenché quand le controller sort du NavMesh.
    /// </summary>
    public event Action? OnNavMeshError;

    /// <summary>
    ///     Retourne la distance entre le droid et le joueur local.
    /// </summary>
    public float GetDistanceToPlayer()
    {
        var playerPos = DroidHelpers.GetLocalPlayerPosition();
        if (!playerPos.HasValue || _controllerTransform == null) return float.MaxValue;
        return Vector3.Distance(_controllerTransform.position, playerPos.Value);
    }

    /// <summary>
    ///     Vérifie si la destination a été atteinte.
    /// </summary>
    public bool HasReachedDestination()
    {
        if (_navAgent == null || _navAgent.pathPending) return false;

        var controllerPos = _controllerTransform != null ? _controllerTransform.position : transform.position;
        var distance = Vector3.Distance(controllerPos, _destination);

        return !_navAgent.hasPath || distance <= _navAgent.stoppingDistance;
    }

    /// <summary>
    ///     Initialise le contrôleur de mouvement.
    /// </summary>
    public void Initialize(
        Transform controllerTransform,
        Transform rigidbodyTransform,
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

    /// <summary>
    ///     Fait tourner le droid pour regarder le joueur.
    /// </summary>
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

    /// <summary>
    ///     Réinitialise le chemin de navigation.
    /// </summary>
    public void ResetPath()
    {
        if (DroidHelpers.IsNavAgentValid(_navAgent))
        {
            _navAgent.ResetPath();
        }
    }

    /// <summary>
    ///     Définit la vitesse du NavAgent.
    /// </summary>
    public void SetSpeed(float speed)
    {
        if (_navAgent != null)
        {
            _navAgent.speed = speed;
        }
    }

    /// <summary>
    ///     Démarre le pré-calcul d'une destination en coroutine.
    /// </summary>
    public void StartPrecomputeDestination()
    {
        if (_isPrecomputingDestination || _precomputedDestination.HasValue) return;
        StartCoroutine(PrecomputeDestinationCoroutine());
    }

    /// <summary>
    ///     Démarre le pré-calcul d'une destination forward (dans la direction actuelle).
    /// </summary>
    public void StartPrecomputeForwardDestination(float minDistance = 5f, float maxDistance = 15f, float maxAngleDeviation = 30f)
    {
        if (_isPrecomputingForward) return;
        // On invalide l'ancienne destination forward pour en calculer une nouvelle
        _precomputedForwardDestination = null;
        StartCoroutine(PrecomputeForwardDestinationCoroutine(minDistance, maxDistance, maxAngleDeviation));
    }

    /// <summary>
    ///     Réinitialise la destination forward pré-calculée.
    /// </summary>
    public void ClearPrecomputedForwardDestination()
    {
        _precomputedForwardDestination = null;
    }

    /// <summary>
    ///     Synchronise la position du NavAgent avec le controller.
    /// </summary>
    public void SyncNavAgentPosition(bool isMovementState)
    {
        if (_navAgent == null || _controllerTransform == null) return;

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

    /// <summary>
    ///     Synchronise la position des visuels avec le controller.
    /// </summary>
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

    /// <summary>
    ///     Définit la destination vers le joueur local.
    /// </summary>
    public bool TrySetDestinationToPlayer()
    {
        var player = PlayerAvatar.instance;
        if (player == null || !DroidHelpers.IsNavAgentValid(_navAgent)) return false;

        var playerPos = player.transform.position;
        if (!NavMesh.SamplePosition(playerPos, out var hit, 5f, _savedAreaMask)) return false;

        var path = new NavMeshPath();
        if (!_navAgent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete)
        {
            return false;
        }

        _destination = hit.position;
        _navAgent.SetPath(path);
        return true;
    }

    /// <summary>
    ///     Définit une destination pour fuir le joueur.
    /// </summary>
    public bool TrySetFleeDestination(float fleeDistance)
    {
        var player = PlayerAvatar.instance;
        if (player == null || _controllerTransform == null) return false;

        var directionAway = (_controllerTransform.position - player.transform.position).normalized;
        var fleeTarget = _controllerTransform.position + directionAway * fleeDistance;

        if (!NavMesh.SamplePosition(fleeTarget, out var hit, 10f, _savedAreaMask)) return false;

        if (!DroidHelpers.IsNavAgentValid(_navAgent)) return false;

        var path = new NavMeshPath();
        if (!_navAgent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete)
        {
            return false;
        }

        _destination = hit.position;
        _navAgent.SetPath(path);
        return true;
    }

    /// <summary>
    ///     Essaie de définir une destination aléatoire.
    /// </summary>
    public bool TrySetRandomDestination()
    {
        if (_precomputedDestination.HasValue)
        {
            return TrySetDestination(_precomputedDestination.Value);
        }

        LOG.Warning("No precomputed destination, falling back to sync search");
        return TrySetDestinationSync();
    }

    /// <summary>
    ///     Essaie de prolonger le chemin actuel dans la même direction (avec variation angulaire).
    ///     Utilise la destination pré-calculée si disponible.
    /// </summary>
    public bool TryExtendCurrentPath()
    {
        // Utiliser la destination pré-calculée si disponible
        if (_precomputedForwardDestination.HasValue)
        {
            LOG.Debug($"TryExtendCurrentPath: Using precomputed forward destination at {_precomputedForwardDestination.Value}");
            return TrySetForwardDestination(_precomputedForwardDestination.Value);
        }

        LOG.Debug("TryExtendCurrentPath: No precomputed forward destination, falling back to sync calculation");
        return TryExtendCurrentPathSync();
    }

    /// <summary>
    ///     Calcul synchrone d'extension de chemin (fallback si pas de pré-calcul).
    /// </summary>
    private bool TryExtendCurrentPathSync(float minDistance = 5f, float maxDistance = 15f, float maxAngleDeviation = 30f)
    {
        if (_controllerTransform == null || !DroidHelpers.IsNavAgentValid(_navAgent))
        {
            LOG.Debug("TryExtendCurrentPathSync: Invalid controller or NavAgent");
            return false;
        }

        // Utiliser la direction actuelle du mouvement
        var direction = _currentVelocity.normalized;
        var directionSource = "velocity";

        if (direction.sqrMagnitude < 0.01f)
        {
            // Fallback: direction vers la destination actuelle
            direction = (_destination - _controllerTransform.position).normalized;
            directionSource = "destination";
        }

        if (direction.sqrMagnitude < 0.01f)
        {
            LOG.Debug("TryExtendCurrentPathSync: No valid direction found");
            return false;
        }

        // Appliquer une déviation angulaire aléatoire
        var angleDeviation = UnityEngine.Random.Range(-maxAngleDeviation, maxAngleDeviation);
        direction = Quaternion.Euler(0f, angleDeviation, 0f) * direction;

        // Chercher une destination dans cette direction
        var distance = UnityEngine.Random.Range(minDistance, maxDistance);
        var targetPos = _controllerTransform.position + direction * distance;

        LOG.Debug($"TryExtendCurrentPathSync: source={directionSource}, angle={angleDeviation:F1}°, dist={distance:F1}m");

        if (!NavMesh.SamplePosition(targetPos, out var hit, 10f, _savedAreaMask))
        {
            LOG.Debug($"TryExtendCurrentPathSync: No NavMesh position found near {targetPos}");
            return false;
        }

        var path = new NavMeshPath();
        if (!_navAgent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete)
        {
            LOG.Debug($"TryExtendCurrentPathSync: Path calculation failed to {hit.position}");
            return false;
        }

        LOG.Debug($"TryExtendCurrentPathSync: SUCCESS - new destination at {hit.position}");
        _destination = hit.position;
        _navAgent.SetPath(path);
        return true;
    }

    private bool TrySetForwardDestination(Vector3 destination)
    {
        _precomputedForwardDestination = null;

        if (!DroidHelpers.IsNavAgentValid(_navAgent)) return false;

        var path = new NavMeshPath();
        if (!_navAgent.CalculatePath(destination, path) || path.status != NavMeshPathStatus.PathComplete)
        {
            LOG.Debug($"TrySetForwardDestination: Path recalculation failed to {destination}");
            return false;
        }

        LOG.Debug($"TrySetForwardDestination: SUCCESS - destination set to {destination}");
        _destination = destination;
        _navAgent.SetPath(path);
        return true;
    }

    /// <summary>
    ///     Met à jour le mouvement du droid.
    /// </summary>
    public void UpdateMovement(bool isMovementState)
    {
        if (!isMovementState || _navAgent == null || !_navAgent.hasPath)
        {
            _currentVelocity = Vector3.zero;
            return;
        }

        _currentVelocity = Vector3.Lerp(_currentVelocity, _navAgent.desiredVelocity, 5f * Time.deltaTime);

        if (_currentVelocity.magnitude > 0.01f && _charController != null)
        {
            var moveVector = _currentVelocity * Time.deltaTime;
            moveVector.y = -0.5f * Time.deltaTime;
            _charController.Move(moveVector);
        }
    }

    /// <summary>
    ///     Met à jour la rotation du droid.
    /// </summary>
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
        var levelPoint = SemiFunc.LevelPointGet(controllerPos, WhispralDebuffManager.SpawnDistanceMin,
                             WhispralDebuffManager.SpawnDistanceMax)
                         ?? SemiFunc.LevelPointGet(controllerPos, 0f, 20f);

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

        var path = new NavMeshPath();
        if (DroidHelpers.IsNavAgentValid(_navAgent) &&
            _navAgent.CalculatePath(hit.position, path) &&
            path.status == NavMeshPathStatus.PathComplete)
        {
            _precomputedDestination = hit.position;
        }

        _isPrecomputingDestination = false;
    }

    private bool TrySetDestination(Vector3 destination)
    {
        _precomputedDestination = null;

        if (!DroidHelpers.IsNavAgentValid(_navAgent)) return false;

        var path = new NavMeshPath();
        if (!_navAgent.CalculatePath(destination, path) || path.status != NavMeshPathStatus.PathComplete)
        {
            return false;
        }

        _destination = destination;
        _navAgent.SetPath(path);
        return true;
    }

    private bool TrySetDestinationSync()
    {
        var controllerPos = _controllerTransform != null ? _controllerTransform.position : transform.position;

        var levelPoint = SemiFunc.LevelPointGet(controllerPos, 5f, 15f)
                         ?? SemiFunc.LevelPointGet(controllerPos, 0f, 20f);

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

        _destination = hit.position;

        var path = new NavMeshPath();
        if (!_navAgent.CalculatePath(_destination, path) || path.status != NavMeshPathStatus.PathComplete)
        {
            return false;
        }

        _navAgent.SetPath(path);
        return true;
    }

    private IEnumerator PrecomputeForwardDestinationCoroutine(float minDistance, float maxDistance, float maxAngleDeviation)
    {
        _isPrecomputingForward = true;
        LOG.Debug("PrecomputeForward: Starting coroutine");

        if (_controllerTransform == null || !DroidHelpers.IsNavAgentValid(_navAgent))
        {
            LOG.Debug("PrecomputeForward: Invalid controller or NavAgent");
            _isPrecomputingForward = false;
            yield break;
        }

        // Capturer la direction actuelle
        var direction = _currentVelocity.normalized;
        var directionSource = "velocity";

        if (direction.sqrMagnitude < 0.01f)
        {
            direction = (_destination - _controllerTransform.position).normalized;
            directionSource = "destination";
        }

        if (direction.sqrMagnitude < 0.01f)
        {
            LOG.Debug("PrecomputeForward: No valid direction found");
            _isPrecomputingForward = false;
            yield break;
        }

        yield return null;

        // Appliquer une déviation angulaire aléatoire
        var angleDeviation = UnityEngine.Random.Range(-maxAngleDeviation, maxAngleDeviation);
        direction = Quaternion.Euler(0f, angleDeviation, 0f) * direction;

        // Calculer la position cible
        var distance = UnityEngine.Random.Range(minDistance, maxDistance);
        var currentPos = _controllerTransform.position;
        var targetPos = currentPos + direction * distance;

        LOG.Debug($"PrecomputeForward: source={directionSource}, angle={angleDeviation:F1}°, dist={distance:F1}m");

        yield return null;

        // Vérifier le NavMesh
        if (!NavMesh.SamplePosition(targetPos, out var hit, 10f, _savedAreaMask))
        {
            LOG.Debug($"PrecomputeForward: No NavMesh position found near {targetPos}");
            _isPrecomputingForward = false;
            yield break;
        }

        yield return null;

        // Vérifier le path
        var path = new NavMeshPath();
        if (!_navAgent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete)
        {
            LOG.Debug($"PrecomputeForward: Path calculation failed to {hit.position}");
            _isPrecomputingForward = false;
            yield break;
        }

        _precomputedForwardDestination = hit.position;
        LOG.Debug($"PrecomputeForward: SUCCESS - precomputed destination at {hit.position}");
        _isPrecomputingForward = false;
    }
}