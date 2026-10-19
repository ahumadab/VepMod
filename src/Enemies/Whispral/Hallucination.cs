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
///     - Rigidbody: follows the Controller
///     The prefab visuals (Cube) are removed and replaced by a clone of the Imitated Player's
///     PlayerAvatarVisuals (cosmetics and colors included), which HallucinationBody animates.
/// </summary>
public sealed partial class Hallucination : StateMachineComponent<Hallucination, Hallucination.StateId>
{
    public enum StateId
    {
        Idle,
        Wander,
        Sprint,
        CheckMap,
        StalkApproach,
        StalkStare,
        StalkLunge,
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

    private static readonly VepLogger LOG = VepLogger.Create<Hallucination>();

    // Static: Hallucinations exist only on the Victim's client, so every one on a client is the local player's (ADR-0002).
    private static readonly List<Hallucination> All = new();

    private readonly Materials.MaterialTrigger _materialTrigger = new();
    private readonly Wait _wait = new(() => UnityEngine.Random.value);
    private readonly TripChain _tripChain = new(() => UnityEngine.Random.value);
    private readonly ChangeOfMind _changeOfMind = new(() => UnityEngine.Random.value);
    private HallucinationFace _face;
    private CharacterController _charController;
    private HallucinationNameplate _nameplateController;
    private NavMeshAgent _navAgent;
    private Transform? _rigidbodyTransform;
    private Animator? _animator;
    private HallucinationBody? _body;
    private Transform? _head;

    // NavMesh settings (saved for initialization)
    private int _savedAgentTypeID;
    private int _savedAreaMask = NavMesh.AllAreas;

    private bool _stalkingRollForced;

    public PlayerAvatar ImitatedPlayer { get; private set; }
    public Transform ControllerTransform { get; private set; }
    internal HallucinationMovement Movement { get; private set; }
    internal HallucinationGaze Gaze { get; private set; }
    internal Vector3 Face => _head != null ? _head.position : ControllerTransform.position + Vector3.up;
    internal HallucinationBodyPose BodyPose { get; private set; }
    internal LungeCountdown LungeCountdown { get; } = new();
    internal DeadTeammateEffect DeadTeammateEffect { get; private set; }

    public bool IsStalkingBeforeFlee =>
        Fsm.CurrentStateStateId is StateId.StalkApproach or StateId.StalkStare or StateId.StalkLunge;

    public string DebugStateName => Fsm.CurrentStateStateId.ToString();

    protected override StateId DefaultState => StateId.Idle;

    private static bool IsStalkingState(StateId state)
    {
        return state is StateId.StalkApproach or StateId.StalkStare or StateId.StalkLunge or StateId.StalkFlee;
    }

    private Hallucination? OtherStalker()
    {
        foreach (var hallucination in All)
        {
            if (hallucination != this && IsStalkingState(hallucination.Fsm.CurrentStateStateId)) return hallucination;
        }

        return null;
    }

    protected override void Awake()
    {
        base.Awake();
        All.Add(this);
    }

    protected override void Update()
    {
        if (Movement == null || !Movement.IsOnNavMesh) return;
        if (ControllerTransform == null) return;

        Fsm.Update();

        Movement.UpdateMovement();
        Movement.UpdateRotation();
        Movement.SyncRigidbodyToController();
        Movement.SyncNavAgentPosition();
        _body?.Animate();
    }

    /// <summary>
    ///     Dev tools only: starts Stalking even while another Hallucination is Stalking, the one it returns.
    /// </summary>
    public Hallucination? DebugStartStalking()
    {
        Fsm.NextStateStateId = StateId.StalkApproach;
        return OtherStalker();
    }

    /// <summary>
    ///     Dev tools only: the next Stalking roll from beyond the Stalking distance succeeds, refused or not.
    /// </summary>
    public void DebugForceNextStalkingRoll()
    {
        _stalkingRollForced = true;
    }

    /// <summary>
    ///     Dev tools only.
    /// </summary>
    public void DebugCheckMap()
    {
        Fsm.NextStateStateId = StateId.CheckMap;
    }

    /// <summary>
    ///     Dev tools only.
    /// </summary>
    public void DebugTurnRoundNextWait()
    {
        _wait.TurnRoundNextWait();
    }

    /// <summary>
    ///     Dev tools only.
    /// </summary>
    public void DebugGlanceToBoundNext()
    {
        _wait.GlanceToBoundNext();
    }

    /// <summary>
    ///     Dev tools only.
    /// </summary>
    public void DebugChainNextArrival()
    {
        _tripChain.ChainNextArrival();
    }

    /// <summary>
    ///     Dev tools only.
    /// </summary>
    public void DebugChangeMindNow()
    {
        _changeOfMind.ChangeNow();
    }

