using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations;
using VepMod.VepFramework;
using VepMod.VepFramework.Structures.FSM;
using Random = UnityEngine.Random;

namespace VepMod.Enemies.Whispral;

/// <summary>
///     Hallucination basée sur le prefab LostDroid de WesleysEnemies.
///     Se balade sans attaquer, utilise l'architecture originale:
///     - Controller: NavMeshAgent + CharacterController (mouvement)
///     - Rigidbody: transform suivi par Cube via ParentConstraint
///     - Cube: visuels + Animator
/// </summary>
public sealed class HallucinationDroid : StateMachineComponent<HallucinationDroid, HallucinationDroid.StateId>
{
    internal const float WalkSpeed = 2f;
    internal const float SprintSpeed = 5f;
    internal const float SprintChance = 0.5f;

    private const float NameplateHeight = 1.9f;

    // Stalk settings
    private const float StalkMinDistance = 15f; // Distance min pour déclencher le stalk
    private const float StalkChance = 0.2f; // Chance de stalk quand trop loin
    private const float StalkArrivalDistance = 5f; // Distance pour commencer à fixer le joueur
    private const float StalkMinKeepDistance = 3f; // Distance minimum à maintenir du joueur
    private const float StalkFleeDistance = 20f; // Distance de fuite
    private const float StalkStareBeforeFlee = 2f; // Temps à fixer le joueur après être vu avant de fuir

    private static readonly VepLogger LOG = VepLogger.Create<HallucinationDroid>(true);
    private readonly Materials.MaterialTrigger _materialTrigger = new();

    // Components
    private Animator _animator;
    private DroidAnimationController _animController;
    private CharacterController _charController;
    private DroidMovementController _movement;
    private NavMeshAgent _navAgent;
    private Transform _rigidbodyTransform;

    // Nameplate
    private WorldSpaceUIPlayerName _nameplate;

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
        _movement.SyncVisualsToController(_animator?.transform);
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

        if (_animator != null)
        {
            _animator.SetBool("isWalking", IsWalking);
            _animator.SetBool("isSprinting", IsSprinting);
            _animator.SetBool("isTurning", IsTurning);
            _animator.SetBool("stun", false);
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
        CreateNameplate();
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
        UpdateNameplate();
        _animController?.UpdateAnimations();
    }

