using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations;
using VepMod.VepFramework;

namespace VepMod.Enemies.Whispral;

/// <summary>
///     Hallucination basée sur le prefab LostDroid de WesleysEnemies.
///     Garde les visuels, animations et sons, mais simplifie le comportement
///     pour juste se balader sans attaquer.
///
///     Architecture du prefab LostDroid:
///     - Root (Enemy - LostDroid) : ne bouge pas
///       - Enable : conteneur des visuels, doit suivre Controller
///         - Cube : mesh + Animator
///       - Rigidbody : physique originale (désactivée)
///       - Controller : NavMeshAgent ici, C'EST LUI QUI BOUGE
/// </summary>
public sealed class HallucinationDroid : MonoBehaviour
{
    private const float MinRoamTime = 5f;
    private const float MaxRoamTime = 15f;
    private const float MinIdleTime = 10f;
    private const float MaxIdleTime = 15f;
    private const float WalkSpeed = 2.0f;

    private static readonly VepLogger LOG = VepLogger.Create<HallucinationDroid>(true);

    // Le transform "Controller" qui bouge réellement (comme dans le jeu original)
    private Transform _controllerTransform;
    private NavMeshAgent _navAgent;
    private CharacterController _charController;

    // Le transform "Rigidbody" qui doit suivre Controller
    // (Cube a un ParentConstraint vers Rigidbody, donc si Rigidbody suit Controller, Cube suit aussi)
    private Transform _rigidbodyTransform;
    private Animator _animator;

    private State _currentState = State.Idle;
    private Vector3 _destination;

    // Paramètres NavMesh sauvegardés du prefab original
    private int _savedAgentTypeID;
    private int _savedAreaMask = NavMesh.AllAreas;
    private float _stateTimer;
    private float _debugLogTimer;

    // Mouvement manuel avec collision
    private Vector3 _currentVelocity;

    // Rotation manuelle
    private Quaternion _targetRotation = Quaternion.identity;
    private const float RotationSpeed = 10f;

    // Flags d'animation
    public bool IsWalking { get; private set; }
    public bool IsSprinting { get; private set; }
    public bool IsTurning { get; private set; }

    public PlayerAvatar SourcePlayer { get; private set; }

    private void Update()
    {
        if (_navAgent == null || !_navAgent.isOnNavMesh) return;
        if (_charController == null || _controllerTransform == null) return;

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

        // Mouvement du Controller (pas du root!)
        UpdateMovement();

        // Rotation du Controller
        UpdateRotation();

        // Synchroniser les visuels (Enable) pour suivre Controller
        SyncVisualsToController();

        // Mise à jour des animations
        UpdateAnimationFlags();

        // Synchroniser le NavMeshAgent avec la position du Controller
        SyncNavAgentPosition();

        // Log périodique
        _debugLogTimer -= Time.deltaTime;
        if (_debugLogTimer <= 0f)
        {
            _debugLogTimer = 0.5f;
            LogPositionSync();
        }
    }

    /// <summary>
    ///     Mouvement du Controller avec CharacterController pour les collisions.
    /// </summary>
    private void UpdateMovement()
    {
        if (_currentState != State.Wander || !_navAgent.hasPath)
        {
            _currentVelocity = Vector3.zero;
            return;
        }

        // Utiliser desiredVelocity du NavMeshAgent
        var desiredVelocity = _navAgent.desiredVelocity;

        // Smooth vers la vélocité désirée
        _currentVelocity = Vector3.Lerp(_currentVelocity, desiredVelocity, 5f * Time.deltaTime);

        // Appliquer le mouvement au Controller via CharacterController
        if (_currentVelocity.magnitude > 0.01f)
        {
            var moveVector = _currentVelocity * Time.deltaTime;
            moveVector.y = -0.5f * Time.deltaTime; // Gravité
            _charController.Move(moveVector);
        }
    }

    /// <summary>
    ///     Synchronise le transform Rigidbody pour suivre le Controller.
    ///     Cube a un ParentConstraint vers Rigidbody, donc il suivra automatiquement.
    /// </summary>
    private void SyncVisualsToController()
    {
        if (_rigidbodyTransform == null || _controllerTransform == null) return;

        // Le Rigidbody doit suivre le Controller (comme EnemyRigidbody.followTarget)
        _rigidbodyTransform.position = _controllerTransform.position;
        _rigidbodyTransform.rotation = _controllerTransform.rotation;

        // Forcer l'animator à localPosition zero (les animations peuvent le bouger)
        if (_animator != null && _animator.transform.localPosition != Vector3.zero)
        {
            _animator.transform.localPosition = Vector3.zero;
        }
    }

