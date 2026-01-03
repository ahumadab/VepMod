using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations;
using VepMod.VepFramework;
using VepMod.VepFramework.Structures.FSM;

namespace VepMod.Enemies.Whispral;

/// <summary>
///     Hallucination basée sur le prefab LostDroid de WesleysEnemies.
///     Se balade sans attaquer, utilise l'architecture originale:
///     - Controller: NavMeshAgent + CharacterController (mouvement)
///     - Rigidbody: transform suivi par Cube via ParentConstraint
///     - Cube: visuels + Animator
/// </summary>
public sealed partial class HallucinationDroid : StateMachineComponent<HallucinationDroid, HallucinationDroid.StateId>
{
    internal const float WalkSpeed = 2f;
    internal const float SprintSpeed = 5f;
    internal const float SprintChance = 0.5f;

    // Stalk settings (internal for FSM states)
    internal const float StalkMinDistance = 15f;
    internal const float StalkChance = 0.2f;
    internal const float StalkArrivalDistance = 5f;
    internal const float StalkMinKeepDistance = 3f;
    internal const float StalkFleeDistance = 20f;
    internal const float StalkStareBeforeFlee = 2f;

    private readonly Materials.MaterialTrigger _materialTrigger = new();

    // Components (animator is internal for FSM states)
    internal Animator Animator;
    private DroidAnimationController _animController;
    private CharacterController _charController;
    private DroidMovementController _movement;
    private DroidNameplate _nameplateController;
    private NavMeshAgent _navAgent;
    private Transform _rigidbodyTransform;

    // NavMesh settings (saved for initialization)
    private int _savedAgentTypeID;
    private int _savedAreaMask = NavMesh.AllAreas;

    public bool IsWalking { get; set; }
    public bool IsSprinting { get; set; }
    public bool IsTurning { get; private set; }
    public bool HasChangedMovementState { get; set; }
    public PlayerAvatar SourcePlayer { get; private set; }
    public Transform ControllerTransform { get; private set; }
    public DroidMovementController Movement => _movement;
    public bool IsPlayerLookingAtMe => _animController?.IsPlayerLookingAtMe ?? false;

    protected override StateId DefaultState => StateId.Idle;

    protected override void Awake()
    {
        // Ne pas appeler base.Awake() ici - on initialise la FSM dans Initialize()
    }

    protected override void Update()
    {
        if (_movement == null || _navAgent == null || !_navAgent.isOnNavMesh) return;
        if (ControllerTransform == null) return;

        // Update FSM
        fsm?.Update();

        // Update movement
        var currentState = fsm?.CurrentStateStateId;
        var isMovementState = currentState.HasValue && DroidHelpers.IsMovementState(currentState.Value);

        _movement.UpdateMovement(isMovementState);
        _movement.UpdateRotation();
        _movement.SyncVisualsToController(Animator?.transform);
        UpdateAnimationFlags();
        _movement.SyncNavAgentPosition(isMovementState);
    }


    #region Animation

    private void UpdateAnimationFlags()
    {
        var currentState = fsm?.CurrentStateStateId;
        var isMovementState = currentState.HasValue && DroidHelpers.IsMovementState(currentState.Value);

        if (isMovementState && ControllerTransform != null && _movement != null)
        {
            var angle = Quaternion.Angle(ControllerTransform.rotation, _movement.TargetRotation);
            IsTurning = angle > 7f;
        }
        else
        {
            IsTurning = false;
        }

        if (Animator != null)
        {
            Animator.SetBool("isWalking", IsWalking);
            Animator.SetBool("isSprinting", IsSprinting);
            Animator.SetBool("isTurning", IsTurning);
            Animator.SetBool("stun", false);
        }
    }

    #endregion

    #region Movement (delegates to DroidMovementController)

    public bool TrySetRandomDestination() => _movement != null && _movement.TrySetRandomDestination();

    public void StartPrecomputeDestination() => _movement?.StartPrecomputeDestination();

    public bool HasPrecomputedDestination => _movement?.HasPrecomputedDestination ?? false;

    public void ResetPath() => _movement?.ResetPath();

    public bool HasReachedDestination() => _movement?.HasReachedDestination() ?? true;

    public float GetDistanceToPlayer() => _movement?.GetDistanceToPlayer() ?? float.MaxValue;

    public bool TrySetDestinationToPlayer() => _movement != null && _movement.TrySetDestinationToPlayer();

    public bool TrySetFleeDestination() => _movement != null && _movement.TrySetFleeDestination(StalkFleeDistance);

