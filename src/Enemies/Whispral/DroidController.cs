using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using VepMod.Enemies.Whispral.Visuals;
using VepMod.VepFramework;
using VepMod.VepFramework.Structures.FSM;

// ReSharper disable Unity.NoNullPatternMatching

// ReSharper disable Unity.NoNullCoalescing

// ReSharper disable NullableWarningSuppressionIsUsed
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

namespace VepMod.Enemies.Whispral;

/// <summary>
///     Hallucination built on the MyDroid prefab of the embedded bundle; it wanders without attacking.
///     Non-visual backbone of the prefab:
///     - Controller: NavMeshAgent + CharacterController (movement)
///     - Rigidbody: transform followed by the visuals
///     The prefab visuals (Cube) are removed and replaced by a clone of the Imitated Player's
///     PlayerAvatarVisuals (cosmetics and colors included).
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

    // From the Victim when spawning, from the Hallucination itself when wandering.
    internal const float SpawnDistanceMin = 5f;
    internal const float SpawnDistanceMax = 15f;

    // Stalk settings (internal for FSM states)
    internal const float StalkMinDistance = 15f;
    internal const float StalkChance = 0.1f;
    internal const float StalkArrivalDistance = 5f;
    internal const float StalkMinKeepDistance = 3f;
    internal const float StalkFleeDistance = 20f;
    internal const float StalkStareBeforeFlee = 2f;

    private static readonly VepLogger LOG = VepLogger.Create<DroidController>();

    private readonly Materials.MaterialTrigger _materialTrigger = new();
    private DroidFaceAnimationController _animController;
    private DroidRightArmPoseController? _armPoseController;
    private DroidLeftArmPoseController? _leftArmPoseController;
    private DroidAvatarAnimationController _avatarAnimController;
    private CharacterController _charController;
    private DroidEyeLookAtController? _eyeLookController;
    private GameObject? _mapToolClone;
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
    public bool HasChangedMovementState { get; set; }

    /// <summary>
    ///     Read by DroidAvatarAnimationController to drive the animator's 'Grabbing' bool (the raised-hand pose).
    ///     Set during CheckMap.
    /// </summary>
    public bool WantsGrabbing { get; set; }

    public PlayerAvatar SourcePlayer { get; private set; }
    public Transform ControllerTransform { get; private set; }
    public DroidMovementController Movement { get; private set; }

    public bool IsPlayerLookingAtMe => _animController.IsPlayerLookingAtMe;

    public bool IsStalking =>
        Fsm.CurrentStateStateId is StateId.StalkApproach or StateId.StalkStare or StateId.StalkFlee;

    public bool IsInMovementState => DroidHelpers.IsMovementState(Fsm.CurrentStateStateId);

    /// <summary>
    ///     Relayed to the animator's 'Falling' bool.
    /// </summary>
    public bool IsFalling => Movement != null && Movement.IsFalling;

    public string DebugStateName => Fsm.CurrentStateStateId.ToString();

    protected override StateId DefaultState => StateId.Idle;

    protected override void Awake()
    {
        // base.Awake() is skipped: the FSM is built in Initialize().
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
        Movement.SyncVisualsToController(Animator != null ? Animator.transform : null);
        Movement.SyncNavAgentPosition(isMovementState);
    }

    /// <summary>
    ///     Also drives the raised-hand pose.
    /// </summary>
    public void ShowMapTool(bool visible)
    {
        LOG.Debug($"ShowMapTool({visible}) - clone={(_mapToolClone != null ? "ok" : "NULL")}");
        if (_mapToolClone != null) _mapToolClone.SetActive(visible);
        WantsGrabbing = visible;
    }

    #region Audio

    /// <summary>
    ///     Uses the local player's WhispralMimics, which holds the Voice Bank of every player.
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


    /// <summary>
    ///     Dev tools only. The CharacterController is disabled during the move to avoid stray collisions; a position
    ///     in mid-air falls and resyncs with the NavMesh on landing.
    /// </summary>
    public void DebugTeleport(Vector3 worldPosition)
    {
        if (ControllerTransform == null) return;

        if (_charController != null) _charController.enabled = false;
        ControllerTransform.position = worldPosition;
        if (_charController != null) _charController.enabled = true;
    }

    #region Movement (delegates to DroidMovementController)

    public bool TrySetRandomDestination()
    {
        return Movement != null && Movement.TrySetRandomDestination();
    }

    public bool TryExtendCurrentPath()
    {
        return Movement != null && Movement.TryExtendCurrentPath();
    }

    public void StartPrecomputeForwardDestination()
    {
        if (Movement != null) Movement.StartPrecomputeForwardDestination();
    }

    public void ClearPrecomputedForwardDestination()
    {
        if (Movement != null) Movement.ClearPrecomputedForwardDestination();
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
            LOG.Warning("Hallucination prefab not available");
            return null;
        }

        var instance = Instantiate(DroidPrefabLoader.DroidPrefab, spawnPosition, Quaternion.identity);
        if (instance == null)
        {
            LOG.Error("Failed to instantiate Hallucination prefab");
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
        SetupClonedVisuals();
        SetupAnimationController();
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
        _animController.Initialize(this, ControllerTransform);

        if (Animator != null)
        {
            _avatarAnimController = gameObject.AddComponent<DroidAvatarAnimationController>();
            _avatarAnimController.Initialize(this, Animator);
        }

        var visualsRoot = Animator != null ? Animator.transform : transform;

        // Setup head transform for talking animation (present in cloned player visuals)
        var headTopTransform = DroidHelpers.FindChildByName(visualsRoot, "code_head_top");
        if (headTopTransform != null)
        {
            _animController.SetupTalking(headTopTransform);
        }

        SetupEyelidsFromClone(visualsRoot);
    }

    private void SetupEyelidsFromClone(Transform visualsRoot)
    {
        try
        {
            var eyelidsLeft = DroidHelpers.FindChildByName(visualsRoot, "EYELIDS LEFT");
            var eyelidsRight = DroidHelpers.FindChildByName(visualsRoot, "EYELIDS RIGHT");

            if (eyelidsLeft == null || eyelidsRight == null)
            {
                LOG.Warning("EYELIDS LEFT/RIGHT not found on cloned visuals");
                return;
            }

            FindEyelidRotations(eyelidsLeft, out var leftUpperX, out var leftUpperZ, out var leftLowerX);
            FindEyelidRotations(eyelidsRight, out var rightUpperX, out var rightUpperZ, out var rightLowerX);

            eyelidsLeft.gameObject.SetActive(false);
            eyelidsRight.gameObject.SetActive(false);

            _animController.SetupEyelids(
                eyelidsLeft.gameObject, eyelidsRight.gameObject,
                leftUpperX, leftUpperZ, leftLowerX,
                rightUpperX, rightUpperZ, rightLowerX);
        }
        catch (Exception ex)
        {
            LOG.Error($"SetupEyelidsFromClone: Exception - {ex.Message}\n{ex.StackTrace}");
        }
    }

    private static void FindEyelidRotations(
        Transform eyelidsRoot,
        out Transform? upperX,
        out Transform? upperZ,
        out Transform? lowerX)
    {
        upperX = null;
        upperZ = null;
        lowerX = null;

        foreach (var child in eyelidsRoot.GetComponentsInChildren<Transform>(true))
        {
            switch (child.name)
            {
                case "eyelid_upper":
                    upperX = child;
                    break;
                case "eyelid_upper_rotation":
                    upperZ = child;
                    break;
                case "eyelid_lower":
                    lowerX = child;
                    break;
            }
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

    /// <summary>
    ///     Called by animation events.
    /// </summary>
    public void PlayMediumFootstep()
    {
        if (ControllerTransform == null) return;
        // Slightly above the ground so the material raycast hits the floor.
        var footPosition = ControllerTransform.position + Vector3.up * 0.1f;
        Materials.Instance.Impulse(
            footPosition,
            Vector3.down,
            Materials.SoundType.Medium,
            true,
            true,
            _materialTrigger,
            Materials.HostType.OtherPlayer);

        if (SourcePlayer)
        {
            var distance = Vector3.Distance(footPosition, SourcePlayer.transform.position);
            LOG.Debug($"Footstep of the Hallucination of {SourcePlayer.playerName}, {distance:F1} m from them");
        }
    }

    private void SetupNameplate()
    {
        _nameplateController = gameObject.AddComponent<DroidNameplate>();
        _nameplateController.Initialize(ControllerTransform, SourcePlayer);
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
                fullTypeName.Contains("Enemy") || typeName.Contains("Enemy"))
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

    private void SetupClonedVisuals()
    {
        if (SourcePlayer == null)
        {
            LOG.Error("Cannot clone visuals: source player is null");
            return;
        }

        var cube = DroidVisualsClone.FindCube(transform);
        if (cube == null)
        {
            LOG.Error("Cannot clone visuals: MyDroid/Enable/Cube not found");
            return;
        }

        // Cube keeps its Animator, BloodDust and Hurt Collider; only the player components that throw NREs
        // (PlayerAvatarVisuals and its siblings) go.
        DroidVisualsClone.SanitizeCube(cube);

        Animator = cube.GetComponent<Animator>();

        DroidVisualsClone.StripExistingRig(cube);

        if (!DroidVisualsClone.TryCloneRig(SourcePlayer, cube, out var cloneRig))
        {
            LOG.Warning("Failed to clone source player [RIG]");
            return;
        }

        if (Animator != null && cloneRig != null)
        {
            Animator.enabled = true;
            Animator.applyRootMotion = false;

            // The Animator ran its Awake on the prefab's old [RIG] and caches bone bindings by path. Swapping [RIG]
            // breaks them; Rebind makes Unity resolve the bone paths against the new hierarchy.
            Animator.Rebind();
            Animator.Update(0f);

            DroidVisualsClone.AttachRelay(Animator, this, cloneRig);
            DroidVisualsClone.TryCloneFlashlight(SourcePlayer, cloneRig);
            DroidVisualsClone.TryCloneMapTool(SourcePlayer, cloneRig, out _mapToolClone);
            SetupArmAndEyeMimics(cloneRig);
        }
        else if (Animator == null)
        {
            LOG.Warning("Cube has no Animator after SanitizeCube");
        }
    }

    private void SetupArmAndEyeMimics(GameObject cloneRig)
    {
        if (SourcePlayer == null || cloneRig == null) return;

        // PlayerAvatarRightArm and PlayerEyes live on the 'Player Visuals' GameObject
        // (playerAvatarVisuals.gameObject), a sibling of the 'Player Avatar Controller' that holds PlayerAvatar.cs, so
        // GetComponentInChildren on the Imitated Player does not find them.
        var visuals = SourcePlayer.playerAvatarVisuals;
        if (visuals == null)
        {
            LOG.Warning("SetupArmAndEyeMimics: SourcePlayer.playerAvatarVisuals is null");
            return;
        }

        var sourceArm = visuals.GetComponent<PlayerAvatarRightArm>();
        if (sourceArm != null && sourceArm.rightArmTransform != null)
        {
            var clonedArm = DroidHelpers.FindChildByName(cloneRig.transform, sourceArm.rightArmTransform.name);
            if (clonedArm != null)
            {
                _armPoseController = gameObject.AddComponent<DroidRightArmPoseController>();
                _armPoseController.Initialize(this, clonedArm, sourceArm.basePose, sourceArm.mapPose,
                    sourceArm.poseCurve, sourceArm.poseSpeed);
                LOG.Debug($"DroidRightArmPoseController initialized on bone '{sourceArm.rightArmTransform.name}'");
            }
            else
            {
                LOG.Warning($"SetupArmAndEyeMimics: bone '{sourceArm.rightArmTransform.name}' not found in clone rig");
            }
        }
        else
        {
            LOG.Warning("SetupArmAndEyeMimics: no PlayerAvatarRightArm on source player");
        }

        // The Hallucination's flashlight is always on, so the left arm always aims for flashlightPose.
        var sourceLeftArm = visuals.GetComponent<PlayerAvatarLeftArm>();
        if (sourceLeftArm != null && sourceLeftArm.leftArmTransform != null)
        {
            var clonedLeftArm = DroidHelpers.FindChildByName(cloneRig.transform, sourceLeftArm.leftArmTransform.name);
            if (clonedLeftArm != null)
            {
                _leftArmPoseController = gameObject.AddComponent<DroidLeftArmPoseController>();
                _leftArmPoseController.Initialize(clonedLeftArm, sourceLeftArm.basePose, sourceLeftArm.flashlightPose,
                    sourceLeftArm.poseCurve, sourceLeftArm.poseSpeed);
                LOG.Debug($"DroidLeftArmPoseController initialized on bone '{sourceLeftArm.leftArmTransform.name}'");
            }
            else
            {
                LOG.Warning($"SetupArmAndEyeMimics: left arm bone '{sourceLeftArm.leftArmTransform.name}' not found in clone rig");
            }
        }
        else
        {
            LOG.Warning("SetupArmAndEyeMimics: no PlayerAvatarLeftArm on source player");
        }

        var sourceEyes = visuals.GetComponent<PlayerEyes>();
        if (sourceEyes != null && sourceEyes.eyeLeft != null && sourceEyes.eyeRight != null)
        {
            var clonedEyeLeft = DroidHelpers.FindChildByName(cloneRig.transform, sourceEyes.eyeLeft.name);
            var clonedEyeRight = DroidHelpers.FindChildByName(cloneRig.transform, sourceEyes.eyeRight.name);
            var lookTarget = _mapToolClone != null
                ? DroidHelpers.FindChildByName(_mapToolClone.transform, "Player Look Target")
                : null;

            if (clonedEyeLeft != null && clonedEyeRight != null && lookTarget != null)
            {
                _eyeLookController = gameObject.AddComponent<DroidEyeLookAtController>();
                _eyeLookController.Initialize(this, clonedEyeLeft, clonedEyeRight, lookTarget);
                LOG.Debug("DroidEyeLookAtController initialized");
            }
            else
            {
                LOG.Warning($"SetupArmAndEyeMimics: eye bones or lookTarget missing " +
                            $"(eyeL={clonedEyeLeft != null}, eyeR={clonedEyeRight != null}, lookTarget={lookTarget != null})");
            }
        }
        else
        {
            LOG.Warning("SetupArmAndEyeMimics: no PlayerEyes on source player or eyeLeft/Right null");
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