    /// <summary>
    ///     Dev tools only: walks to <paramref name="point" />, which must be on the NavMesh.
    /// </summary>
    public void DebugWalkTo(Vector3 point)
    {
        // A trip under way only changes its destination: the state machine does not re-enter its current state.
        if (Fsm.CurrentStateStateId is StateId.Wander or StateId.Sprint)
        {
            Movement.DebugSetDestination(point);
            return;
        }

        Movement.DebugQueueDestination(point);
        Fsm.NextStateStateId = StateId.Wander;
    }

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

    #region Factory & Initialization

    public static Hallucination? Create(PlayerAvatar imitatedPlayer, Vector3 spawnPosition)
    {
        if (!HallucinationPrefabLoader.IsAvailable)
        {
            LOG.Warning("Hallucination prefab not available");
            return null;
        }

        var instance = Instantiate(HallucinationPrefabLoader.Prefab, spawnPosition, Quaternion.identity);
        if (instance == null)
        {
            LOG.Error("Failed to instantiate Hallucination prefab");
            return null;
        }

        instance.name = $"Hallucination_{imitatedPlayer.playerName}";

        var hallucination = instance.AddComponent<Hallucination>();
        hallucination.Initialize(imitatedPlayer);

        return hallucination;
    }

    private void Initialize(PlayerAvatar imitatedPlayer)
    {
        ImitatedPlayer = imitatedPlayer;

        FindCriticalTransforms();
        SaveNavMeshSettings();
        DisableEnemyComponents();
        SetupNavigation();
        SetupMovementController();
        BodyPose = gameObject.AddComponent<HallucinationBodyPose>();
        DeadTeammateEffect = gameObject.AddComponent<DeadTeammateEffect>();
        SetupClonedVisuals();
        SetupGaze();
        SetupAnimationController();
        SetupNameplate();
        AddStates();

        LOG.Info($"Hallucination created for {imitatedPlayer.playerName} at {ControllerTransform.position}");
    }

    private void SetupMovementController()
    {
        if (ControllerTransform == null) return;

        Movement = gameObject.AddComponent<HallucinationMovement>();
        Movement.Initialize(ControllerTransform, _rigidbodyTransform, _navAgent, _charController, _savedAreaMask);
        Movement.OnNavMeshError += HandleNavMeshError;
    }

    private void HandleNavMeshError()
    {
        Fsm.NextStateStateId = StateId.Idle;
    }

    private void SetupGaze()
    {
        _head = HallucinationVisualsClone.FindChildByName(transform, "code_head_top");
        Gaze = new HallucinationGaze(
            transform,
            ControllerTransform,
            _head,
            HallucinationVisualsClone.FindChildByName(transform, "code_body_top_up"),
            ImitatedPlayer.playerName);
    }

    private void SetupAnimationController()
    {
        _face = gameObject.AddComponent<HallucinationFace>();
        _face.Initialize(this);

        var visualsRoot = _animator != null ? _animator.transform : transform;

        // Setup head transform for talking animation (present in cloned player visuals)
        var headTopTransform = HallucinationVisualsClone.FindChildByName(visualsRoot, "code_head_top");
        if (headTopTransform != null)
        {
            _face.SetupTalking(headTopTransform);
        }

        SetupEyelidsFromClone(visualsRoot);
    }