    public void LookAtPlayer() => _movement?.LookAtPlayer();

    public void SetSpeed(float speed) => _movement?.SetSpeed(speed);

    #endregion

    #region Audio

    /// <summary>
    ///     Joue le son enregistré du joueur source à la position du Controller.
    ///     Utilise le WhispralMimics du joueur LOCAL (qui a les fichiers audio stockés).
    /// </summary>
    public void PlayVoice(bool applyFilter = false)
    {
        if (SourcePlayer == null || ControllerTransform == null) return;

        var localPlayer = DroidHelpers.GetLocalPlayer();
        if (localPlayer == null) return;

        var mimics = localPlayer.GetComponent<WhispralMimics>();
        if (mimics == null)
        {
            LOG.Warning("WhispralMimics not found on local player");
            return;
        }

        mimics.PlayAudioAtTransform(ControllerTransform, SourcePlayer.playerName, applyFilter);
    }

    #endregion

    #region Factory & Initialization

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

    private void Initialize(PlayerAvatar sourcePlayer)
    {
        SourcePlayer = sourcePlayer;

        FindCriticalTransforms();
        SaveNavMeshSettings();
        DisableEnemyComponents();
        SetupNavigation();
        SetupMovementController();
        SetupAnimator();
        SetupAnimationController();
        ApplyPlayerColor();
        SetupNameplate();
        InitializeFSM();

        LOG.Info($"HallucinationDroid created for {sourcePlayer.playerName} at {ControllerTransform?.position}");
    }

    private void SetupMovementController()
    {
        if (ControllerTransform == null) return;

        _movement = gameObject.AddComponent<DroidMovementController>();
        _movement.Initialize(ControllerTransform, _rigidbodyTransform, _navAgent, _charController, _savedAreaMask);
        _movement.OnNavMeshError += HandleNavMeshError;
    }

    private void HandleNavMeshError()
    {
        fsm.NextStateStateId = StateId.Idle;
    }

    private void SetupAnimationController()
    {
        _animController = gameObject.AddComponent<DroidAnimationController>();
        _animController.Initialize(ControllerTransform);

        // Setup head transform for talking animation
        var headTopTransform = DroidHelpers.FindChildByName(transform, "code_head_top");
        if (headTopTransform != null)
        {
            _animController.SetupTalking(headTopTransform);
        }

        // Setup eyelids for angry eyes
        SetupEyelidsForAnimController();
    }