    /// <summary>
    ///     Synchronise le NavMeshAgent avec la position du Controller.
    /// </summary>
    private void SyncNavAgentPosition()
    {
        if (_navAgent == null || _controllerTransform == null) return;

        // Utiliser la position du Controller, pas du root
        var controllerPos = _controllerTransform.position;

        if (NavMesh.SamplePosition(controllerPos, out var hit, 2f, _savedAreaMask))
        {
            _navAgent.nextPosition = hit.position;

            var deviation = Vector3.Distance(controllerPos, hit.position);
            if (deviation > 0.5f)
            {
                LOG.Debug($"Controller deviation from NavMesh: {deviation:F2}m");
                if (_currentState == State.Wander && _navAgent.hasPath)
                {
                    _navAgent.SetDestination(_destination);
                }
            }
        }
        else
        {
            LOG.Warning($"Controller position {controllerPos} is off NavMesh!");
            EnterIdleState();
        }
    }

    /// <summary>
    ///     Rotation du Controller basée sur la vélocité.
    /// </summary>
    private void UpdateRotation()
    {
        if (_controllerTransform == null) return;

        if (_currentVelocity.magnitude > 0.1f)
        {
            var velocityDirection = _currentVelocity.normalized;
            velocityDirection.y = 0f;
            if (velocityDirection.sqrMagnitude > 0.01f)
            {
                _targetRotation = Quaternion.LookRotation(velocityDirection);
            }
        }

        // Appliquer la rotation au Controller
        _controllerTransform.rotation = Quaternion.Slerp(
            _controllerTransform.rotation,
            _targetRotation,
            RotationSpeed * Time.deltaTime);
    }