    private void SetupEyelidsFromClone(Transform visualsRoot)
    {
        try
        {
            var eyelidsLeft = HallucinationVisualsClone.FindChildByName(visualsRoot, "EYELIDS LEFT");
            var eyelidsRight = HallucinationVisualsClone.FindChildByName(visualsRoot, "EYELIDS RIGHT");

            if (eyelidsLeft == null || eyelidsRight == null)
            {
                LOG.Warning("EYELIDS LEFT/RIGHT not found on cloned visuals");
                return;
            }

            // The game's PlayerExpression springs eyelids_scale to 1 while a player expresses, then leaves it around 0.8
            // once the expression ends, and the copied visuals keep that live scale: below 1 the lids sit inside the
            // eyeball.
            var leftScale = HallucinationVisualsClone.FindChildByName(eyelidsLeft, "eyelids_scale");
            var rightScale = HallucinationVisualsClone.FindChildByName(eyelidsRight, "eyelids_scale");
            if (leftScale == null || rightScale == null)
            {
                LOG.Warning("eyelids_scale not found under EYELIDS LEFT/RIGHT on cloned visuals");
                return;
            }

            LOG.Debug($"Eyelids scale reset to 1 from left {leftScale.localScale.x:F2}, right {rightScale.localScale.x:F2}: " +
                      $"Hallucination of {ImitatedPlayer.playerName}");
            leftScale.localScale = Vector3.one;
            rightScale.localScale = Vector3.one;

            FindEyelidRotations(eyelidsLeft, out var leftUpperX, out var leftUpperZ, out var leftLowerX);
            FindEyelidRotations(eyelidsRight, out var rightUpperX, out var rightUpperZ, out var rightLowerX);

            eyelidsLeft.gameObject.SetActive(false);
            eyelidsRight.gameObject.SetActive(false);

            _face.SetupEyelids(
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
        Gaze.Update();
        _nameplateController.UpdateNameplate();
        _face.UpdateAnimations();
    }

    /// <summary>
    ///     Destroys the Hallucination. A Mimic it spoke as it lunged stays in the world where it stands and plays to its
    ///     end; other Mimics are cut.
    /// </summary>
    public void Remove()
    {
        LeaveLungeMimicInWorld();
        // In OnDestroy, the emitters no longer play: the line was never written from there.
        LogMimicsCutShort();
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        All.Remove(this);
        _body?.Release();
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

        if (ImitatedPlayer)
        {
            var distance = Vector3.Distance(footPosition, ImitatedPlayer.transform.position);
            LOG.Debug($"Footstep of the Hallucination of {ImitatedPlayer.playerName}, {distance:F1} m from them");
        }
    }

    private void SetupNameplate()
    {
        _nameplateController = gameObject.AddComponent<HallucinationNameplate>();
        _nameplateController.Initialize(ControllerTransform, ImitatedPlayer, Gaze);
    }

    private void AddStates()
    {
        Fsm.AddState(StateId.Idle, new IdleState());
        Fsm.AddState(StateId.Wander, new WanderState());
        Fsm.AddState(StateId.Sprint, new SprintState());
        Fsm.AddState(StateId.CheckMap, new CheckMapState());
        Fsm.AddState(StateId.StalkApproach, new StalkApproachState());
        Fsm.AddState(StateId.StalkStare, new StalkStareState());
        Fsm.AddState(StateId.StalkLunge, new StalkLungeState());
        Fsm.AddState(StateId.StalkFlee, new StalkFleeState());
    }

    private void FindCriticalTransforms()
    {
        var controllerTransform = HallucinationVisualsClone.FindChildByName(transform, "Controller");
        if (controllerTransform == null)
        {
            LOG.Warning("Controller transform not found, creating one");
            var controllerGo = new GameObject("Controller");
            controllerGo.transform.SetParent(transform);
            controllerGo.transform.localPosition = Vector3.zero;
            controllerTransform = controllerGo.transform;
        }

        ControllerTransform = controllerTransform;

        _rigidbodyTransform = HallucinationVisualsClone.FindChildByName(transform, "Rigidbody");
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
        if (ImitatedPlayer == null)
        {
            LOG.Error("Cannot clone visuals: Imitated Player is null");
            return;
        }

        var cube = HallucinationVisualsClone.FindCube(transform);
        if (cube == null)
        {
            LOG.Error("Cannot clone visuals: MyDroid/Enable/Cube not found");
            return;
        }

        // Cube keeps its Animator, BloodDust and Hurt Collider; only the player components that throw NREs
        // (PlayerAvatarVisuals and its siblings) go.
        HallucinationVisualsClone.SanitizeCube(cube);

        _animator = cube.GetComponent<Animator>();

        HallucinationVisualsClone.StripExistingRig(cube);

        if (!HallucinationVisualsClone.TryCloneRig(ImitatedPlayer, cube, out var cloneRig, out var springTypes))
        {
            LOG.Warning("Failed to clone Imitated Player [RIG]");
            return;
        }

        if (_animator != null && cloneRig != null)
        {
            _animator.enabled = true;
            _animator.applyRootMotion = false;

            // The Animator ran its Awake on the prefab's old [RIG] and caches bone bindings by path. Swapping [RIG]
            // breaks them; Rebind makes Unity resolve the bone paths against the new hierarchy.
            _animator.Rebind();
            _animator.Update(0f);

            HallucinationVisualsClone.AttachRelay(_animator, this, cloneRig, springTypes);
            HallucinationVisualsClone.TryCloneFlashlight(ImitatedPlayer, cloneRig);
            HallucinationVisualsClone.TryCloneMapTool(ImitatedPlayer, cloneRig, out var mapTool);

            // The arm and eye scripts live on the 'Player Visuals' GameObject, a sibling of the 'Player Avatar
            // Controller' that holds PlayerAvatar.cs: GetComponentInChildren on the Imitated Player misses them.
            BodyPose.Initialize(ImitatedPlayer.playerAvatarVisuals, cloneRig.transform, mapTool);
            _body = HallucinationBody.Create(this, cube, _animator);
        }
        else if (_animator == null)
        {
            LOG.Warning("Cube has no Animator after SanitizeCube");
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