    private void SetupEyelidsForAnimController()
    {
        try
        {
            var eyeLeft = DroidHelpers.FindChildByName(transform, "ANIM EYE LEFT");
            var eyeRight = DroidHelpers.FindChildByName(transform, "ANIM EYE RIGHT");

            if (eyeLeft == null || eyeRight == null)
            {
                LOG.Warning("ANIM EYE LEFT/RIGHT not found for angry eyes effect");
                return;
            }

            var playerSource = FindEyelidSource();
            if (playerSource == null)
            {
                LOG.Warning("No PlayerAvatar found to copy eyelid structure");
                return;
            }

            var codeEyeLeft = DroidHelpers.FindChildByName(transform, "code_eye_left") ?? eyeLeft;
            var codeEyeRight = DroidHelpers.FindChildByName(transform, "code_eye_right") ?? eyeRight;

            var eyelidsLeft = SetupEyeFromPlayer(codeEyeLeft, playerSource, "LEFT",
                out var leftUpperX, out var leftUpperZ, out var leftLowerX, out _);

            var eyelidsRight = SetupEyeFromPlayer(codeEyeRight, playerSource, "RIGHT",
                out var rightUpperX, out var rightUpperZ, out var rightLowerX, out _);

            if (eyelidsLeft != null) eyelidsLeft.SetActive(false);
            if (eyelidsRight != null) eyelidsRight.SetActive(false);

            _animController.SetupEyelids(
                eyelidsLeft, eyelidsRight,
                leftUpperX, leftUpperZ, leftLowerX,
                rightUpperX, rightUpperZ, rightLowerX);

            LOG.Info($"Angry eyes setup: left={eyelidsLeft != null}, right={eyelidsRight != null}");
        }
        catch (Exception ex)
        {
            LOG.Error($"SetupEyelidsForAnimController: Exception - {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void LateUpdate()
    {
        _nameplateController?.UpdateNameplate();
        _animController?.UpdateAnimations();
    }

    private void OnDestroy()
    {
        if (_movement != null)
        {
            _movement.OnNavMeshError -= HandleNavMeshError;
        }
    }

    private Material GetDroidBodyMaterial()
    {
        // Chercher un renderer sur le corps du droid (pas les yeux)
        foreach (var renderer in GetComponentsInChildren<MeshRenderer>(true))
        {
            var name = renderer.gameObject.name.ToLower();
            // Éviter les yeux et pupilles
            if (name.Contains("eye") || name.Contains("pupil") || name.Contains("eyelid")) continue;
            if (renderer.material != null)
            {
                return renderer.material;
            }
        }

        return null;
    }

    private Transform FindEyelidSource()
    {
        // Chercher PlayerAvatarVisuals dans la scène (c'est là que sont les paupières)
        var playerVisuals = FindObjectsOfType<PlayerAvatarVisuals>();

        foreach (var visuals in playerVisuals)
        {
            // Chercher EYELIDS LEFT pour vérifier que c'est une source valide
            foreach (var child in visuals.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == "EYELIDS LEFT")
                {
                    return visuals.transform;
                }
            }
        }

        LOG.Warning("No PlayerAvatarVisuals with EYELIDS LEFT found");
        return null;
    }

    /// <summary>
    ///     Configure les paupières pour un oeil du droid en copiant depuis le player.
    /// </summary>
    private GameObject SetupEyeFromPlayer(
        Transform droidEye,
        Transform playerSource,
        string side,
        out Transform upperRotationX,
        out Transform upperRotationZ,
        out Transform lowerRotationX,
        out Transform lowerRotationZ)
    {
        upperRotationX = null;
        upperRotationZ = null;
        lowerRotationX = null;
        lowerRotationZ = null;

        var eyelidsName = $"EYELIDS {side}";

        // Trouver EYELIDS sur le player source
        Transform playerEyelids = null;
        foreach (var child in playerSource.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == eyelidsName)
            {
                playerEyelids = child;
                break;
            }
        }

        if (playerEyelids == null)
        {
            LOG.Warning($"{eyelidsName} not found on player source");
            return null;
        }

        // Vérifier si EYELIDS existe déjà sur le droid
        Transform droidEyelids = null;
        foreach (Transform child in droidEye)
        {
            if (child.name == eyelidsName)
            {
                droidEyelids = child;
                break;
            }
        }

        if (droidEyelids == null)
        {
            // Copier toute la hiérarchie EYELIDS du player vers le droid
            droidEyelids = CopyTransformHierarchy(playerEyelids, droidEye);

            // Agrandir les paupières pour s'adapter aux yeux du droid
            droidEyelids.localScale = Vector3.one * 1.3f;

            // Appliquer le material du droid aux paupières
            var droidBodyMaterial = GetDroidBodyMaterial();
            if (droidBodyMaterial != null)
            {
                foreach (var r in droidEyelids.GetComponentsInChildren<MeshRenderer>(true))
                {
                    r.material = droidBodyMaterial;
                }
            }
        }

        // Trouver les transforms de rotation dans la hiérarchie
        foreach (var child in droidEyelids.GetComponentsInChildren<Transform>(true))
        {
            switch (child.name)
            {
                case "eyelid_upper":
                    upperRotationX = child;
                    break;
                case "eyelid_upper_rotation":
                    upperRotationZ = child;
                    break;
                case "eyelid_lower":
                    lowerRotationX = child;
                    break;
                case "eyelid_lower_rotation":
                    lowerRotationZ = child;
                    break;
            }
        }

        return droidEyelids.gameObject;
    }

    private Transform CopyTransformHierarchy(Transform source, Transform newParent)
    {
        // Créer un nouveau GameObject avec le même nom
        var copy = new GameObject(source.name);
        copy.transform.SetParent(newParent, false);
        copy.transform.localPosition = source.localPosition;
        copy.transform.localRotation = source.localRotation;
        copy.transform.localScale = source.localScale;

        // Copier les MeshFilter et MeshRenderer si présents
        var sourceMeshFilter = source.GetComponent<MeshFilter>();
        var sourceMeshRenderer = source.GetComponent<MeshRenderer>();

        if (sourceMeshFilter != null && sourceMeshRenderer != null)
        {
            var copyMeshFilter = copy.AddComponent<MeshFilter>();
            var copyMeshRenderer = copy.AddComponent<MeshRenderer>();

            copyMeshFilter.sharedMesh = sourceMeshFilter.sharedMesh;
            copyMeshRenderer.sharedMaterials = sourceMeshRenderer.sharedMaterials;
            copyMeshRenderer.shadowCastingMode = sourceMeshRenderer.shadowCastingMode;
            copyMeshRenderer.receiveShadows = sourceMeshRenderer.receiveShadows;
        }

        // Récursivement copier les enfants
        foreach (Transform child in source)
        {
            CopyTransformHierarchy(child, copy.transform);
        }

        return copy.transform;
    }

    /// <summary>
    ///     Appelé par les animation events pour jouer un son de pas.
    /// </summary>
    public void PlayMediumFootstep()
    {
        if (ControllerTransform == null) return;
        // LOG.Debug($"PlayMediumFootstep -> calling droid at {ControllerTransform.position}");
        // Position légèrement au-dessus du sol pour que le raycast trouve le matériau
        var footPosition = ControllerTransform.position + Vector3.up * 0.1f;
        Materials.Instance.Impulse(
            footPosition,
            Vector3.down,
            Materials.SoundType.Medium,
            true,
            true,
            _materialTrigger,
            Materials.HostType.OtherPlayer);
    }

    private void SetupNameplate()
    {
        _nameplateController = gameObject.AddComponent<DroidNameplate>();
        _nameplateController.Initialize(ControllerTransform, SourcePlayer);
    }

    private void ApplyPlayerColor()
    {
        if (SourcePlayer == null) return;

        var colorIndex = StatsManager.instance.GetPlayerColor(SourcePlayer.steamID);
        if (colorIndex < 0 || colorIndex >= AssetManager.instance.playerColors.Count) return;

        var color = AssetManager.instance.playerColors[colorIndex];

        // Trouver le Cube qui contient les visuels
        var cube = transform.Find("Rigidbody/Cube") ?? transform.Find("Cube");
        if (cube == null)
        {
            foreach (var child in GetComponentsInChildren<Transform>())
            {
                if (child.name == "Cube")
                {
                    cube = child;
                    break;
                }
            }
        }

        if (cube == null)
        {
            LOG.Warning("Cube transform not found for color application");
            return;
        }

        // Appliquer la couleur à tous les renderers (sauf yeux, pupilles et health shadow)
        foreach (var renderer in cube.GetComponentsInChildren<Renderer>())
        {
            var name = renderer.gameObject.name;
            if (name.Contains("eye") || name.Contains("pupil") || name.Contains("mesh_health"))
            {
                continue;
            }

            foreach (var material in renderer.materials)
            {
                material.SetColor("_AlbedoColor", color);
            }
        }
    }

    private void InitializeFSM()
    {
        fsm = new StateMachine(this, DefaultState);
        fsm.AddState(StateId.Idle, new IdleState());
        fsm.AddState(StateId.Wander, new WanderState());
        fsm.AddState(StateId.Sprint, new SprintState());
        fsm.AddState(StateId.CheckMap, new CheckMapState());
        fsm.AddState(StateId.StalkApproach, new StalkApproachState());
        fsm.AddState(StateId.StalkStare, new StalkStareState());
        fsm.AddState(StateId.StalkFlee, new StalkFleeState());
    }

    private void FindCriticalTransforms()
    {
        ControllerTransform = transform.Find("Controller");
        if (ControllerTransform == null)
        {
            foreach (var child in GetComponentsInChildren<Transform>())
            {
                if (child.name == "Controller")
                {
                    ControllerTransform = child;
                    break;
                }
            }
        }

        if (ControllerTransform == null)
        {
            LOG.Warning("Controller transform not found, creating one");
            var controllerGO = new GameObject("Controller");
            controllerGO.transform.SetParent(transform);
            controllerGO.transform.localPosition = Vector3.zero;
            ControllerTransform = controllerGO.transform;
        }

        _rigidbodyTransform = transform.Find("Rigidbody");
        if (_rigidbodyTransform == null)
        {
            foreach (var child in GetComponentsInChildren<Transform>())
            {
                if (child.name == "Rigidbody")
                {
                    _rigidbodyTransform = child;
                    break;
                }
            }
        }

        if (_rigidbodyTransform == null)
        {
            LOG.Warning("Rigidbody transform not found");
        }
    }

    private void SaveNavMeshSettings()
    {
        NavMeshAgent existingAgent = null;

        if (ControllerTransform != null)
        {
            existingAgent = ControllerTransform.GetComponent<NavMeshAgent>();
        }

        if (existingAgent == null)
        {
            existingAgent = GetComponentInChildren<NavMeshAgent>();
        }

        if (existingAgent != null)
        {
            _savedAgentTypeID = existingAgent.agentTypeID;
            _savedAreaMask = existingAgent.areaMask;
        }
        else
        {
            _savedAgentTypeID = -334000983;
            _savedAreaMask = 1;
        }
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

            if (component is Transform) continue;
            if (component is Animator) continue;
            if (component is Renderer) continue;
            if (component is MeshFilter) continue;
            if (component is SkinnedMeshRenderer) continue;
            if (component is NavMeshAgent) continue;

            if (fullTypeName.Contains("Photon") || typeName.Contains("Photon") ||
                fullTypeName.Contains("Enemy") || fullTypeName.Contains("LostDroid") ||
                typeName.Contains("Enemy") || typeName.Contains("LostDroid"))
            {
                componentsToDestroy.Add(component);
                continue;
            }

            if (component is MonoBehaviour mb)
            {
                componentsToDisable.Add(mb);
            }
        }

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
                    }
                    catch { }
                }
            }

            if (destroyedThisPass == 0) break;
        }

        foreach (var mb in componentsToDisable)
        {
            if (mb != null && mb != this)
            {
                mb.enabled = false;
            }
        }

        foreach (var rb in GetComponentsInChildren<Rigidbody>())
        {
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        foreach (var col in GetComponentsInChildren<Collider>())
        {
            col.isTrigger = true;
        }
    }

    private void SetupAnimator()
    {
        Animator = GetComponentInChildren<Animator>();

        if (Animator != null)
        {
            Animator.enabled = true;
            Animator.applyRootMotion = false;

            var parentConstraint = Animator.GetComponent<ParentConstraint>();
            if (parentConstraint != null && !parentConstraint.constraintActive)
            {
                parentConstraint.constraintActive = true;
            }

            // Ajouter le relais pour les animation events (footsteps, etc.)
            Animator.gameObject.AddComponent<HallucinationAnimEvents>();
        }
        else
        {
            LOG.Warning("No Animator found on prefab");
        }
    }

    private void SetupNavigation()
    {
        if (ControllerTransform == null)
        {
            LOG.Error("Cannot setup navigation: Controller is null");
            return;
        }

        _navAgent = ControllerTransform.GetComponent<NavMeshAgent>();
        if (_navAgent == null)
        {
            _navAgent = ControllerTransform.gameObject.AddComponent<NavMeshAgent>();
        }

        var startPos = ControllerTransform.position;
        var validPosition = startPos;
        var foundNavMesh = false;

        var filter = new NavMeshQueryFilter
        {
            agentTypeID = _savedAgentTypeID,
            areaMask = _savedAreaMask
        };

        float[] distances = { 5f, 10f, 20f, 50f };
        foreach (var distance in distances)
        {
            if (NavMesh.SamplePosition(startPos, out var hit, distance, filter))
            {
                validPosition = hit.position;
                foundNavMesh = true;
                break;
            }
        }

        if (!foundNavMesh)
        {
            LOG.Error($"No NavMesh found near {startPos}");
            return;
        }

        _navAgent.enabled = false;
        _navAgent.agentTypeID = _savedAgentTypeID;
        _navAgent.areaMask = _savedAreaMask;

        _navAgent.baseOffset = 0f;
        _navAgent.speed = 2f;
        _navAgent.angularSpeed = 999f;
        _navAgent.acceleration = 15f;
        _navAgent.stoppingDistance = 0f;
        _navAgent.autoBraking = true;

        _navAgent.updatePosition = false;
        _navAgent.updateRotation = false;

        _navAgent.radius = 0.7f;
        _navAgent.height = 2f;
        _navAgent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
        _navAgent.avoidancePriority = 10;

        _navAgent.autoTraverseOffMeshLink = true;
        _navAgent.autoRepath = true;

        ControllerTransform.position = validPosition;
        _navAgent.Warp(validPosition);
        _navAgent.enabled = true;

        SetupCharacterController();
    }

    private void SetupCharacterController()
    {
        if (ControllerTransform == null) return;

        var existingCC = ControllerTransform.GetComponent<CharacterController>();
        if (existingCC != null)
        {
            DestroyImmediate(existingCC);
        }

        _charController = ControllerTransform.gameObject.AddComponent<CharacterController>();
        _charController.height = 2f;
        _charController.radius = 0.5f;
        _charController.center = new Vector3(0f, 1f, 0f);
        _charController.slopeLimit = 45f;
        _charController.stepOffset = 0.3f;
        _charController.skinWidth = 0.08f;
        _charController.minMoveDistance = 0.001f;
    }

    #endregion

    #region FSM

    public enum StateId
    {
        Idle,
        Wander,
        Sprint,
        CheckMap,
        StalkApproach,
        StalkStare,
        StalkFlee
    }

    #endregion
}