using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using VepMod.VepFramework;

namespace VepMod.Enemies.Whispral;

/// <summary>
///     Hallucination basée sur le prefab LostDroid de WesleysEnemies.
///     Garde les visuels, animations et sons, mais simplifie le comportement
///     pour juste se balader sans attaquer.
/// </summary>
public sealed class HallucinationDroid : MonoBehaviour
{
    private const float MinRoamTime = 5f;
    private const float MaxRoamTime = 15f;
    private const float MinIdleTime = 10f;
    private const float MaxIdleTime = 15f;
    private const float WalkSpeed = 2.0f;

    private static readonly VepLogger LOG = VepLogger.Create<HallucinationDroid>(true);
    private Animator _animator;

    private State _currentState = State.Idle;
    private Vector3 _destination;

    private NavMeshAgent _navAgent;

    // Paramètres NavMesh sauvegardés du prefab original
    private int _savedAgentTypeID;
    private int _savedAreaMask = NavMesh.AllAreas;
    private float _stateTimer;
    private float _debugLogTimer;

    // Rotation manuelle (comme le LostDroid original)
    private Quaternion _targetRotation = Quaternion.identity;
    private const float RotationSpeed = 10f;

    // Flags d'animation (lus par LostDroidAnimationController)
    public bool IsWalking { get; private set; }
    public bool IsSprinting { get; private set; }
    public bool IsTurning { get; private set; }

    public PlayerAvatar SourcePlayer { get; private set; }

    private void Update()
    {
        if (_navAgent == null || !_navAgent.isOnNavMesh) return;

        _stateTimer -= Time.deltaTime;

        switch (_currentState)
        {
            case State.Idle:
                UpdateIdleState();
                break;
            case State.Wander:
                UpdateWanderState();
                break;
        }

        // Rotation manuelle basée sur la vélocité (comme le LostDroid original)
        UpdateRotation();

        // Mise à jour des animations
        UpdateAnimationFlags();

        // Forcer la localPosition de l'Animator à zéro (les animations peuvent la modifier)
        if (_animator != null && _animator.transform.localPosition != Vector3.zero)
        {
            _animator.transform.localPosition = Vector3.zero;
        }

        // Log périodique pour diagnostiquer le mouvement (toutes les 0.5 secondes)
        _debugLogTimer -= Time.deltaTime;
        if (_debugLogTimer <= 0f)
        {
            _debugLogTimer = 0.5f;
            LogPositionSync();
        }
    }

    /// <summary>
    ///     Rotation manuelle basée sur la vélocité (comme le LostDroid original).
    ///     Le droid tourne pour faire face à la direction de son mouvement.
    /// </summary>
    private void UpdateRotation()
    {
        // Seulement tourner si on bouge (vélocité significative)
        if (_navAgent.velocity.magnitude > 0.1f)
        {
            // La cible de rotation est la direction de la vélocité (comme le LostDroid original)
            var velocityDirection = _navAgent.velocity.normalized;
            _targetRotation = Quaternion.LookRotation(velocityDirection);
            // Garder seulement la rotation Y (pas de tilt)
            _targetRotation = Quaternion.Euler(0f, _targetRotation.eulerAngles.y, 0f);
        }

        // Rotation smooth vers la cible (équivalent du SpringQuaternion du jeu)
        transform.rotation = Quaternion.Slerp(transform.rotation, _targetRotation, RotationSpeed * Time.deltaTime);
    }

    private void LogPositionSync()
    {
        LOG.Debug($"=== MOVEMENT DEBUG (State={_currentState}) ===");
        LOG.Debug($"Position: {transform.position}, Rotation: {transform.eulerAngles.y:F1}°");
        LOG.Debug($"Velocity: {_navAgent.velocity} (magnitude: {_navAgent.velocity.magnitude:F2})");

        // Calculer l'angle entre la direction du droid et sa vélocité
        if (_navAgent.velocity.magnitude > 0.1f)
        {
            var velocityDir = _navAgent.velocity.normalized;
            var forwardDir = transform.forward;
            var angleDiff = Vector3.SignedAngle(forwardDir, velocityDir, Vector3.up);
            LOG.Debug($"Forward vs Velocity angle: {angleDiff:F1}° (sliding si > 45°)");
        }

        LOG.Debug($"Destination: {_destination}, RemainingDistance: {_navAgent.remainingDistance:F2}");
        LOG.Debug($"HasPath: {_navAgent.hasPath}, PathPending: {_navAgent.pathPending}, PathStatus: {_navAgent.pathStatus}");

        if (_navAgent.hasPath && _navAgent.path.corners.Length > 0)
        {
            LOG.Debug($"Path corners: {_navAgent.path.corners.Length}, Next corner: {_navAgent.steeringTarget}");
        }
    }