    private void LogPositionSync()
    {
        LOG.Debug($"=== MOVEMENT DEBUG (State={_currentState}) ===");
        LOG.Debug($"Root Position: {transform.position}");

        if (_controllerTransform != null)
        {
            LOG.Debug($"Controller Position: {_controllerTransform.position}, Rotation: {_controllerTransform.eulerAngles.y:F1}°");
        }

        if (_rigidbodyTransform != null)
        {
            LOG.Debug($"Rigidbody Transform Position: {_rigidbodyTransform.position}");
        }

        if (_animator != null)
        {
            LOG.Debug($"Animator (Cube) Position: {_animator.transform.position}, localPos: {_animator.transform.localPosition}");
        }

        LOG.Debug($"Velocity: {_currentVelocity} (magnitude: {_currentVelocity.magnitude:F2})");

        if (_charController != null)
        {
            LOG.Debug($"CharController grounded: {_charController.isGrounded}");
        }

        LOG.Debug($"Destination: {_destination}");
        if (_controllerTransform != null)
        {
            LOG.Debug($"Distance to dest: {Vector3.Distance(_controllerTransform.position, _destination):F2}");
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

        var instance = Instantiate(LostDroidPrefabLoader.LostDroidPrefab, spawnPosition, Quaternion.identity);
        if (instance == null)
        {
            LOG.Error("Failed to instantiate LostDroid prefab");
            return null;
        }

        instance.name = $"HallucinationDroid_{sourcePlayer.playerName}";

        var hallucination = instance.AddComponent<HallucinationDroid>();
        hallucination.Initialize(sourcePlayer);

        return hallucination;
    }

    private void DisableEnemyComponents()
    {
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
            // IMPORTANT: Garder le NavMeshAgent sur Controller!
            if (component is NavMeshAgent) continue;

            // Détruire les composants Photon
            if (fullTypeName.Contains("Photon") || typeName.Contains("Photon"))
            {
                componentsToDestroy.Add(component);
                LOG.Debug($"Marking for destruction (Photon): {typeName}");
                continue;
            }

            // Détruire les composants Enemy/AI (sauf NavMeshAgent)
            if (fullTypeName.Contains("Enemy") || fullTypeName.Contains("LostDroid") ||
                typeName.Contains("Enemy") || typeName.Contains("LostDroid") ||
                typeName == "EnemyParent" || typeName == "Enemy")
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

        // Détruire en plusieurs passes
        var destroyedCount = 0;
        for (var pass = 0; pass < 5; pass++)
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
                        // Réessayer à la prochaine passe
                    }
                }
            }
            if (destroyedThisPass == 0) break;
        }

        // Désactiver les autres
        foreach (var mb in componentsToDisable)
        {
            if (mb != null && mb != this)
            {
                mb.enabled = false;
            }
        }

        // Désactiver tous les Rigidbody
        foreach (var rb in GetComponentsInChildren<Rigidbody>())
        {
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // Mettre les colliders en trigger (sauf celui du CharacterController qu'on va créer)
        foreach (var col in GetComponentsInChildren<Collider>())
        {
            col.isTrigger = true;
        }

        LOG.Debug($"Component cleanup: {destroyedCount} destroyed, {componentsToDisable.Count} disabled");
    }

    private void Initialize(PlayerAvatar sourcePlayer)
    {
        SourcePlayer = sourcePlayer;

        // Trouver les transforms importants AVANT de toucher aux composants
        FindCriticalTransforms();

        // Sauvegarder les paramètres NavMesh
        SaveNavMeshSettings();

        // Désactiver les composants du système Enemy
        DisableEnemyComponents();

        // Configurer la navigation sur le Controller
        SetupNavigation();

        // Récupérer l'animator
        SetupAnimation();

        // Logger la hiérarchie
        LogHierarchyDebug();

        // Démarrer en idle
        EnterIdleState();

        LOG.Debug($"HallucinationDroid initialized for {sourcePlayer.playerName}");
    }

    /// <summary>
    ///     Trouve les transforms critiques: Controller (mouvement) et Enable (visuels).
    /// </summary>
    private void FindCriticalTransforms()
    {
        // Chercher le Controller (c'est là que le NavMeshAgent original est)
        _controllerTransform = transform.Find("Controller");
        if (_controllerTransform != null)
        {
            LOG.Debug($"Found Controller transform at {_controllerTransform.position}");
        }
        else
        {
            // Chercher récursivement
            foreach (Transform child in GetComponentsInChildren<Transform>())
            {
                if (child.name == "Controller")
                {
                    _controllerTransform = child;
                    LOG.Debug($"Found Controller transform (recursive) at {_controllerTransform.position}");
                    break;
                }
            }
        }

        if (_controllerTransform == null)
        {
            LOG.Warning("Controller transform not found! Creating one...");
            var controllerGO = new GameObject("Controller");
            controllerGO.transform.SetParent(transform);
            controllerGO.transform.localPosition = Vector3.zero;
            _controllerTransform = controllerGO.transform;
        }

        // Chercher Rigidbody transform (Cube a un ParentConstraint vers lui)
        _rigidbodyTransform = transform.Find("Rigidbody");
        if (_rigidbodyTransform != null)
        {
            LOG.Debug($"Found Rigidbody transform at {_rigidbodyTransform.position}");
        }
        else
        {
            foreach (Transform child in GetComponentsInChildren<Transform>())
            {
                if (child.name == "Rigidbody")
                {
                    _rigidbodyTransform = child;
                    LOG.Debug($"Found Rigidbody transform (recursive) at {_rigidbodyTransform.position}");
                    break;
                }
            }
        }

        if (_rigidbodyTransform == null)
        {
            LOG.Warning("Rigidbody transform not found!");
        }
    }

    private void LogHierarchyDebug()
    {
        LOG.Debug("=== HIERARCHY DEBUG ===");
        LOG.Debug($"Root: {gameObject.name} at {transform.position}");
        LOG.Debug($"Controller: {(_controllerTransform != null ? _controllerTransform.name : "NULL")} at {(_controllerTransform != null ? _controllerTransform.position.ToString() : "N/A")}");
        LOG.Debug($"Rigidbody: {(_rigidbodyTransform != null ? _rigidbodyTransform.name : "NULL")} at {(_rigidbodyTransform != null ? _rigidbodyTransform.position.ToString() : "N/A")}");
        LOG.Debug($"Animator: {(_animator != null ? _animator.gameObject.name : "NULL")}");

        LogChildrenRecursive(transform, 0);
        LOG.Debug("=== END HIERARCHY DEBUG ===");
    }

    private void LogChildrenRecursive(Transform parent, int depth)
    {
        var indent = new string(' ', depth * 2);
        foreach (Transform child in parent)
        {
            var hasRenderer = child.GetComponent<Renderer>() != null;
            var hasAnimator = child.GetComponent<Animator>() != null;
            var hasNavAgent = child.GetComponent<NavMeshAgent>() != null;
            var hasCharCtrl = child.GetComponent<CharacterController>() != null;
            var flags = (hasRenderer ? "[R]" : "") + (hasAnimator ? "[A]" : "") +
                        (hasNavAgent ? "[Nav]" : "") + (hasCharCtrl ? "[CC]" : "");
            LOG.Debug($"{indent}- {child.name} {flags} pos={child.position}");
            LogChildrenRecursive(child, depth + 1);
        }
    }

    private void SaveNavMeshSettings()
    {
        // Chercher le NavMeshAgent existant (devrait être sur Controller)
        NavMeshAgent existingAgent = null;

        if (_controllerTransform != null)
        {
            existingAgent = _controllerTransform.GetComponent<NavMeshAgent>();
        }

        if (existingAgent == null)
        {
            existingAgent = GetComponentInChildren<NavMeshAgent>();
        }

        if (existingAgent != null)
        {
            _savedAgentTypeID = existingAgent.agentTypeID;
            _savedAreaMask = existingAgent.areaMask;
            LOG.Debug($"Saved NavMeshAgent settings from {existingAgent.gameObject.name}: agentTypeID={_savedAgentTypeID}, areaMask={_savedAreaMask}");
        }
        else
        {
            _savedAgentTypeID = -334000983;
            _savedAreaMask = 1;
            LOG.Debug($"Using hardcoded LostDroid NavMesh settings");
        }
    }

    private void SetupAnimation()
    {
        _animator = GetComponentInChildren<Animator>();

        if (_animator != null)
        {
            _animator.enabled = true;
            _animator.applyRootMotion = false;
            LOG.Debug($"Animator found on {_animator.gameObject.name}, root motion disabled");

            // Vérifier le ParentConstraint sur Cube
            var parentConstraint = _animator.GetComponent<ParentConstraint>();
            if (parentConstraint != null)
            {
                LOG.Debug($"ParentConstraint found on {_animator.gameObject.name}");
                LOG.Debug($"  constraintActive: {parentConstraint.constraintActive}");
                LOG.Debug($"  sourceCount: {parentConstraint.sourceCount}");

                // S'assurer que le constraint est actif
                if (!parentConstraint.constraintActive)
                {
                    parentConstraint.constraintActive = true;
                    LOG.Debug("  -> Activated ParentConstraint");
                }

                // Logger les sources
                for (int i = 0; i < parentConstraint.sourceCount; i++)
                {
                    var source = parentConstraint.GetSource(i);
                    LOG.Debug($"  Source {i}: {(source.sourceTransform != null ? source.sourceTransform.name : "NULL")}, weight={source.weight}");
                }
            }
            else
            {
                LOG.Warning($"No ParentConstraint found on {_animator.gameObject.name}!");
            }
        }
        else
        {
            LOG.Warning("No Animator found");
        }
    }

    private void SetupNavigation()
    {
        if (_controllerTransform == null)
        {
            LOG.Error("Cannot setup navigation: Controller transform is null!");
            return;
        }

        // Récupérer ou créer le NavMeshAgent sur le Controller
        _navAgent = _controllerTransform.GetComponent<NavMeshAgent>();

        if (_navAgent == null)
        {
            LOG.Debug("No NavMeshAgent on Controller, creating one...");
            _navAgent = _controllerTransform.gameObject.AddComponent<NavMeshAgent>();
        }

        // Trouver une position valide sur le NavMesh
        var startPos = _controllerTransform.position;
        var validPosition = startPos;
        var foundNavMesh = false;

        var filter = new NavMeshQueryFilter
        {
            agentTypeID = _savedAgentTypeID,
            areaMask = _savedAreaMask
        };

        float[] sampleDistances = { 5f, 10f, 20f, 50f };
        foreach (var distance in sampleDistances)
        {
            if (NavMesh.SamplePosition(startPos, out var hit, distance, filter))
            {
                validPosition = hit.position;
                foundNavMesh = true;
                LOG.Debug($"Found valid NavMesh position at distance {distance}: {validPosition}");
                break;
            }
        }

        if (!foundNavMesh)
        {
            LOG.Warning($"No NavMesh found near {startPos}");
            return;
        }

        // Désactiver pour configurer
        _navAgent.enabled = false;

        // Configurer
        _navAgent.agentTypeID = _savedAgentTypeID;
        _navAgent.areaMask = _savedAreaMask;
        _navAgent.speed = WalkSpeed;
        _navAgent.acceleration = 8f;
        _navAgent.angularSpeed = 120f;
        _navAgent.stoppingDistance = 0.5f;
        _navAgent.autoBraking = true;
        _navAgent.autoRepath = true;

        // IMPORTANT: Le NavMeshAgent ne contrôle PAS la position - on utilise CharacterController
        _navAgent.updatePosition = false;
        _navAgent.updateRotation = false;

        _navAgent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
        _navAgent.radius = 0.7f;
        _navAgent.height = 2f;
        _navAgent.baseOffset = 0f;

        // Positionner le Controller sur le NavMesh
        _controllerTransform.position = validPosition;
        _navAgent.Warp(validPosition);
        _navAgent.enabled = true;

        // Créer le CharacterController sur le Controller
        SetupCharacterController();

        LOG.Debug($"NavMeshAgent configured on Controller, isOnNavMesh={_navAgent.isOnNavMesh}");
    }

    private void SetupCharacterController()
    {
        if (_controllerTransform == null) return;

        // Supprimer tout CharacterController existant sur Controller
        var existingCC = _controllerTransform.GetComponent<CharacterController>();
        if (existingCC != null)
        {
            DestroyImmediate(existingCC);
        }

        // Créer le CharacterController sur le Controller
        _charController = _controllerTransform.gameObject.AddComponent<CharacterController>();
        _charController.height = 2f;
        _charController.radius = 0.5f;
        _charController.center = new Vector3(0f, 1f, 0f);
        _charController.slopeLimit = 45f;
        _charController.stepOffset = 0.3f;
        _charController.skinWidth = 0.08f;
        _charController.minMoveDistance = 0.001f;

        LOG.Debug($"CharacterController created on Controller");
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
        LOG.Debug($"EnterWanderState: Walking to {_destination}");
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
        if (_navAgent.pathPending) return;

        // Utiliser la position du Controller pour calculer la distance
        var controllerPos = _controllerTransform != null ? _controllerTransform.position : transform.position;
        var distanceToDestination = Vector3.Distance(controllerPos, _destination);

        if (!_navAgent.hasPath || distanceToDestination <= _navAgent.stoppingDistance)
        {
            LOG.Debug($"Destination reached! distance={distanceToDestination:F2}");
            EnterIdleState();
        }
    }

    private bool TrySetRandomDestination()
    {
        var controllerPos = _controllerTransform != null ? _controllerTransform.position : transform.position;

        var levelPoint = SemiFunc.LevelPointGet(controllerPos, 5f, 15f)
                         ?? SemiFunc.LevelPointGet(controllerPos, 0f, 20f);

        if (levelPoint == null)
        {
            LOG.Debug("TrySetRandomDestination: No LevelPoint found");
            return false;
        }

        var targetPos = levelPoint.transform.position;

        if (!NavMesh.SamplePosition(targetPos, out var hit, 5f, _savedAreaMask))
        {
            LOG.Debug($"TrySetRandomDestination: NavMesh.SamplePosition failed at {targetPos}");
            return false;
        }

        if (!Physics.Raycast(hit.position + Vector3.up, Vector3.down, 5f, LayerMask.GetMask("Default")))
        {
            LOG.Debug($"TrySetRandomDestination: Position {hit.position} is not on valid ground");
            return false;
        }

        _destination = hit.position;

        var path = new NavMeshPath();
        if (!_navAgent.CalculatePath(_destination, path) || path.status != NavMeshPathStatus.PathComplete)
        {
            LOG.Debug($"TrySetRandomDestination: Path to {_destination} is not complete");
            return false;
        }

        _navAgent.SetPath(path);
        LOG.Debug($"TrySetRandomDestination: Set destination to {_destination}");
        return true;
    }

    #endregion

    #region Animation

    private void UpdateAnimationFlags()
    {
        if (_currentState == State.Wander && _controllerTransform != null)
        {
            var angle = Quaternion.Angle(_controllerTransform.rotation, _targetRotation);
            IsTurning = angle > 7f;
        }
        else
        {
            IsTurning = false;
        }

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