    private void OnDestroy()
    {
        if (_movement != null)
        {
            _movement.OnNavMeshError -= HandleNavMeshError;
        }

        if (_nameplate != null)
        {
            Destroy(_nameplate.gameObject);
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

    private void CreateNameplate()
    {
        if (SourcePlayer == null || WorldSpaceUIParent.instance == null) return;

        var prefab = WorldSpaceUIParent.instance.playerNamePrefab;
        if (prefab == null) return;

        var nameplateGO = Instantiate(prefab, WorldSpaceUIParent.instance.transform);
        _nameplate = nameplateGO.GetComponent<WorldSpaceUIPlayerName>();

        if (_nameplate != null)
        {
            // Assigner le SourcePlayer pour éviter la destruction automatique
            _nameplate.playerAvatar = SourcePlayer;
            _nameplate.text.text = SourcePlayer.playerName;
        }
    }

    private void UpdateNameplate()
    {
        if (_nameplate == null || ControllerTransform == null || Camera.main == null) return;

        var worldPos = ControllerTransform.position + Vector3.up * NameplateHeight;
        var cameraPos = Camera.main.transform.position;
        var distance = Vector3.Distance(worldPos, cameraPos);

        // Vérifier si le droid est visible (pas de mur entre la caméra et le droid)
        var direction = worldPos - cameraPos;
        var isVisible = !Physics.Raycast(cameraPos, direction.normalized, distance,
            LayerMask.GetMask("Default"), QueryTriggerInteraction.Ignore);

        // Alpha basé sur la distance (1 proche, 0 à 30m+)
        const float maxDistance = 8f;
        var distanceAlpha = Mathf.Clamp01(1f - distance / maxDistance);
        var targetAlpha = isVisible ? distanceAlpha : 0f;

        // Lerp smooth
        var currentColor = _nameplate.text.color;
        var newAlpha = Mathf.Lerp(currentColor.a, targetAlpha, Time.deltaTime * 5f);
        _nameplate.text.color = new Color(1f, 1f, 1f, newAlpha);

        // Mettre à jour la position
        var screenPos = SemiFunc.UIWorldToCanvasPosition(worldPos);
        var rect = _nameplate.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.anchoredPosition = screenPos;
        }

        // Ajuster la taille selon la distance
        _nameplate.text.fontSize = Mathf.Clamp(20f - distance, 8f, 20f);
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
        _animator = GetComponentInChildren<Animator>();

        if (_animator != null)
        {
            _animator.enabled = true;
            _animator.applyRootMotion = false;

            var parentConstraint = _animator.GetComponent<ParentConstraint>();
            if (parentConstraint != null && !parentConstraint.constraintActive)
            {
                parentConstraint.constraintActive = true;
            }

            // Ajouter le relais pour les animation events (footsteps, etc.)
            _animator.gameObject.AddComponent<HallucinationAnimEvents>();
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

    private class IdleState : StateMachineBase<StateMachine, StateId>.StateBaseTimed
    {
        private const string ClipName = "LostDroidStand";
        private const float IdleLoopStart = 0f;
        private const float IdleLoopEnd = 4.5f;
        private const float BlendDelay = 0.3f; // Temps pour laisser l'Animator faire le blend

        private const float MinIdleTime = 2f;
        private const float MaxIdleTime = 4f;
        private const float PrecomputeRetryInterval = 1f;
        private const float CheckMapChance = 0.25f;

        private Animator _animator;
        private bool _animatorTakenOver;
        private AnimationClip _clip;
        private float _currentTime;

        private float _duration;
        private float _precomputeTimer;
        private GameObject _targetObject;

        public override void OnStateEnter(StateId previous)
        {
            base.OnStateEnter(previous);
            _duration = Random.Range(MinIdleTime, MaxIdleTime);
            _precomputeTimer = 0f;
            _animatorTakenOver = false;
            Machine.Owner.ResetPath();
            Machine.Owner.HasChangedMovementState = false;

            // Setup animation (mais on ne désactive pas l'Animator tout de suite)
            _animator = Machine.Owner._animator;
            if (_animator != null)
            {
                _clip = DroidHelpers.GetAnimationClip(_animator, ClipName);
                if (_clip != null)
                {
                    _targetObject = _animator.gameObject;
                }
            }

            // Démarrer le pré-calcul immédiatement
            Machine.Owner.StartPrecomputeDestination();
        }

        public override void OnStateUpdate()
        {
            base.OnStateUpdate();

            // Attendre le blend avant de prendre le contrôle
            if (!_animatorTakenOver && _clip != null && _targetObject != null)
            {
                if (TimeElapsed >= BlendDelay)
                {
                    _animator.enabled = false;
                    _currentTime = IdleLoopStart;
                    _animatorTakenOver = true;
                }
            }

            // Jouer l'animation idle en boucle (seulement après avoir pris le contrôle)
            if (_animatorTakenOver && _clip != null && _targetObject != null)
            {
                _currentTime += Time.deltaTime;
                if (_currentTime >= IdleLoopEnd)
                {
                    _currentTime = IdleLoopStart;
                }

                _clip.SampleAnimation(_targetObject, _currentTime);
            }

            // Réessayer le pré-calcul si pas encore de destination
            if (!Machine.Owner.HasPrecomputedDestination)
            {
                _precomputeTimer -= Time.deltaTime;
                if (_precomputeTimer <= 0f)
                {
                    Machine.Owner.StartPrecomputeDestination();
                    _precomputeTimer = PrecomputeRetryInterval;
                }
            }

            if (TimeElapsed >= _duration)
            {
                // Vérifier si on peut stalk (joueur trop loin)
                var distanceToPlayer = Machine.Owner.GetDistanceToPlayer();
                if (distanceToPlayer > StalkMinDistance && Random.value < StalkChance)
                {
                    Machine.NextStateStateId = StateId.StalkApproach;
                    return;
                }

                // Choisir le prochain état normal
                var roll = Random.value;
                if (roll < CheckMapChance)
                {
                    Machine.NextStateStateId = StateId.CheckMap;
                }
                else
                {
                    Machine.NextStateStateId = Random.value < SprintChance ? StateId.Sprint : StateId.Wander;
                }
            }
        }

        public override void OnStateExit(StateId next)
        {
            base.OnStateExit(next);

            // Réactiver l'Animator sauf si on va vers CheckMap (qui gère son propre contrôle)
            if (_animator != null && next != StateId.CheckMap)
            {
                _animator.enabled = true;
            }
        }
    }

    private abstract class MovementState : StateMachineBase<StateMachine, StateId>.StateBaseTimed
    {
        private const float SwitchCheckInterval = 1f;
        private const float SwitchChance = 0.2f;

        private float _duration;
        private float _nextSwitchCheck;

        protected abstract float MinDuration { get; }
        protected abstract float MaxDuration { get; }
        protected abstract float Speed { get; }
        protected abstract StateId SwitchTargetState { get; }
        protected abstract void SetMovementFlag(bool value);

        public override void OnStateEnter(StateId previous)
        {
            base.OnStateEnter(previous);
            _duration = Random.Range(MinDuration, MaxDuration);
            _nextSwitchCheck = SwitchCheckInterval;

            // Si on vient d'un autre état de mouvement (Wander ↔ Sprint), garder la même destination
            var isMovementSwitch = previous is StateId.Wander or StateId.Sprint;
            if (!isMovementSwitch)
            {
                // Nouvelle destination nécessaire (depuis Idle)
                if (!Machine.Owner.TrySetRandomDestination())
                {
                    Machine.NextStateStateId = StateId.Idle;
                    return;
                }
            }

            // Juste changer la vitesse
            Machine.Owner.SetSpeed(Speed);
            SetMovementFlag(true);
        }

        public override void OnStateExit(StateId next)
        {
            base.OnStateExit(next);
            SetMovementFlag(false);
        }

        public override void OnStateUpdate()
        {
            base.OnStateUpdate();

            if (Machine.Owner.HasReachedDestination() || TimeElapsed >= _duration)
            {
                Machine.NextStateStateId = StateId.Idle;
                return;
            }

            // 20% de chance de changer d'état (une seule fois)
            if (!Machine.Owner.HasChangedMovementState && TimeElapsed >= _nextSwitchCheck)
            {
                _nextSwitchCheck += SwitchCheckInterval;
                if (Random.value < SwitchChance)
                {
                    Machine.Owner.HasChangedMovementState = true;
                    Machine.NextStateStateId = SwitchTargetState;
                }
            }
        }
    }

    private class WanderState : MovementState
    {
        protected override float MinDuration => 5f;
        protected override float MaxDuration => 15f;
        protected override float Speed => WalkSpeed;
        protected override StateId SwitchTargetState => StateId.Sprint;

        protected override void SetMovementFlag(bool value)
        {
            Machine.Owner.IsWalking = value;
        }
    }

    private class SprintState : MovementState
    {
        protected override float MinDuration => 3f;
        protected override float MaxDuration => 8f;
        protected override float Speed => SprintSpeed;
        protected override StateId SwitchTargetState => StateId.Wander;

        protected override void SetMovementFlag(bool value)
        {
            Machine.Owner.IsSprinting = value;
        }
    }

    private class CheckMapState : StateMachineBase<StateMachine, StateId>.StateBaseTimed
    {
        private const string ClipName = "LostDroidStand";

        // Timestamps en secondes (basés sur les keyframes de l'animation)
        private const float RaiseStartTime = 4.65f;
        private const float LookStartTime = 5.5f;
        private const float LookEndTime = 6.9333334f;
        private const float LowerEndTime = 7.15f;

        // Durée de la phase de regard (configurable)
        private const float MinLookDuration = 0.5f;
        private const float MaxLookDuration = 3f;

        private Animator _animator;
        private AnimationClip _clip;

        private Phase _currentPhase;
        private float _currentTime;
        private float _lookDuration;
        private float _lookTimer;
        private GameObject _targetObject;

        public override void OnStateEnter(StateId previous)
        {
            base.OnStateEnter(previous);

            _animator = Machine.Owner._animator;
            if (_animator == null)
            {
                LOG.Warning("CheckMapState: Animator is null!");
                Machine.NextStateStateId = StateId.Idle;
                return;
            }

            // Trouver le clip
            _clip = DroidHelpers.GetAnimationClip(_animator, ClipName);
            if (_clip == null)
            {
                LOG.Warning($"CheckMapState: Clip '{ClipName}' not found!");
                Machine.NextStateStateId = StateId.Idle;
                return;
            }

            _targetObject = _animator.gameObject;

            // Désactiver les flags de mouvement
            Machine.Owner.IsWalking = false;
            Machine.Owner.IsSprinting = false;
            Machine.Owner.ResetPath();

            // Désactiver l'Animator pour prendre le contrôle direct
            _animator.enabled = false;

            // Configurer la durée de regard
            _lookDuration = Random.Range(MinLookDuration, MaxLookDuration);
            _lookTimer = 0f;

            // Démarrer la phase de lever
            _currentPhase = Phase.Raise;
            _currentTime = RaiseStartTime;

            LOG.Debug($"CheckMapState: ENTER - Clip length={_clip.length}s, starting at {_currentTime}s");
            LOG.Debug(
                $"CheckMapState: Thresholds - Raise={RaiseStartTime}s, Look={LookStartTime}s-{LookEndTime}s, Lower={LowerEndTime}s");
            LOG.Debug($"CheckMapState: LookDuration={_lookDuration:F1}s");

            // Appliquer la première frame
            _clip.SampleAnimation(_targetObject, _currentTime);
        }

        public override void OnStateUpdate()
        {
            base.OnStateUpdate();

            if (_clip == null || _targetObject == null)
            {
                Machine.NextStateStateId = StateId.Idle;
                return;
            }

            // Avancer le temps
            _currentTime += Time.deltaTime;

            switch (_currentPhase)
            {
                case Phase.Raise:
                    if (_currentTime >= LookStartTime)
                    {
                        LOG.Debug("CheckMapState: Transitioning to Phase.Look");
                        _currentPhase = Phase.Look;
                        _lookTimer = 0f;
                    }

                    break;

                case Phase.Look:
                    _lookTimer += Time.deltaTime;

                    if (_currentTime >= LookEndTime)
                    {
                        if (_lookTimer < _lookDuration)
                        {
                            // Boucler
                            _currentTime = LookStartTime;
                        }
                        else
                        {
                            LOG.Debug("CheckMapState: Transitioning to Phase.Lower");
                            _currentPhase = Phase.Lower;
                        }
                    }

                    break;

                case Phase.Lower:
                    if (_currentTime >= LowerEndTime)
                    {
                        LOG.Debug("CheckMapState: Animation complete, returning to Idle");
                        Machine.NextStateStateId = StateId.Idle;
                        return;
                    }

                    break;
            }

            // Appliquer l'animation
            _clip.SampleAnimation(_targetObject, _currentTime);
        }

        public override void OnStateExit(StateId next)
        {
            base.OnStateExit(next);
            LOG.Debug($"CheckMapState: EXIT -> {next}");

            // Réactiver l'Animator
            if (_animator != null)
            {
                _animator.enabled = true;
            }
        }

        private enum Phase
        {
            Raise,
            Look,
            Lower
        }
    }

    /// <summary>
    ///     État de stalk : le droid court vers le joueur.
    /// </summary>
    private class StalkApproachState : StateMachineBase<StateMachine, StateId>.StateBaseTimed
    {
        private const float MaxApproachTime = 30f;
        private const float DestinationUpdateInterval = 1f;
        private float _nextDestinationUpdate;

        public override void OnStateEnter(StateId previous)
        {
            base.OnStateEnter(previous);
            LOG.Debug("=== STALK: Approach started - running towards player ===");

            Machine.Owner.IsSprinting = true;
            Machine.Owner.SetSpeed(SprintSpeed);
            _nextDestinationUpdate = 0f;

            if (!Machine.Owner.TrySetDestinationToPlayer())
            {
                LOG.Debug("STALK: Failed to set destination to player, aborting");
                Machine.NextStateStateId = StateId.Idle;
            }
        }

        public override void OnStateUpdate()
        {
            base.OnStateUpdate();

            var distance = Machine.Owner.GetDistanceToPlayer();
            var reachedNavDest = Machine.Owner.HasReachedDestination();

            // Timeout
            if (TimeElapsed >= MaxApproachTime)
            {
                LOG.Debug($"STALK: Approach timeout, dist={distance:F1}m, aborting");
                Machine.NextStateStateId = StateId.Idle;
                return;
            }

            // Vérifier si on est arrivé près du joueur
            if (distance <= StalkArrivalDistance)
            {
                LOG.Debug($"STALK: Arrived near player (distance={distance:F1}m)");
                Machine.NextStateStateId = StateId.StalkStare;
                return;
            }

            // Si on a atteint la destination nav mais pas le joueur, passer en stare quand même
            // (le joueur est probablement inaccessible directement)
            if (reachedNavDest)
            {
                LOG.Debug($"STALK: Reached nav destination but player still at {distance:F1}m, starting stare anyway");
                Machine.NextStateStateId = StateId.StalkStare;
                return;
            }

            // Mettre à jour la destination périodiquement (le joueur bouge)
            _nextDestinationUpdate -= Time.deltaTime;
            if (_nextDestinationUpdate <= 0f)
            {
                Machine.Owner.TrySetDestinationToPlayer();
                _nextDestinationUpdate = DestinationUpdateInterval;
            }
        }

        public override void OnStateExit(StateId next)
        {
            base.OnStateExit(next);
            Machine.Owner.IsSprinting = false;
            Machine.Owner.ResetPath();
        }
    }

    /// <summary>
    ///     État de stalk : le droid fixe le joueur tout en s'approchant.
    ///     Si le joueur le regarde, il maintient le regard X secondes puis fuit.
    /// </summary>
    private class StalkStareState : StateMachineBase<StateMachine, StateId>.StateBaseTimed
    {
        private const float MaxStareTime = 30f;
        private const float DestinationUpdateInterval = 0.5f;

        private float _fleeCountdown;
        private bool _hasBeenSeen;
        private float _nextDestinationUpdate;

        public override void OnStateEnter(StateId previous)
        {
            base.OnStateEnter(previous);
            LOG.Debug("=== STALK: Staring at player ===");

            _hasBeenSeen = false;
            _fleeCountdown = StalkStareBeforeFlee;
            _nextDestinationUpdate = 0f;

            // Commencer à marcher lentement vers le joueur
            Machine.Owner.IsWalking = true;
            Machine.Owner.IsSprinting = false;
            Machine.Owner.SetSpeed(WalkSpeed);
        }

        public override void OnStateUpdate()
        {
            base.OnStateUpdate();

            // Toujours regarder le joueur
            Machine.Owner.LookAtPlayer();

            var distance = Machine.Owner.GetDistanceToPlayer();
            var isPlayerLooking = Machine.Owner.IsPlayerLookingAtMe;

            // Le joueur nous a vu !
            if (isPlayerLooking && !_hasBeenSeen)
            {
                _hasBeenSeen = true;
                LOG.Debug("=== STALK: Player sees me! Maintaining eye contact... ===");

                // Arrêter de bouger, fixer le joueur
                Machine.Owner.IsWalking = false;
                Machine.Owner.ResetPath();
            }

            // Si on a été vu, countdown avant de fuir
            if (_hasBeenSeen)
            {
                _fleeCountdown -= Time.deltaTime;
                if (_fleeCountdown <= 0f)
                {
                    LOG.Debug("=== STALK: Eye contact over, fleeing! ===");
                    Machine.NextStateStateId = StateId.StalkFlee;
                    return;
                }
            }
            else
            {
                // Pas encore vu : continuer à s'approcher (mais maintenir distance min)
                if (distance > StalkMinKeepDistance)
                {
                    _nextDestinationUpdate -= Time.deltaTime;
                    if (_nextDestinationUpdate <= 0f)
                    {
                        Machine.Owner.TrySetDestinationToPlayer();
                        _nextDestinationUpdate = DestinationUpdateInterval;
                    }
                }
                else
                {
                    // Trop proche, arrêter
                    Machine.Owner.ResetPath();
                }
            }

            // Timeout global
            if (TimeElapsed >= MaxStareTime)
            {
                LOG.Debug("STALK: Stare timeout, fleeing");
                Machine.NextStateStateId = StateId.StalkFlee;
            }
        }

        public override void OnStateExit(StateId next)
        {
            base.OnStateExit(next);
            Machine.Owner.IsWalking = false;
        }
    }

    /// <summary>
    ///     État de stalk : le droid fuit le joueur.
    /// </summary>
    private class StalkFleeState : StateMachineBase<StateMachine, StateId>.StateBaseTimed
    {
        private const float MaxFleeTime = 10f;

        public override void OnStateEnter(StateId previous)
        {
            base.OnStateEnter(previous);
            LOG.Debug("=== STALK: Fleeing from player! ===");

            Machine.Owner.IsSprinting = true;
            Machine.Owner.SetSpeed(SprintSpeed);

            if (!Machine.Owner.TrySetFleeDestination())
            {
                LOG.Debug("STALK: Failed to set flee destination, going to idle");
                Machine.NextStateStateId = StateId.Idle;
            }
        }

        public override void OnStateUpdate()
        {
            base.OnStateUpdate();

            // Vérifier si on a atteint la destination ou timeout
            if (Machine.Owner.HasReachedDestination() || TimeElapsed >= MaxFleeTime)
            {
                LOG.Debug("=== STALK: Flee complete, returning to idle ===");
                Machine.NextStateStateId = StateId.Idle;
            }
        }

        public override void OnStateExit(StateId next)
        {
            base.OnStateExit(next);
            Machine.Owner.IsSprinting = false;
        }
    }

    #endregion
}