    private void OnDestroy()
    {
        LOG.Debug("HallucinationDroid destroyed");
    }

    /// <summary>
    ///     Crée une hallucination LostDroid à la position spécifiée.
    /// </summary>
    public static HallucinationDroid Create(PlayerAvatar sourcePlayer, Vector3 spawnPosition)
    {
        if (!LostDroidPrefabLoader.IsAvailable)
        {
            LOG.Warning("LostDroid prefab not available");
            return null;
        }

        // Instancier le prefab
        var instance = Instantiate(LostDroidPrefabLoader.LostDroidPrefab, spawnPosition, Quaternion.identity);
        if (instance == null)
        {
            LOG.Error("Failed to instantiate LostDroid prefab");
            return null;
        }

        instance.name = $"HallucinationDroid_{sourcePlayer.playerName}";

        // Configurer comme hallucination
        var hallucination = instance.AddComponent<HallucinationDroid>();
        hallucination.Initialize(sourcePlayer);

        return hallucination;
    }

    private void DisableEnemyComponents()
    {
        // Liste des composants à détruire complètement (réseau et IA qui peuvent interférer)
        var componentsToDestroy = new List<Component>();
        var componentsToDisable = new List<MonoBehaviour>();

        foreach (var component in GetComponentsInChildren<Component>(true))
        {
            if (component == null || component == this) continue;

            var typeName = component.GetType().Name;
            var fullTypeName = component.GetType().FullName ?? "";

            // Garder les composants essentiels
            if (component is Transform) continue;
            if (component is Animator) continue;
            if (component is Renderer) continue;
            if (component is MeshFilter) continue;
            if (component is SkinnedMeshRenderer) continue;

            // Détruire les composants Photon qui peuvent causer des téléportations
            if (fullTypeName.Contains("Photon") || typeName.Contains("Photon") ||
                typeName.Contains("PhotonView") || typeName.Contains("PhotonTransformView") ||
                typeName.Contains("PhotonRigidbodyView") || typeName.Contains("PhotonAnimatorView"))
            {
                componentsToDestroy.Add(component);
                LOG.Debug($"Marking for destruction (Photon): {typeName}");
                continue;
            }

            // Détruire les composants Enemy/AI qui peuvent interférer
            if (fullTypeName.Contains("Enemy") || fullTypeName.Contains("LostDroid") ||
                typeName.Contains("Enemy") || typeName.Contains("LostDroid") ||
                typeName == "EnemyParent" || typeName == "Enemy" ||
                (typeName.Contains("Controller") && !typeName.Contains("Animator")))
            {
                componentsToDestroy.Add(component);
                LOG.Debug($"Marking for destruction (Enemy/AI): {typeName}");
                continue;
            }

            // Désactiver les autres MonoBehaviours
            if (component is MonoBehaviour mb)
            {
                componentsToDisable.Add(mb);
            }
        }

        // Détruire les composants marqués en plusieurs passes (pour gérer les dépendances)
        var maxPasses = 5;
        var destroyedCount = 0;
        for (var pass = 0; pass < maxPasses; pass++)
        {
            var destroyedThisPass = 0;
            foreach (var component in componentsToDestroy)
            {
                if (component != null)
                {
                    try
                    {
                        DestroyImmediate(component);
                        destroyedThisPass++;
                        destroyedCount++;
                    }
                    catch
                    {
                        // Échec à cause d'une dépendance, on réessaiera à la prochaine passe
                    }
                }
            }

            if (destroyedThisPass == 0) break; // Plus rien à détruire
        }

        // Désactiver les autres composants
        foreach (var mb in componentsToDisable)
        {
            if (mb != null && mb != this)
            {
                mb.enabled = false;
            }
        }

        // Configurer le Rigidbody pour ne pas interférer
        var rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.interpolation = RigidbodyInterpolation.None;
        }

