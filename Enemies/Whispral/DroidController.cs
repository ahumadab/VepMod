using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations;
using VepMod.VepFramework;
using VepMod.VepFramework.Structures.FSM;

// ReSharper disable Unity.NoNullPatternMatching

// ReSharper disable Unity.NoNullCoalescing

// ReSharper disable NullableWarningSuppressionIsUsed
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

namespace VepMod.Enemies.Whispral;

/// <summary>
///     Hallucination basée sur le prefab LostDroid de WesleysEnemies.
///     Se balade sans attaquer, utilise l'architecture originale:
///     - Controller: NavMeshAgent + CharacterController (mouvement)
///     - Rigidbody: transform suivi par Cube via ParentConstraint
///     - Cube: visuels + Animator
/// </summary>
public sealed partial class DroidController : StateMachineComponent<DroidController, DroidController.StateId>
{
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

    private static readonly VepLogger LOG = VepLogger.Create<DroidController>(true);
    private static readonly int IsWalkingKey = Animator.StringToHash("isWalking");
    private static readonly int IsSprintingKey = Animator.StringToHash("isSprinting");
    private static readonly int IsTurningKey = Animator.StringToHash("isTurning");
    private static readonly int StunKey = Animator.StringToHash("stun");
    private static readonly int AlbedoColorKey = Shader.PropertyToID("_AlbedoColor");

    private readonly Materials.MaterialTrigger _materialTrigger = new();
    private DroidFaceAnimationController _animController;
    private CharacterController _charController;
    private DroidNameplate _nameplateController;
    private NavMeshAgent _navAgent;
    private Transform _rigidbodyTransform;

    // NavMesh settings (saved for initialization)
    private int _savedAgentTypeID;
    private int _savedAreaMask = NavMesh.AllAreas;

    // Components (animator is internal for FSM states)
    internal Animator Animator;

    public bool IsWalking { get; set; }
    public bool IsSprinting { get; set; }
    public bool IsTurning { get; private set; }
    public bool HasChangedMovementState { get; set; }
    public PlayerAvatar SourcePlayer { get; private set; }
    public Transform ControllerTransform { get; private set; }
    public DroidMovementController Movement { get; private set; }

    public bool IsPlayerLookingAtMe => _animController.IsPlayerLookingAtMe;

    protected override StateId DefaultState => StateId.Idle;

    protected override void Awake()
    {
        // Ne pas appeler base.Awake() ici - on initialise la FSM dans Initialize()
    }

    protected override void Update()
    {
        if (Movement == null || _navAgent == null || !_navAgent.isOnNavMesh) return;
        if (ControllerTransform == null) return;

        // Update FSM
        Fsm.Update();

        // Update movement
        var currentState = Fsm.CurrentStateStateId;
        var isMovementState = DroidHelpers.IsMovementState(currentState);

        Movement.UpdateMovement(isMovementState);
        Movement.UpdateRotation();
        Movement.SyncVisualsToController(Animator.transform);
        UpdateAnimationFlags();
        Movement.SyncNavAgentPosition(isMovementState);
    }

    #region Audio

    /// <summary>
    ///     Joue le son enregistré du joueur source à la position du Controller.
    ///     Utilise le WhispralMimics du joueur LOCAL (qui a les fichiers audio stockés).
    /// </summary>
    public void PlayVoice(bool applyFilter = false)
    {
        if (SourcePlayer == null || ControllerTransform == null) return;

        var localPlayer = PlayerAvatar.instance;
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


    #region Animation

    private void UpdateAnimationFlags()
    {
        var currentState = Fsm.CurrentStateStateId;
        var isMovementState = DroidHelpers.IsMovementState(currentState);

        if (isMovementState && ControllerTransform != null && Movement != null)
        {
            var angle = Quaternion.Angle(ControllerTransform.rotation, Movement.TargetRotation);
            IsTurning = angle > 7f;
        }
        else
        {
            IsTurning = false;
        }

        if (Animator != null)
        {
            Animator.SetBool(IsWalkingKey, IsWalking);
            Animator.SetBool(IsSprintingKey, IsSprinting);
            Animator.SetBool(IsTurningKey, IsTurning);
            Animator.SetBool(StunKey, false);
        }
    }

    #endregion

    #region Movement (delegates to DroidMovementController)

    public bool TrySetRandomDestination()
    {
        return Movement != null && Movement.TrySetRandomDestination();
    }

    public void StartPrecomputeDestination()
    {
        Movement.StartPrecomputeDestination();
    }

    public bool HasPrecomputedDestination => Movement.HasPrecomputedDestination;

    public void ResetPath()
    {
        Movement.ResetPath();
    }

    public bool HasReachedDestination()
    {
        return Movement.HasReachedDestination();
    }

    public float GetDistanceToPlayer()
    {
        return Movement.GetDistanceToPlayer();
    }

    public bool TrySetDestinationToPlayer()
    {
        return Movement != null && Movement.TrySetDestinationToPlayer();
    }

    public bool TrySetFleeDestination()
    {
        return Movement != null && Movement.TrySetFleeDestination(StalkFleeDistance);
    }

    public void LookAtPlayer()
    {
        Movement.LookAtPlayer();
    }

    public void SetSpeed(float speed)
    {
        Movement.SetSpeed(speed);
    }

    #endregion

    #region Factory & Initialization

    public static DroidController? Create(PlayerAvatar sourcePlayer, Vector3 spawnPosition)
    {
        if (!DroidPrefabLoader.IsAvailable)
        {
            LOG.Warning("LostDroid prefab not available");
            return null;
        }

        var instance = Instantiate(DroidPrefabLoader.LostDroidPrefab, spawnPosition, Quaternion.identity);
        if (instance == null)
        {
            LOG.Error("Failed to instantiate LostDroid prefab");
            return null;
        }

        instance.name = $"HallucinationDroid_{sourcePlayer.playerName}";

        var hallucination = instance.AddComponent<DroidController>();
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
        InitializeFsm();

        LOG.Info($"HallucinationDroid created for {sourcePlayer.playerName} at {ControllerTransform.position}");
    }

    private void SetupMovementController()
    {
        if (ControllerTransform == null) return;

        Movement = gameObject.AddComponent<DroidMovementController>();
        Movement.Initialize(ControllerTransform, _rigidbodyTransform, _navAgent, _charController, _savedAreaMask);
        Movement.OnNavMeshError += HandleNavMeshError;
    }

    private void HandleNavMeshError()
    {
        Fsm.NextStateStateId = StateId.Idle;
    }

    private void SetupAnimationController()
    {
        _animController = gameObject.AddComponent<DroidFaceAnimationController>();
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
                out var leftUpperX, out var leftUpperZ, out var leftLowerX);

            var eyelidsRight = SetupEyeFromPlayer(codeEyeRight, playerSource, "RIGHT",
                out var rightUpperX, out var rightUpperZ, out var rightLowerX);

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
        _nameplateController.UpdateNameplate();
        _animController.UpdateAnimations();
    }

    private void OnDestroy()
    {
        if (Movement != null)
        {
            Movement.OnNavMeshError -= HandleNavMeshError;
        }
    }

    private Material? GetDroidBodyMaterial()
    {
        // Chercher un renderer sur le corps du droid (pas les yeux)
        foreach (var renderer in GetComponentsInChildren<MeshRenderer>(true))
        {
            var gameObjectName = renderer.gameObject.name.ToLower();
            // Éviter les yeux et pupilles
            if (gameObjectName.Contains("eye") || gameObjectName.Contains("pupil") ||
                gameObjectName.Contains("eyelid"))
            {
                continue;
            }

            if (renderer.material != null)
            {
                return renderer.material;
            }
        }

        return null;
    }

    private Transform? FindEyelidSource()
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
    private GameObject? SetupEyeFromPlayer(
        Transform droidEye,
        Transform playerSource,
        string side,
        out Transform? upperRotationX,
        out Transform? upperRotationZ,
        out Transform? lowerRotationX)
    {
        upperRotationX = null;
        upperRotationZ = null;
        lowerRotationX = null;

        var eyelidsName = $"EYELIDS {side}";

        // Trouver EYELIDS sur le player source
        Transform? playerEyelids = null;
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
        Transform? droidEyelids = null;
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
                // case "eyelid_lower_rotation":
                //     lowerRotationZ = child;
                //     break;
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
            var gameObjectName = renderer.gameObject.name;
            if (gameObjectName.Contains("eye") || gameObjectName.Contains("pupil") ||
                gameObjectName.Contains("mesh_health"))
            {
                continue;
            }

            foreach (var material in renderer.materials)
            {
                material.SetColor(AlbedoColorKey, color);
            }
        }
    }

    private void InitializeFsm()
    {
        Fsm = new StateMachine(this, DefaultState);
        Fsm.AddState(StateId.Idle, new IdleState());
        Fsm.AddState(StateId.Wander, new WanderState());
        Fsm.AddState(StateId.Sprint, new SprintState());
        Fsm.AddState(StateId.CheckMap, new CheckMapState());
        Fsm.AddState(StateId.StalkApproach, new StalkApproachState());
        Fsm.AddState(StateId.StalkStare, new StalkStareState());
        Fsm.AddState(StateId.StalkFlee, new StalkFleeState());
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
            var controllerGo = new GameObject("Controller");
            controllerGo.transform.SetParent(transform);
            controllerGo.transform.localPosition = Vector3.zero;
            ControllerTransform = controllerGo.transform;
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
        NavMeshAgent? existingAgent = null;

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
                    catch
                    {
                        // ignored
                    }
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
            var a = Animator.GetOrAddComponent<DroidFootstepsEventReceiver>();
            a.Initialize(this);
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

        var existingCharacterController = ControllerTransform.GetComponent<CharacterController>();
        if (existingCharacterController != null)
        {
            DestroyImmediate(existingCharacterController);
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
}