        // Garder les colliders mais les mettre en trigger
        foreach (var col in GetComponentsInChildren<Collider>())
        {
            col.isTrigger = true;
        }

        LOG.Debug(
            $"Component cleanup complete: {destroyedCount}/{componentsToDestroy.Count} destroyed, {componentsToDisable.Count} disabled");
    }

    private void Initialize(PlayerAvatar sourcePlayer)
    {
        SourcePlayer = sourcePlayer;

        // IMPORTANT: Sauvegarder les paramètres NavMesh AVANT de détruire les composants
        SaveNavMeshSettings();

        // Désactiver les composants du système Enemy
        DisableEnemyComponents();

        // Configurer la navigation
        SetupNavigation();

        // Récupérer les composants d'animation
        SetupAnimation();

        // Logger la hiérarchie pour diagnostic
        LogHierarchyDebug();

        // Démarrer en idle
        EnterIdleState();

        LOG.Debug($"HallucinationDroid initialized for {sourcePlayer.playerName}");
    }

    private void LogHierarchyDebug()
    {
        LOG.Debug("=== HIERARCHY DEBUG ===");
        LOG.Debug($"Root GameObject: {gameObject.name} at {transform.position}");

        // Logger tous les enfants avec leur position
        LogChildrenRecursive(transform, 0);

        // Logger spécifiquement l'Animator
        if (_animator != null)
        {
            LOG.Debug($"Animator is on: {_animator.gameObject.name}");
            LOG.Debug($"  Animator world pos: {_animator.transform.position}");
            LOG.Debug($"  Animator local pos: {_animator.transform.localPosition}");
            LOG.Debug($"  Parent of Animator: {(_animator.transform.parent != null ? _animator.transform.parent.name : "NONE")}");
        }

        // Logger les renderers
        var renderers = GetComponentsInChildren<Renderer>();
        foreach (var r in renderers)
        {
            LOG.Debug($"Renderer: {r.gameObject.name} at world pos {r.transform.position}, local pos {r.transform.localPosition}");
        }

        LOG.Debug("=== END HIERARCHY DEBUG ===");
    }

    private void LogChildrenRecursive(Transform parent, int depth)
    {
        var indent = new string(' ', depth * 2);
        foreach (Transform child in parent)
        {
            var hasRenderer = child.GetComponent<Renderer>() != null;
            var hasAnimator = child.GetComponent<Animator>() != null;
            var flags = (hasRenderer ? "[R]" : "") + (hasAnimator ? "[A]" : "");
            LOG.Debug($"{indent}- {child.name} {flags} localPos={child.localPosition}");
            LogChildrenRecursive(child, depth + 1);
        }
    }

    private void SaveNavMeshSettings()
    {
        // Chercher le NavMeshAgent sur l'objet ou ses enfants
        var existingAgent = GetComponentInChildren<NavMeshAgent>();
        if (existingAgent != null)
        {
            _savedAgentTypeID = existingAgent.agentTypeID;
            _savedAreaMask = existingAgent.areaMask;
            LOG.Debug($"Saved NavMeshAgent settings: agentTypeID={_savedAgentTypeID}, areaMask={_savedAreaMask}");
        }
        else
        {
            // Utiliser les valeurs du prefab LostDroid (extrait du fichier YAML Unity)
            // m_AgentTypeID: -334000983 est le type d'agent utilisé par les monstres du jeu
            _savedAgentTypeID = -334000983;
            _savedAreaMask = 1; // m_WalkableMask: 1
            LOG.Debug(
                $"Using hardcoded LostDroid NavMesh settings: agentTypeID={_savedAgentTypeID}, areaMask={_savedAreaMask}");
        }
    }

    private void SetupAnimation()
    {
        // Récupérer l'Animator
        _animator = GetComponentInChildren<Animator>();

        if (_animator != null)
        {
            _animator.enabled = true;
            // Désactiver le root motion pour que l'Animator ne contrôle pas la position
            _animator.applyRootMotion = false;
            LOG.Debug($"Animator found and enabled: {_animator.gameObject.name}");
            LOG.Debug(
                $"  RuntimeAnimatorController: {(_animator.runtimeAnimatorController != null ? _animator.runtimeAnimatorController.name : "NULL")}");
            LOG.Debug("  Root Motion disabled");
        }
        else
        {
            LOG.Warning("No Animator found on LostDroid prefab");
        }
    }

    private void SetupNavigation()
    {
        // Détruire tout NavMeshAgent existant (a déjà été copié dans SaveNavMeshSettings)
        _navAgent = GetComponent<NavMeshAgent>();
        if (_navAgent != null)
        {
            DestroyImmediate(_navAgent);
            _navAgent = null;
        }

        LOG.Debug($"Using saved NavMesh settings: agentTypeID={_savedAgentTypeID}, areaMask={_savedAreaMask}");

        // Trouver une position valide sur le NavMesh avec le bon type d'agent
        var validPosition = transform.position;
        var foundNavMesh = false;

        // Créer un filtre avec le bon type d'agent
        var filter = new NavMeshQueryFilter
        {
            agentTypeID = _savedAgentTypeID,
            areaMask = _savedAreaMask
        };

        // Essayer plusieurs distances de sample
        float[] sampleDistances = { 5f, 10f, 20f, 50f };
        foreach (var distance in sampleDistances)
        {
            if (NavMesh.SamplePosition(transform.position, out var hit, distance, filter))
            {
                validPosition = hit.position;
                foundNavMesh = true;
                LOG.Debug($"Found valid NavMesh position at distance {distance}: {validPosition}");
                break;
            }
        }

        if (!foundNavMesh)
        {
            LOG.Warning($"No NavMesh found near position {transform.position} for agentTypeID {_savedAgentTypeID}");
            return;
        }

        // Définir la position AVANT de créer l'agent
        transform.position = validPosition;

        // Créer un nouvel agent
        _navAgent = gameObject.AddComponent<NavMeshAgent>();

        // Désactiver temporairement pour configurer
        _navAgent.enabled = false;

        // CRITIQUE: Utiliser le même type d'agent que les monstres du jeu
        _navAgent.agentTypeID = _savedAgentTypeID;
        _navAgent.areaMask = _savedAreaMask;

        // Configurer les paramètres pour un mouvement fluide
        _navAgent.speed = WalkSpeed;
        _navAgent.acceleration = 2f;
        _navAgent.angularSpeed = 120f;
        _navAgent.stoppingDistance = 0.5f;
        _navAgent.autoBraking = true;
        _navAgent.autoRepath = true;

        // Position gérée par Unity, rotation gérée manuellement (comme le LostDroid original)
        _navAgent.updatePosition = true;
        _navAgent.updateRotation = false; // On gère la rotation nous-mêmes

        // Paramètres de collision
        _navAgent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
        _navAgent.avoidancePriority = 10;
        _navAgent.autoTraverseOffMeshLink = false; // Désactivé pour éviter les téléportations via OffMeshLinks

        // Taille de l'agent
        _navAgent.radius = 0.7f;
        _navAgent.height = 2f;
        _navAgent.baseOffset = 0f;

        // Warp à la position valide, puis activer
        _navAgent.Warp(validPosition);
        _navAgent.enabled = true;

        LOG.Debug(
            $"NavMeshAgent configured: agentTypeID={_savedAgentTypeID}, speed={_navAgent.speed}, radius={_navAgent.radius}");

        if (_navAgent.isOnNavMesh)
        {
            LOG.Debug("NavMeshAgent successfully placed on NavMesh");
        }
        else
        {
            LOG.Warning("NavMeshAgent still not on NavMesh after Warp");
        }
    }

    private enum State
    {
        Idle,
        Wander
    }

    #region State Machine

    private void EnterIdleState()
    {
        _currentState = State.Idle;
        _stateTimer = Random.Range(MinIdleTime, MaxIdleTime);

        IsWalking = false;
        IsSprinting = false;

        if (_navAgent != null && _navAgent.isOnNavMesh)
        {
            _navAgent.ResetPath();
        }
    }

    private void EnterWanderState()
    {
        _currentState = State.Wander;
        _stateTimer = Random.Range(MinRoamTime, MaxRoamTime);

        if (!TrySetRandomDestination())
        {
            LOG.Debug("EnterWanderState: Failed to set destination, returning to Idle");
            EnterIdleState();
            return;
        }

        IsWalking = true;
        IsSprinting = false;
        LOG.Debug($"EnterWanderState: Walking to {_destination}, hasPath={_navAgent.hasPath}, pathPending={_navAgent.pathPending}");
    }

    private void UpdateIdleState()
    {
        if (_stateTimer <= 0f)
        {
            LOG.Debug("Idle timer expired, attempting to wander...");
            EnterWanderState();
        }
    }

    private void UpdateWanderState()
    {
        // Attendre que le path soit calculé
        if (_navAgent.pathPending) return;

        // Vérifier si on a atteint la destination (pas de timer - on attend vraiment d'arriver)
        if (!_navAgent.hasPath || _navAgent.remainingDistance <= _navAgent.stoppingDistance)
        {
            LOG.Debug($"Destination reached! remainingDistance={_navAgent.remainingDistance:F2}, stoppingDistance={_navAgent.stoppingDistance}");
            EnterIdleState();
        }
    }

    private bool TrySetRandomDestination()
    {
        // Chercher un point proche (5-15m) pour éviter les longs trajets qui traversent des murs
        var levelPoint = SemiFunc.LevelPointGet(transform.position, 5f, 15f)
                         ?? SemiFunc.LevelPointGet(transform.position, 0f, 20f);

        if (levelPoint == null)
        {
            LOG.Debug("TrySetRandomDestination: No LevelPoint found");
            return false;
        }

        var targetPos = levelPoint.transform.position;

        // Utiliser le bon areaMask pour sampler la position
        if (!NavMesh.SamplePosition(targetPos, out var hit, 5f, _savedAreaMask))
        {
            LOG.Debug($"TrySetRandomDestination: NavMesh.SamplePosition failed at {targetPos}");
            return false;
        }

        // Vérifier que la destination est sur un sol valide (comme le LostDroid original ligne 1242)
        // Cela empêche le droid de se diriger vers des positions hors de la map
        if (!Physics.Raycast(hit.position + Vector3.up, Vector3.down, 5f, LayerMask.GetMask("Default")))
        {
            LOG.Debug($"TrySetRandomDestination: Position {hit.position} is not on valid ground");
            return false;
        }

        _destination = hit.position;

        // Vérifier que le chemin est valide avant de commencer à marcher
        var path = new NavMeshPath();
        if (!_navAgent.CalculatePath(_destination, path) || path.status != NavMeshPathStatus.PathComplete)
        {
            LOG.Debug($"TrySetRandomDestination: Path to {_destination} is not complete (status={path.status})");
            return false;
        }

        _navAgent.SetPath(path);
        LOG.Debug($"TrySetRandomDestination: Set destination to {_destination}, distance={Vector3.Distance(transform.position, _destination):F1}m");
        return true;
    }

    #endregion

    #region Animation

    private void UpdateAnimationFlags()
    {
        // Mettre à jour le turning basé sur l'angle entre rotation actuelle et cible
        // (comme TurnDroid() dans le LostDroid original - seuil de 7°)
        if (_currentState == State.Wander)
        {
            var angle = Quaternion.Angle(transform.rotation, _targetRotation);
            IsTurning = angle > 7f;
        }
        else
        {
            IsTurning = false;
        }

        // Appliquer les flags à l'animator directement
        if (_animator != null)
        {
            _animator.SetBool("isWalking", IsWalking);
            _animator.SetBool("isSprinting", IsSprinting);
            _animator.SetBool("isTurning", IsTurning);
            _animator.SetBool("stun", false);
        }
    }

    #endregion
}